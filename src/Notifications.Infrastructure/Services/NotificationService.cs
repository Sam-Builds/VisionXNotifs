using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly NotificationsDbContext _db;
    private readonly IPreferenceService _preferenceService;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        NotificationsDbContext db,
        IPreferenceService preferenceService,
        ISessionStateService sessionStateService,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _preferenceService = preferenceService;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    public async Task<PaginatedNotificationsResponse> GetUserNotificationsAsync(
        string userId,
        int limit,
        int offset,
        bool? unreadOnly,
        string? categoryCode)
    {
        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsArchived);

        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            query = query.Where(n => n.CategoryCode == categoryCode.ToLowerInvariant());
        }

        var totalCount = await query.CountAsync();
        var unreadCount = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsArchived && !n.IsRead)
            .CountAsync();

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return new PaginatedNotificationsResponse
        {
            Items = notifications.Select(ToItemDto).ToList(),
            TotalCount = totalCount,
            UnreadCount = unreadCount,
            Offset = offset,
            Limit = limit
        };
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsArchived && !n.IsRead)
            .CountAsync();
    }

    public async Task<NotificationItemDto?> GetNotificationByIdAsync(string userId, Guid id)
    {
        var item = await _db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && !n.IsArchived);

        return item != null ? ToItemDto(item) : null;
    }

    public async Task<bool> MarkAsReadAsync(string userId, Guid id)
    {
        var item = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

        if (item == null)
        {
            return false;
        }

        if (!item.IsRead)
        {
            item.IsRead = true;
            item.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(string userId)
    {
        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && !n.IsArchived)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var item in unread)
        {
            item.IsRead = true;
            item.ReadAt = now;
        }

        await _db.SaveChangesAsync();
        return unread.Count;
    }

    public async Task<bool> ArchiveNotificationAsync(string userId, Guid id)
    {
        var item = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

        if (item == null)
        {
            return false;
        }

        item.IsArchived = true;
        item.ArchivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<Notification> CreateNotificationRecordAsync(
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
        string? imageUrl = null)
    {
        var normalizedCat = categoryCode.ToLowerInvariant();
        var routeParamsJson = routeParams != null && routeParams.Count > 0
            ? JsonSerializer.Serialize(routeParams)
            : null;

        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Body = body,
            CategoryCode = normalizedCat,
            RouteKey = routeKey,
            RouteParametersJson = routeParamsJson,
            CampaignId = campaignId,
            EventId = eventId,
            Source = source,
            Priority = priority,
            ExpiresAt = expiresAt,
            PayloadJson = payloadJson,
            ImageUrl = imageUrl,
            CreatedAt = DateTime.UtcNow,
            IsRead = false,
            Status = "Created"
        };

        _db.Notifications.Add(notification);

        // Check if user is banned
        var isBanned = await _sessionStateService.IsUserBannedAsync(userId);

        // Check if push is enabled for this category
        var isPushEnabled = await _preferenceService.IsPushEnabledForCategoryAsync(userId, normalizedCat);

        // Fetch user's active devices
        var userDevices = await _db.Devices
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync();

        foreach (var device in userDevices)
        {
            var delivery = new NotificationDelivery
            {
                Notification = notification,
                Device = device,
                Provider = "FCM",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (isBanned)
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.UserBanned;
                delivery.LastErrorMessage = "User account is banned; push suppressed.";
            }
            else if (!isPushEnabled)
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.PreferenceDisabled;
                delivery.LastErrorMessage = $"Push notifications disabled by user preferences for category '{normalizedCat}'.";
            }
            else
            {
                delivery.Status = DeliveryStatus.Pending;
                delivery.NextAttemptAt = DateTime.UtcNow;
            }

            _db.NotificationDeliveries.Add(delivery);
            notification.Deliveries.Add(delivery);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Created notification {NotificationId} for user {UserId} with {DeviceCount} deliveries (PushEnabled={PushEnabled}, Banned={IsBanned})",
            notification.Id, userId, userDevices.Count, isPushEnabled, isBanned);

        return notification;
    }

    private static NotificationItemDto ToItemDto(Notification n)
    {
        Dictionary<string, string>? routeParams = null;
        if (!string.IsNullOrWhiteSpace(n.RouteParametersJson))
        {
            try
            {
                routeParams = JsonSerializer.Deserialize<Dictionary<string, string>>(n.RouteParametersJson);
            }
            catch
            {
                // Ignore deserialize failure
            }
        }

        return new NotificationItemDto
        {
            Id = n.Id,
            UserId = n.UserId,
            CampaignId = n.CampaignId,
            EventId = n.EventId,
            CategoryCode = n.CategoryCode,
            Title = n.Title,
            Body = n.Body,
            ImageUrl = n.ImageUrl,
            Priority = n.Priority.ToString(),
            PayloadJson = n.PayloadJson,
            RouteKey = n.RouteKey,
            RouteParameters = routeParams,
            CreatedAt = n.CreatedAt,
            ExpiresAt = n.ExpiresAt,
            IsRead = n.IsRead,
            ReadAt = n.ReadAt,
            Source = n.Source,
            Status = n.Status
        };
    }
}
