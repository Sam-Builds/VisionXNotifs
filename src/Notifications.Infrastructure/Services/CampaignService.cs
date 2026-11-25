using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Application.Routing;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class CampaignService : ICampaignService
{
    private readonly NotificationsDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly IPreferenceService _preferenceService;
    private readonly IAudienceResolverClient _audienceResolver;
    private readonly IPushNotificationProvider _pushProvider;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<CampaignService> _logger;

    public CampaignService(
        NotificationsDbContext db,
        INotificationService notificationService,
        IPreferenceService preferenceService,
        IAudienceResolverClient audienceResolver,
        IPushNotificationProvider pushProvider,
        IAuditLogService auditLog,
        ILogger<CampaignService> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _preferenceService = preferenceService;
        _audienceResolver = audienceResolver;
        _pushProvider = pushProvider;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<CampaignDto> CreateCampaignAsync(string actorId, string actorEmail, CreateCampaignRequest request)
    {
        var categoryCode = request.CategoryCode.ToLowerInvariant();

        // Validate route if specified
        if (!string.IsNullOrWhiteSpace(request.RouteKey))
        {
            var routeValidation = RouteRegistry.ValidateRoute(request.RouteKey, request.RouteParameters);
            if (!routeValidation.IsValid)
            {
                throw new ArgumentException($"Invalid route configuration: {routeValidation.ErrorMessage}");
            }
        }

        var audienceJson = request.Audience != null ? JsonSerializer.Serialize(request.Audience) : null;
        var routeParamsJson = request.RouteParameters != null && request.RouteParameters.Count > 0
            ? JsonSerializer.Serialize(request.RouteParameters)
            : null;

        var status = CampaignStatus.Draft;
        if (request.SendImmediately)
        {
            status = CampaignStatus.Approved;
        }
        else if (request.ScheduledAt.HasValue)
        {
            status = CampaignStatus.Scheduled;
        }

        var campaign = new NotificationCampaign
        {
            Name = request.Name,
            Description = request.Description,
            CategoryCode = categoryCode,
            Title = request.Title,
            Body = request.Body,
            ImageUrl = request.ImageUrl,
            PayloadJson = request.PayloadJson,
            RouteKey = request.RouteKey,
            RouteParametersJson = routeParamsJson,
            Priority = request.Priority,
            AudienceDefinitionJson = audienceJson,
            ScheduledAt = request.ScheduledAt,
            ExpiresAt = request.ExpiresAt,
            Status = status,
            CreatedBy = actorEmail,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.NotificationCampaigns.Add(campaign);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(
            actorId,
            actorEmail,
            "campaign.created",
            "Campaign",
            campaign.Id.ToString(),
            new { campaign.Name, campaign.Status, request.SendImmediately });

        if (request.SendImmediately)
        {
            await ExecuteCampaignDispatchAsync(campaign, actorEmail);
        }

        return await GetCampaignByIdAsync(campaign.Id)
            ?? throw new InvalidOperationException("Failed to retrieve newly created campaign.");
    }

    public async Task<List<CampaignDto>> GetCampaignsAsync(int limit, int offset, string? status)
    {
        var query = _db.NotificationCampaigns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CampaignStatus>(status, true, out var filterStatus))
        {
            query = query.Where(c => c.Status == filterStatus);
        }

        var campaigns = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var result = new List<CampaignDto>();
        foreach (var c in campaigns)
        {
            result.Add(await MapToDtoAsync(c));
        }

        return result;
    }

    public async Task<CampaignDto?> GetCampaignByIdAsync(Guid id)
    {
        var campaign = await _db.NotificationCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        return campaign != null ? await MapToDtoAsync(campaign) : null;
    }

    public async Task<AudiencePreviewResponse> PreviewAudienceAsync(AudienceFilterDto? audience, string categoryCode)
    {
        var filter = audience ?? new AudienceFilterDto { TargetType = "All" };
        var userIds = await _audienceResolver.ResolveAudienceUserIdsAsync(filter);

        var devicesCount = await _db.Devices
            .AsNoTracking()
            .Where(d => d.IsActive && userIds.Contains(d.UserId))
            .CountAsync();

        // Check preferences suppression estimate
        int suppressedCount = 0;
        foreach (var uid in userIds.Take(200)) // Sample
        {
            var enabled = await _preferenceService.IsPushEnabledForCategoryAsync(uid, categoryCode.ToLowerInvariant());
            if (!enabled) suppressedCount++;
        }

        var suppressionRatio = userIds.Count > 0 ? (double)suppressedCount / Math.Min(userIds.Count, 200) : 0;
        var estimatedSuppressed = (int)(userIds.Count * suppressionRatio);

        var summary = filter.TargetType switch
        {
            "All" => "All registered students and personnel",
            "Students" => "All active enrolled students",
            "Faculty" => "Faculty members",
            "Department" => $"Department ID: {filter.DepartmentId}",
            "Semester" => $"Semester {filter.Semester}",
            "CourseOffering" => $"Course Offering ID: {filter.CourseOfferingId}",
            "Section" => $"Section ID: {filter.SectionId}",
            "Club" => $"Club ID: {filter.ClubId}",
            "SpecificUsers" => $"{filter.SpecificUserIds?.Count ?? 0} targeted individual user(s)",
            _ => "Custom filter"
        };

        return new AudiencePreviewResponse
        {
            EstimatedRecipients = userIds.Count,
            EstimatedPushEligible = Math.Max(0, devicesCount - estimatedSuppressed),
            EstimatedPreferenceSuppressed = estimatedSuppressed,
            SampleUserIds = userIds.Take(10).ToList(),
            AudienceSummary = summary
        };
    }

    public async Task<TestNotificationResult> SendTestNotificationAsync(
        string actorId,
        string actorEmail,
        TestNotificationRequest request)
    {
        _logger.LogInformation("Sending test notification initiated by {ActorEmail}", actorEmail);

        // Find test target devices: either specific installation ID or the actor's own registered devices
        List<Device> targetDevices;

        if (!string.IsNullOrWhiteSpace(request.TargetInstallationId))
        {
            targetDevices = await _db.Devices
                .Where(d => d.InstallationId == request.TargetInstallationId && d.IsActive)
                .ToListAsync();
        }
        else
        {
            // Find active devices registered to actor
            targetDevices = await _db.Devices
                .Where(d => d.UserId == actorId && d.IsActive)
                .ToListAsync();

            if (targetDevices.Count == 0)
            {
                // Fallback to most recently seen active device
                var recentDevice = await _db.Devices
                    .Where(d => d.IsActive)
                    .OrderByDescending(d => d.LastSeenAt)
                    .FirstOrDefaultAsync();

                if (recentDevice != null)
                {
                    targetDevices.Add(recentDevice);
                }
            }
        }

        if (targetDevices.Count == 0)
        {
            return new TestNotificationResult
            {
                Success = false,
                Message = "No active registered devices found to receive the test notification.",
                DevicesTargeted = 0,
                Status = "NoTargetDevices"
            };
        }

        var notification = await _notificationService.CreateNotificationRecordAsync(
            userId: actorId,
            title: request.Title,
            body: request.Body,
            categoryCode: request.CategoryCode,
            routeKey: request.RouteKey,
            routeParams: request.RouteParameters,
            source: "TestNotification",
            priority: NotificationPriority.High);

        string? providerMessageId = null;
        var deliveredCount = 0;

        foreach (var device in targetDevices)
        {
            var pushMsg = new PushMessage
            {
                FcmToken = device.FcmToken,
                Title = request.Title,
                Body = request.Body,
                Priority = "High",
                RouteKey = request.RouteKey,
                RouteParameters = request.RouteParameters,
                CustomData = request.ExtraData,
                NotificationId = notification.Id.ToString(),
                CategoryCode = request.CategoryCode,
                CreatedAtUtc = notification.CreatedAt
            };

            var sendResult = await _pushProvider.SendPushAsync(pushMsg);
            if (sendResult.Success)
            {
                deliveredCount++;
                providerMessageId = sendResult.ProviderMessageId;

                device.LastSuccessfulDeliveryAt = DateTime.UtcNow;
                var delRecord = await _db.NotificationDeliveries
                    .FirstOrDefaultAsync(d => d.NotificationId == notification.Id && d.DeviceId == device.Id);

                if (delRecord != null)
                {
                    delRecord.Status = DeliveryStatus.Delivered;
                    delRecord.AcceptedAt = DateTime.UtcNow;
                    delRecord.ProviderMessageId = sendResult.ProviderMessageId;
                }
            }
            else
            {
                var delRecord = await _db.NotificationDeliveries
                    .FirstOrDefaultAsync(d => d.NotificationId == notification.Id && d.DeviceId == device.Id);

                if (delRecord != null)
                {
                    delRecord.Status = DeliveryStatus.Failed;
                    delRecord.LastErrorCode = sendResult.ErrorCode;
                    delRecord.LastErrorMessage = sendResult.ErrorMessage;
                }
            }
        }

        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(
            actorId,
            actorEmail,
            "notification.test_sent",
            "Notification",
            notification.Id.ToString(),
            new { DevicesTargeted = targetDevices.Count, DeliveredCount = deliveredCount });

        return new TestNotificationResult
        {
            Success = deliveredCount > 0,
            NotificationId = notification.Id,
            DevicesTargeted = targetDevices.Count,
            Status = deliveredCount > 0 ? "Delivered" : "Failed",
            Message = $"Dispatched test notification to {targetDevices.Count} device(s) ({deliveredCount} successful).",
            ProviderMessageId = providerMessageId
        };
    }

    public async Task<bool> ScheduleOrSendCampaignAsync(Guid id, string actorEmail)
    {
        var campaign = await _db.NotificationCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null || campaign.Status == CampaignStatus.Completed || campaign.Status == CampaignStatus.Cancelled)
        {
            return false;
        }

        if (campaign.ScheduledAt.HasValue && campaign.ScheduledAt.Value > DateTime.UtcNow)
        {
            campaign.Status = CampaignStatus.Scheduled;
            campaign.ApprovedBy = actorEmail;
            campaign.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        campaign.Status = CampaignStatus.Approved;
        campaign.ApprovedBy = actorEmail;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await ExecuteCampaignDispatchAsync(campaign, actorEmail);
        return true;
    }

    public async Task<bool> CancelCampaignAsync(Guid id, string actorEmail)
    {
        var campaign = await _db.NotificationCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null || campaign.Status == CampaignStatus.Completed)
        {
            return false;
        }

        campaign.Status = CampaignStatus.Cancelled;
        campaign.CancelledAt = DateTime.UtcNow;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync(null, actorEmail, "campaign.cancelled", "Campaign", id.ToString(), null);
        return true;
    }

    public async Task<List<DeliveryRecordDto>> GetCampaignDeliveriesAsync(Guid campaignId, int limit, int offset)
    {
        var deliveries = await _db.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Include(d => d.Device)
            .Where(d => d.Notification != null && d.Notification.CampaignId == campaignId)
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return deliveries.Select(ToDeliveryDto).ToList();
    }

    public async Task<List<DeliveryRecordDto>> GetAllDeliveriesAsync(int limit, int offset, string? status, string? failureReason)
    {
        var query = _db.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Include(d => d.Device)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DeliveryStatus>(status, true, out var ds))
        {
            query = query.Where(d => d.Status == ds);
        }

        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            var fr = failureReason.ToLower();
            query = query.Where(d => d.LastErrorCode != null && d.LastErrorCode.ToLower().Contains(fr));
        }

        var deliveries = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return deliveries.Select(ToDeliveryDto).ToList();
    }

    public async Task<AdminNotificationOverviewDto> GetOverviewMetricsAsync()
    {
        var totalNotifications = await _db.Notifications.CountAsync();
        var totalCampaigns = await _db.NotificationCampaigns.CountAsync();
        var scheduledCampaigns = await _db.NotificationCampaigns.CountAsync(c => c.Status == CampaignStatus.Scheduled);
        var pendingJobs = await _db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Pending);
        var delivered = await _db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Delivered);
        var failed = await _db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Failed);
        var suppressed = await _db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Suppressed);
        var activeDevices = await _db.Devices.CountAsync(d => d.IsActive);
        var totalDevices = await _db.Devices.CountAsync();
        var uniqueUsers = await _db.Notifications.Select(n => n.UserId).Distinct().CountAsync();

        var recent = await _db.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Include(d => d.Device)
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .ToListAsync();

        return new AdminNotificationOverviewDto
        {
            TotalNotificationsCreated = totalNotifications,
            TotalCampaigns = totalCampaigns,
            ActiveScheduledCampaigns = scheduledCampaigns,
            PendingDeliveryJobs = pendingJobs,
            TotalAcceptedByProvider = delivered,
            TotalFailedDeliveries = failed,
            TotalSuppressedDeliveries = suppressed,
            ActiveRegisteredDevices = activeDevices,
            TotalRegisteredDevices = totalDevices,
            TotalUniqueUsersReached = uniqueUsers,
            RecentDeliveries = recent.Select(ToDeliveryDto).ToList()
        };
    }

    private async Task ExecuteCampaignDispatchAsync(NotificationCampaign campaign, string actorEmail)
    {
        campaign.Status = CampaignStatus.Processing;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            AudienceFilterDto? filter = null;
            if (!string.IsNullOrWhiteSpace(campaign.AudienceDefinitionJson))
            {
                filter = JsonSerializer.Deserialize<AudienceFilterDto>(campaign.AudienceDefinitionJson);
            }

            var targetUserIds = await _audienceResolver.ResolveAudienceUserIdsAsync(filter ?? new AudienceFilterDto { TargetType = "All" });

            Dictionary<string, string>? routeParams = null;
            if (!string.IsNullOrWhiteSpace(campaign.RouteParametersJson))
            {
                routeParams = JsonSerializer.Deserialize<Dictionary<string, string>>(campaign.RouteParametersJson);
            }

            _logger.LogInformation("Executing dispatch for Campaign {CampaignId} ('{Name}') to {UserCount} recipients",
                campaign.Id, campaign.Name, targetUserIds.Count);

            foreach (var userId in targetUserIds)
            {
                var recipientRecord = new CampaignRecipient
                {
                    CampaignId = campaign.Id,
                    UserId = userId,
                    RecipientStatus = RecipientStatus.Dispatched,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CampaignRecipients.Add(recipientRecord);

                var notification = await _notificationService.CreateNotificationRecordAsync(
                    userId: userId,
                    title: campaign.Title,
                    body: campaign.Body,
                    categoryCode: campaign.CategoryCode,
                    routeKey: campaign.RouteKey,
                    routeParams: routeParams,
                    campaignId: campaign.Id,
                    source: "Campaign",
                    priority: campaign.Priority,
                    expiresAt: campaign.ExpiresAt,
                    payloadJson: campaign.PayloadJson,
                    imageUrl: campaign.ImageUrl);

                recipientRecord.NotificationId = notification.Id;
            }

            campaign.Status = CampaignStatus.Completed;
            campaign.SentAt = DateTime.UtcNow;
            campaign.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                null,
                actorEmail,
                "campaign.dispatched",
                "Campaign",
                campaign.Id.ToString(),
                new { RecipientsCount = targetUserIds.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch campaign {CampaignId}", campaign.Id);
            campaign.Status = CampaignStatus.Failed;
            campaign.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    private async Task<CampaignDto> MapToDtoAsync(NotificationCampaign c)
    {
        AudienceFilterDto? audience = null;
        if (!string.IsNullOrWhiteSpace(c.AudienceDefinitionJson))
        {
            try { audience = JsonSerializer.Deserialize<AudienceFilterDto>(c.AudienceDefinitionJson); } catch { }
        }

        Dictionary<string, string>? routeParams = null;
        if (!string.IsNullOrWhiteSpace(c.RouteParametersJson))
        {
            try { routeParams = JsonSerializer.Deserialize<Dictionary<string, string>>(c.RouteParametersJson); } catch { }
        }

        var totalRecipients = await _db.CampaignRecipients.CountAsync(r => r.CampaignId == c.Id);
        var deliveries = _db.NotificationDeliveries
            .Where(d => d.Notification != null && d.Notification.CampaignId == c.Id);

        var deliveredCount = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Delivered);
        var failedCount = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Failed);
        var suppressedCount = await deliveries.CountAsync(d => d.Status == DeliveryStatus.Suppressed);

        return new CampaignDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            CategoryCode = c.CategoryCode,
            Title = c.Title,
            Body = c.Body,
            ImageUrl = c.ImageUrl,
            PayloadJson = c.PayloadJson,
            RouteKey = c.RouteKey,
            RouteParameters = routeParams,
            Priority = c.Priority,
            Audience = audience,
            ScheduledAt = c.ScheduledAt,
            ExpiresAt = c.ExpiresAt,
            Status = c.Status,
            CreatedBy = c.CreatedBy,
            ApprovedBy = c.ApprovedBy,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            SentAt = c.SentAt,
            CancelledAt = c.CancelledAt,
            TotalRecipients = totalRecipients,
            DeliveredCount = deliveredCount,
            FailedCount = failedCount,
            SuppressedCount = suppressedCount
        };
    }

    public async Task<CampaignDto?> UpdateCampaignAsync(Guid id, CreateCampaignRequest request, string actorEmail)
    {
        var campaign = await _db.NotificationCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null || campaign.Status == CampaignStatus.Completed || campaign.Status == CampaignStatus.Cancelled)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.RouteKey))
        {
            var routeValidation = RouteRegistry.ValidateRoute(request.RouteKey, request.RouteParameters);
            if (!routeValidation.IsValid)
            {
                throw new ArgumentException($"Invalid route configuration: {routeValidation.ErrorMessage}");
            }
        }

        campaign.Name = request.Name;
        campaign.Description = request.Description;
        campaign.CategoryCode = request.CategoryCode.ToLowerInvariant();
        campaign.Title = request.Title;
        campaign.Body = request.Body;
        campaign.ImageUrl = request.ImageUrl;
        campaign.PayloadJson = request.PayloadJson;
        campaign.RouteKey = request.RouteKey;
        campaign.RouteParametersJson = request.RouteParameters != null && request.RouteParameters.Count > 0
            ? JsonSerializer.Serialize(request.RouteParameters)
            : null;
        campaign.Priority = request.Priority;
        campaign.AudienceDefinitionJson = request.Audience != null ? JsonSerializer.Serialize(request.Audience) : null;
        campaign.ScheduledAt = request.ScheduledAt;
        campaign.ExpiresAt = request.ExpiresAt;
        campaign.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("admin", actorEmail, "campaign.updated", "Campaign", campaign.Id.ToString(), new { campaign.Name, campaign.Status });

        return await MapToDtoAsync(campaign);
    }

    public async Task<bool> ApproveCampaignAsync(Guid id, string actorEmail)
    {
        var campaign = await _db.NotificationCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null || campaign.Status == CampaignStatus.Completed || campaign.Status == CampaignStatus.Cancelled)
        {
            return false;
        }

        campaign.Status = CampaignStatus.Approved;
        campaign.ApprovedBy = actorEmail;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("admin", actorEmail, "campaign.approved", "Campaign", campaign.Id.ToString(), new { campaign.Name });
        return true;
    }

    public async Task<bool> ScheduleCampaignAsync(Guid id, DateTime scheduledAt, string actorEmail)
    {
        var campaign = await _db.NotificationCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null || campaign.Status == CampaignStatus.Completed || campaign.Status == CampaignStatus.Cancelled)
        {
            return false;
        }

        campaign.Status = CampaignStatus.Scheduled;
        campaign.ScheduledAt = scheduledAt;
        campaign.ApprovedBy = actorEmail;
        campaign.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("admin", actorEmail, "campaign.scheduled", "Campaign", campaign.Id.ToString(), new { campaign.Name, scheduledAt });
        return true;
    }

    public async Task<List<NotificationTemplateDto>> GetTemplatesAsync()
    {
        var templates = await _db.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return templates.Select(t =>
        {
            List<string> vars = new();
            if (!string.IsNullOrWhiteSpace(t.SupportedVariablesJson))
            {
                try { vars = JsonSerializer.Deserialize<List<string>>(t.SupportedVariablesJson) ?? new(); } catch {}
            }
            return new NotificationTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                CategoryCode = t.CategoryCode,
                TitleTemplate = t.TitleTemplate,
                BodyTemplate = t.BodyTemplate,
                DefaultRouteKey = t.DefaultRouteKey,
                DefaultPriority = t.DefaultPriority,
                SupportedVariables = vars,
                IsActive = t.IsActive,
                Version = t.Version
            };
        }).ToList();
    }

    public async Task<NotificationTemplateDto> CreateTemplateAsync(CreateTemplateRequest request, string actorEmail)
    {
        var varsJson = request.SupportedVariables != null && request.SupportedVariables.Count > 0
            ? JsonSerializer.Serialize(request.SupportedVariables)
            : null;

        var template = new NotificationTemplate
        {
            Name = request.Name,
            CategoryCode = request.CategoryCode.ToLowerInvariant(),
            TitleTemplate = request.TitleTemplate,
            BodyTemplate = request.BodyTemplate,
            DefaultRouteKey = request.DefaultRouteKey,
            DefaultPriority = request.DefaultPriority,
            SupportedVariablesJson = varsJson,
            IsActive = true,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.NotificationTemplates.Add(template);
        await _db.SaveChangesAsync();

        await _auditLog.LogAsync("admin", actorEmail, "template.created", "Template", template.Id.ToString(), new { template.Name, template.CategoryCode });

        return new NotificationTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            CategoryCode = template.CategoryCode,
            TitleTemplate = template.TitleTemplate,
            BodyTemplate = template.BodyTemplate,
            DefaultRouteKey = template.DefaultRouteKey,
            DefaultPriority = template.DefaultPriority,
            SupportedVariables = request.SupportedVariables ?? new(),
            IsActive = template.IsActive,
            Version = template.Version
        };
    }

    public async Task<NotificationTemplateDto?> UpdateTemplateAsync(Guid id, CreateTemplateRequest request, string actorEmail)
    {
        var template = await _db.NotificationTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template == null) return null;

        template.Name = request.Name;
        template.CategoryCode = request.CategoryCode.ToLowerInvariant();
        template.TitleTemplate = request.TitleTemplate;
        template.BodyTemplate = request.BodyTemplate;
        template.DefaultRouteKey = request.DefaultRouteKey;
        template.DefaultPriority = request.DefaultPriority;
        template.SupportedVariablesJson = request.SupportedVariables != null ? JsonSerializer.Serialize(request.SupportedVariables) : null;
        template.Version++;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _auditLog.LogAsync("admin", actorEmail, "template.updated", "Template", template.Id.ToString(), new { template.Name, template.Version });

        return new NotificationTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            CategoryCode = template.CategoryCode,
            TitleTemplate = template.TitleTemplate,
            BodyTemplate = template.BodyTemplate,
            DefaultRouteKey = template.DefaultRouteKey,
            DefaultPriority = template.DefaultPriority,
            SupportedVariables = request.SupportedVariables ?? new(),
            IsActive = template.IsActive,
            Version = template.Version
        };
    }

    private static DeliveryRecordDto ToDeliveryDto(NotificationDelivery d)
    {
        return new DeliveryRecordDto
        {
            Id = d.Id,
            NotificationId = d.NotificationId,
            UserId = d.Notification?.UserId ?? "",
            CategoryCode = d.Notification?.CategoryCode ?? "",
            Title = d.Notification?.Title ?? "",
            DeviceId = d.DeviceId,
            Platform = d.Device?.Platform.ToString() ?? "",
            DeviceName = d.Device?.DeviceName ?? "",
            Provider = d.Provider,
            Status = d.Status,
            AttemptCount = d.AttemptCount,
            LastAttemptAt = d.LastAttemptAt,
            ProviderMessageId = d.ProviderMessageId,
            LastErrorCode = d.LastErrorCode,
            LastErrorMessage = d.LastErrorMessage,
            SuppressionReason = d.SuppressionReason,
            CreatedAt = d.CreatedAt
        };
    }
}
