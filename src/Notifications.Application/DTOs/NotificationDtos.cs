using Notifications.Domain.Enums;

namespace Notifications.Application.DTOs;

// Device DTOs
public class DeviceRegistrationRequest
{
    public required string InstallationId { get; set; }
    public required string FcmToken { get; set; }
    public string Platform { get; set; } = "android";
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? OSVersion { get; set; }
}

public class DeviceDto
{
    public Guid Id { get; set; }
    public string InstallationId { get; set; } = "";
    public string FcmToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? OSVersion { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime TokenUpdatedAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastSuccessfulDeliveryAt { get; set; }
}

// Notification Category DTOs
public class NotificationCategoryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? IconIdentifier { get; set; }
    public bool IsActive { get; set; }
    public bool IsUserConfigurable { get; set; }
    public bool IsSystemCategory { get; set; }
    public bool DefaultEnabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class CreateNotificationCategoryRequest
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? IconIdentifier { get; set; }
    public bool IsUserConfigurable { get; set; } = true;
    public bool DefaultEnabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}

// Notification Preference DTOs
public class CategoryPreferenceItem
{
    public required string CategoryCode { get; set; }
    public bool Enabled { get; set; }
}

public class UserPreferencesResponse
{
    public List<CategoryPreferenceItem> Preferences { get; set; } = new();
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdatePreferencesRequest
{
    public required List<CategoryPreferenceItem> Preferences { get; set; }
    public long? ExpectedVersion { get; set; }
    public string? UpdatedFrom { get; set; }
}

// Notification Inbox DTOs
public class NotificationItemDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid? CampaignId { get; set; }
    public string? EventId { get; set; }
    public string CategoryCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? PayloadJson { get; set; }
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
}

public class PaginatedNotificationsResponse
{
    public List<NotificationItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
}

public class UnreadCountResponse
{
    public int UnreadCount { get; set; }
}

// Campaign DTOs
public class CreateCampaignRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CategoryCode { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? ImageUrl { get; set; }
    public string? PayloadJson { get; set; }
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public AudienceFilterDto? Audience { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool SendImmediately { get; set; } = false;
}

public class ScheduleCampaignRequest
{
    public DateTime ScheduledAt { get; set; }
}

public class CampaignDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string CategoryCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? PayloadJson { get; set; }
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public NotificationPriority Priority { get; set; }
    public AudienceFilterDto? Audience { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public CampaignStatus Status { get; set; }
    public string CreatedBy { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int TotalRecipients { get; set; }
    public int DeliveredCount { get; set; }
    public int FailedCount { get; set; }
    public int SuppressedCount { get; set; }
}

public class AudienceFilterDto
{
    public string TargetType { get; set; } = "All"; // "All", "Students", "Faculty", "Department", "Semester", "CourseOffering", "Section", "Club", "SpecificUsers"
    public Guid? DepartmentId { get; set; }
    public int? Semester { get; set; }
    public Guid? CourseOfferingId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? ClubId { get; set; }
    public List<string>? SpecificUserIds { get; set; }
    public List<string>? ExcludedUserIds { get; set; }
}

public class AudiencePreviewResponse
{
    public int EstimatedRecipients { get; set; }
    public int EstimatedPushEligible { get; set; }
    public int EstimatedPreferenceSuppressed { get; set; }
    public List<string> SampleUserIds { get; set; } = new();
    public string AudienceSummary { get; set; } = "";
}

public class TestNotificationRequest
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string CategoryCode { get; set; } = "general";
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public string? TargetInstallationId { get; set; }
    public Dictionary<string, string>? ExtraData { get; set; }
}

public class TestNotificationResult
{
    public bool Success { get; set; }
    public Guid NotificationId { get; set; }
    public int DevicesTargeted { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string? ProviderMessageId { get; set; }
}

// Delivery DTOs
public class DeliveryRecordDto
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public string UserId { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid DeviceId { get; set; }
    public string Platform { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Provider { get; set; } = "";
    public DeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public SuppressionReason SuppressionReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Template DTOs
public class NotificationTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string TitleTemplate { get; set; } = "";
    public string BodyTemplate { get; set; } = "";
    public string? DefaultRouteKey { get; set; }
    public NotificationPriority DefaultPriority { get; set; }
    public List<string> SupportedVariables { get; set; } = new();
    public bool IsActive { get; set; }
    public int Version { get; set; }
}

public class CreateTemplateRequest
{
    public required string Name { get; set; }
    public required string CategoryCode { get; set; }
    public required string TitleTemplate { get; set; }
    public required string BodyTemplate { get; set; }
    public string? DefaultRouteKey { get; set; }
    public NotificationPriority DefaultPriority { get; set; } = NotificationPriority.Normal;
    public List<string>? SupportedVariables { get; set; }
}

// Overview KPI DTOs
public class AdminNotificationOverviewDto
{
    public int TotalNotificationsCreated { get; set; }
    public int TotalCampaigns { get; set; }
    public int ActiveScheduledCampaigns { get; set; }
    public int PendingDeliveryJobs { get; set; }
    public int TotalAcceptedByProvider { get; set; }
    public int TotalFailedDeliveries { get; set; }
    public int TotalSuppressedDeliveries { get; set; }
    public int ActiveRegisteredDevices { get; set; }
    public int TotalRegisteredDevices { get; set; }
    public int TotalUniqueUsersReached { get; set; }
    public List<DeliveryRecordDto> RecentDeliveries { get; set; } = new();
}

// Internal Server-to-Server Event DTOs
public class InboundAcademicEventDto
{
    public required string EventId { get; set; }
    public required string EventType { get; set; } // e.g. "assignment.published", "attendance.session_published", "internal_marks.published", "results.published", "timetable.published"
    public required string Source { get; set; }
    public string? CorrelationId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string CategoryCode { get; set; } = "academic";
    public string? RouteKey { get; set; }
    public Dictionary<string, string>? RouteParameters { get; set; }
    public AudienceFilterDto? TargetAudience { get; set; }
    public List<string>? DirectRecipientUserIds { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class SessionEventWebhookDto
{
    public required string EventId { get; set; }
    public required string EventType { get; set; }
    public required string SessionId { get; set; }
    public required string UserId { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public long Sequence { get; set; }
    public string? OccurredAt { get; set; }
}
