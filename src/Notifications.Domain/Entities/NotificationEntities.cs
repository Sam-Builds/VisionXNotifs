using Notifications.Domain.Enums;

namespace Notifications.Domain.Entities;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string InstallationId { get; set; }
    public required string UserId { get; set; }
    public required string FcmToken { get; set; }
    public DevicePlatform Platform { get; set; } = DevicePlatform.Android;
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? OSVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime TokenUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSuccessfulDeliveryAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? IconIdentifier { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsUserConfigurable { get; set; } = true;
    public bool IsSystemCategory { get; set; } = false;
    public bool DefaultEnabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public required string CategoryCode { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedFrom { get; set; }
    public long Version { get; set; } = 1;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public Guid? CampaignId { get; set; }
    public NotificationCampaign? Campaign { get; set; }
    public string? EventId { get; set; }
    public required string CategoryCode { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? ImageUrl { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public string? PayloadJson { get; set; }
    public string? RouteKey { get; set; }
    public string? RouteParametersJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }
    public bool IsArchived { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }
    public string Source { get; set; } = "Direct";
    public string Status { get; set; } = "Created";

    public List<NotificationDelivery> Deliveries { get; set; } = new();
}

public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Notification? Notification { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public string Provider { get; set; } = "FCM";
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int AttemptCount { get; set; } = 0;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public SuppressionReason SuppressionReason { get; set; } = SuppressionReason.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CategoryCode { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? ImageUrl { get; set; }
    public string? PayloadJson { get; set; }
    public string? RouteKey { get; set; }
    public string? RouteParametersJson { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public string? AudienceDefinitionJson { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public required string CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public List<CampaignRecipient> Recipients { get; set; } = new();
}

public class CampaignRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public NotificationCampaign? Campaign { get; set; }
    public required string UserId { get; set; }
    public Guid? NotificationId { get; set; }
    public Notification? Notification { get; set; }
    public RecipientStatus RecipientStatus { get; set; } = RecipientStatus.Pending;
    public SuppressionReason SuppressionReason { get; set; } = SuppressionReason.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string CategoryCode { get; set; }
    public required string TitleTemplate { get; set; }
    public required string BodyTemplate { get; set; }
    public string? DefaultRouteKey { get; set; }
    public NotificationPriority DefaultPriority { get; set; } = NotificationPriority.Normal;
    public string? SupportedVariablesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string EventId { get; set; }
    public required string EventType { get; set; }
    public required string PayloadJson { get; set; }
    public string DeliveryStatus { get; set; } = "Pending";
    public int AttemptCount { get; set; } = 0;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? DetailsJson { get; set; }
    public string? CorrelationId { get; set; }
    public string? ClientIp { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SessionState
{
    public required string SessionId { get; set; }
    public required string UserId { get; set; }
    public string Status { get; set; } = "Allowed"; // "Allowed", "Blocked"
    public string? Reason { get; set; }
    public long Sequence { get; set; }
    public DateTime LastEventAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserBanState
{
    public required string UserId { get; set; }
    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? Reason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ProcessedEvent
{
    public required string EventId { get; set; }
    public required string EventType { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
