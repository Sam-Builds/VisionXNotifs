using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Application.Interfaces;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Workers;

public class NotificationDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationDeliveryWorker> _logger;

    public NotificationDeliveryWorker(IServiceProvider serviceProvider, ILogger<NotificationDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDeliveryWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDeliveriesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred during notification delivery processing cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("NotificationDeliveryWorker stopping.");
    }

    private async Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var pushProvider = scope.ServiceProvider.GetRequiredService<IPushNotificationProvider>();

        var now = DateTime.UtcNow;

        var pendingDeliveries = await db.NotificationDeliveries
            .Include(d => d.Notification)
            .Include(d => d.Device)
            .Where(d => d.Status == DeliveryStatus.Pending ||
                        (d.Status == DeliveryStatus.Failed && d.AttemptCount < 3 && d.NextAttemptAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (pendingDeliveries.Count == 0)
        {
            return;
        }

        _logger.LogInformation("NotificationDeliveryWorker processing {Count} pending deliveries...", pendingDeliveries.Count);

        foreach (var delivery in pendingDeliveries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (delivery.Device == null || !delivery.Device.IsActive)
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.DeviceInactive;
                delivery.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            if (string.IsNullOrWhiteSpace(delivery.Device.FcmToken) ||
                delivery.Device.FcmToken.StartsWith("device_", StringComparison.OrdinalIgnoreCase))
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.DeviceInactive;
                delivery.Device.IsActive = false;
                delivery.LastErrorMessage = "Invalid or dummy FCM token format; device deactivated.";
                delivery.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            if (delivery.Notification == null)
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.LastErrorMessage = "Associated notification entity not found.";
                delivery.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            if (delivery.Notification.ExpiresAt.HasValue && delivery.Notification.ExpiresAt.Value <= DateTime.UtcNow)
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.NotificationExpired;
                delivery.LastErrorMessage = "Notification has expired before delivery could be completed.";
                delivery.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            var sessionStateService = scope.ServiceProvider.GetRequiredService<ISessionStateService>();
            if (await sessionStateService.IsUserBannedAsync(delivery.Notification.UserId))
            {
                delivery.Status = DeliveryStatus.Suppressed;
                delivery.SuppressionReason = SuppressionReason.UserBanned;
                delivery.LastErrorMessage = "Recipient user account is banned; push delivery suppressed.";
                delivery.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            Dictionary<string, string>? routeParams = null;
            if (!string.IsNullOrWhiteSpace(delivery.Notification.RouteParametersJson))
            {
                try
                {
                    routeParams = JsonSerializer.Deserialize<Dictionary<string, string>>(delivery.Notification.RouteParametersJson);
                }
                catch
                {
                    // Ignore parse error
                }
            }

            var pushMessage = new PushMessage
            {
                FcmToken = delivery.Device.FcmToken,
                Title = delivery.Notification.Title,
                Body = delivery.Notification.Body,
                ImageUrl = delivery.Notification.ImageUrl,
                Priority = delivery.Notification.Priority.ToString(),
                RouteKey = delivery.Notification.RouteKey,
                RouteParameters = routeParams,
                NotificationId = delivery.Notification.Id.ToString(),
                CategoryCode = delivery.Notification.CategoryCode,
                CreatedAtUtc = delivery.Notification.CreatedAt
            };

            delivery.AttemptCount++;
            delivery.LastAttemptAt = DateTime.UtcNow;

            var sendResult = await pushProvider.SendPushAsync(pushMessage, cancellationToken);

            if (sendResult.Success)
            {
                delivery.Status = DeliveryStatus.Delivered;
                delivery.AcceptedAt = DateTime.UtcNow;
                delivery.ProviderMessageId = sendResult.ProviderMessageId;
                delivery.LastErrorCode = null;
                delivery.LastErrorMessage = null;
                delivery.Device.LastSuccessfulDeliveryAt = DateTime.UtcNow;
                delivery.Device.LastSeenAt = DateTime.UtcNow;
            }
            else
            {
                delivery.LastErrorCode = sendResult.ErrorCode;
                delivery.LastErrorMessage = sendResult.ErrorMessage;

                if (sendResult.IsInvalidToken)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.Device.IsActive = false;
                    _logger.LogInformation("Deactivated device {DeviceId} due to unregistered or invalid token", delivery.Device.Id);
                }
                else if (delivery.AttemptCount >= 3)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    _logger.LogWarning("Delivery {DeliveryId} reached max attempts (3). Marked as Failed.", delivery.Id);
                }
                else
                {
                    delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, delivery.AttemptCount) * 10);
                }
            }

            delivery.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
