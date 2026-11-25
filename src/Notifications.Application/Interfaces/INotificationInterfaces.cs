using Notifications.Application.DTOs;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;

namespace Notifications.Application.Interfaces;

public class PushMessage
{
    public required string FcmToken { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? ImageUrl { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public Dictionary<string, string>? CustomData { get; set; }
    public string? NotificationId { get; set; }
    public string? CategoryCode { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}

public class PushSendResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsInvalidToken { get; set; }
    public bool IsRateLimited { get; set; }
}

public interface IPushNotificationProvider
{
    string ProviderName { get; }
    Task<PushSendResult> SendPushAsync(PushMessage message, CancellationToken cancellationToken = default);
}

public interface IDeviceService
{
    Task<DeviceDto> RegisterDeviceAsync(string userId, DeviceRegistrationRequest request);
    Task<List<DeviceDto>> GetUserDevicesAsync(string userId);
    Task<bool> RemoveDeviceAsync(string userId, string installationId);
    Task<List<DeviceDto>> GetAdminDevicesAsync(int limit, int offset, string? search);
    Task<bool> DeactivateDeviceAsync(Guid deviceId, string actorEmail);
}

public interface INotificationService
{
    Task<PaginatedNotificationsResponse> GetUserNotificationsAsync(string userId, int limit, int offset, bool? unreadOnly, string? categoryCode);
    Task<int> GetUnreadCountAsync(string userId);
    Task<NotificationItemDto?> GetNotificationByIdAsync(string userId, Guid id);
    Task<bool> MarkAsReadAsync(string userId, Guid id);
    Task<int> MarkAllAsReadAsync(string userId);
    Task<bool> ArchiveNotificationAsync(string userId, Guid id);
    Task<Notification> CreateNotificationRecordAsync(
        string userId,
        string title,
        string body,
        string categoryCode,
        string? routeKey = null,
        Dictionary<string, string>? routeParams = null,
        Guid? campaignId = null,
        string? eventId = null,
        string source = "Direct",
        NotificationPriority priority = NotificationPriority.Normal,
        DateTime? expiresAt = null,
        string? payloadJson = null,
        string? imageUrl = null);
}

public interface IPreferenceService
{
    Task<UserPreferencesResponse> GetUserPreferencesAsync(string userId);
    Task<UserPreferencesResponse> UpdateUserPreferencesAsync(string userId, UpdatePreferencesRequest request);
    Task<List<NotificationCategoryDto>> GetActiveCategoriesAsync();
    Task<NotificationCategoryDto> CreateCategoryAsync(CreateNotificationCategoryRequest request);
    Task<NotificationCategoryDto?> UpdateCategoryAsync(Guid id, CreateNotificationCategoryRequest request);
    Task<bool> IsPushEnabledForCategoryAsync(string userId, string categoryCode);
}

public interface ICampaignService
{
    Task<CampaignDto> CreateCampaignAsync(string actorId, string actorEmail, CreateCampaignRequest request);
    Task<CampaignDto?> UpdateCampaignAsync(Guid id, CreateCampaignRequest request, string actorEmail);
    Task<bool> ApproveCampaignAsync(Guid id, string actorEmail);
    Task<bool> ScheduleCampaignAsync(Guid id, DateTime scheduledAt, string actorEmail);
    Task<List<CampaignDto>> GetCampaignsAsync(int limit, int offset, string? status);
    Task<CampaignDto?> GetCampaignByIdAsync(Guid id);
    Task<AudiencePreviewResponse> PreviewAudienceAsync(AudienceFilterDto? audience, string categoryCode);
    Task<TestNotificationResult> SendTestNotificationAsync(string actorId, string actorEmail, TestNotificationRequest request);
    Task<bool> ScheduleOrSendCampaignAsync(Guid id, string actorEmail);
    Task<bool> CancelCampaignAsync(Guid id, string actorEmail);
    Task<List<DeliveryRecordDto>> GetCampaignDeliveriesAsync(Guid campaignId, int limit, int offset);
    Task<List<DeliveryRecordDto>> GetAllDeliveriesAsync(int limit, int offset, string? status, string? failureReason);
    Task<AdminNotificationOverviewDto> GetOverviewMetricsAsync();
    Task<List<NotificationTemplateDto>> GetTemplatesAsync();
    Task<NotificationTemplateDto> CreateTemplateAsync(CreateTemplateRequest request, string actorEmail);
    Task<NotificationTemplateDto?> UpdateTemplateAsync(Guid id, CreateTemplateRequest request, string actorEmail);
}

public interface IAcademicEventProcessor
{
    Task<bool> ProcessInboundAcademicEventAsync(InboundAcademicEventDto eventDto);
}

public interface ISessionStateService
{
    Task ProcessSessionEventAsync(SessionEventWebhookDto eventDto);
    Task<bool> IsSessionAllowedAsync(string sessionId, string userId);
    Task<bool> IsUserBannedAsync(string userId);
    bool VerifyWebhookSignature(string rawBody, string timestamp, string signature, string secret);
}

public interface IAudienceResolverClient
{
    Task<List<string>> ResolveAudienceUserIdsAsync(AudienceFilterDto filter, CancellationToken cancellationToken = default);
}

public interface IAuditLogService
{
    Task LogAsync(string? actorId, string? actorEmail, string action, string targetType, string? targetId, object? details, string? correlationId = null, string? clientIp = null);
    Task<List<NotificationAuditLog>> GetAuditLogsAsync(int limit, int offset, string? action);
}
