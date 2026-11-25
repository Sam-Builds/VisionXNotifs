namespace Notifications.Domain.Enums;

public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum DeliveryStatus
{
    Pending = 0,
    Processing = 1,
    AcceptedByProvider = 2,
    Delivered = 2,
    RetryScheduled = 3,
    PermanentlyFailed = 4,
    Failed = 4,
    Cancelled = 5,
    Suppressed = 6
}

public enum CampaignStatus
{
    Draft = 0,
    ReadyForReview = 1,
    Approved = 2,
    Scheduled = 3,
    Queued = 4,
    Processing = 5,
    Completed = 6,
    CompletedWithFailures = 7,
    Failed = 8,
    Cancelled = 9
}

public enum RecipientStatus
{
    Pending = 0,
    Dispatched = 1,
    Delivered = 1,
    Suppressed = 2,
    Failed = 3
}

public enum DevicePlatform
{
    Android = 0,
    iOS = 1,
    Web = 2,
    Other = 3
}

public enum SuppressionReason
{
    None = 0,
    UserBanned = 1,
    SessionRevoked = 2,
    CategoryDisabled = 3,
    PreferenceDisabled = 3,
    DeviceInactive = 4,
    TokenInvalid = 5,
    NotificationExpired = 6,
    RecipientNotEligible = 7,
    Duplicate = 8
}
