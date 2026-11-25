using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;

namespace Notifications.Infrastructure.Persistence;

public class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<NotificationCategory> NotificationCategories => Set<NotificationCategory>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationCampaign> NotificationCampaigns => Set<NotificationCampaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationOutboxMessage> NotificationOutbox => Set<NotificationOutboxMessage>();
    public DbSet<NotificationAuditLog> NotificationAuditLogs => Set<NotificationAuditLog>();
    public DbSet<SessionState> SessionStates => Set<SessionState>();
    public DbSet<UserBanState> UserBanStates => Set<UserBanState>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Device configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InstallationId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.FcmToken);
            entity.HasIndex(e => new { e.UserId, e.IsActive });
            entity.Property(e => e.InstallationId).HasMaxLength(128);
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.FcmToken).HasMaxLength(1024);
            entity.Property(e => e.Platform).HasConversion<string>();
        });

        // NotificationCategory configuration
        modelBuilder.Entity<NotificationCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(64);
            entity.Property(e => e.Name).HasMaxLength(128);
        });

        // NotificationPreference configuration
        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CategoryCode }).IsUnique();
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.CategoryCode).HasMaxLength(64);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt });
            entity.HasIndex(e => e.CampaignId);
            entity.HasIndex(e => e.EventId);
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.CategoryCode).HasMaxLength(64);
            entity.Property(e => e.Title).HasMaxLength(256);
            entity.Property(e => e.Priority).HasConversion<string>();

            entity.HasOne(e => e.Campaign)
                .WithMany()
                .HasForeignKey(e => e.CampaignId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // NotificationDelivery configuration
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.HasIndex(e => e.NotificationId);
            entity.HasIndex(e => e.DeviceId);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.SuppressionReason).HasConversion<string>();

            entity.HasOne(e => e.Notification)
                .WithMany(n => n.Deliveries)
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NotificationCampaign configuration
        modelBuilder.Entity<NotificationCampaign>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ScheduledAt);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.CategoryCode).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Priority).HasConversion<string>();
        });

        // CampaignRecipient configuration
        modelBuilder.Entity<CampaignRecipient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CampaignId, e.UserId }).IsUnique();
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.RecipientStatus).HasConversion<string>();
            entity.Property(e => e.SuppressionReason).HasConversion<string>();

            entity.HasOne(e => e.Campaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(e => e.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NotificationTemplate configuration
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(128);
            entity.Property(e => e.CategoryCode).HasMaxLength(64);
            entity.Property(e => e.DefaultPriority).HasConversion<string>();
        });

        // NotificationOutboxMessage configuration
        modelBuilder.Entity<NotificationOutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DeliveryStatus, e.NextAttemptAt });
            entity.HasIndex(e => e.EventId);
        });

        // NotificationAuditLog configuration
        modelBuilder.Entity<NotificationAuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Action);
        });

        // SessionState configuration
        modelBuilder.Entity<SessionState>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.UserId);
        });

        // UserBanState configuration
        modelBuilder.Entity<UserBanState>(entity =>
        {
            entity.HasKey(e => e.UserId);
        });

        // ProcessedEvent configuration
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
        });
    }

    public static async Task SeedInitialCategoriesAsync(NotificationsDbContext db)
    {
        var defaultCategories = new List<NotificationCategory>
        {
            new()
            {
                Code = "academic",
                Name = "Academic Notices",
                Description = "Exam notifications, curriculum notices, and departmental circulars",
                IconIdentifier = "school_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 1
            },
            new()
            {
                Code = "assignments",
                Name = "Assignments",
                Description = "Coursework assignments, project deadlines, and submission reminders",
                IconIdentifier = "assignment_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 2
            },
            new()
            {
                Code = "attendance",
                Name = "Attendance Notices",
                Description = "Daily attendance warnings, shortage alerts, and summary updates",
                IconIdentifier = "fact_check_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 3
            },
            new()
            {
                Code = "internals",
                Name = "Internal Assessments",
                Description = "Internal test schedules, marks published, and assessment feedback",
                IconIdentifier = "grading_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 4
            },
            new()
            {
                Code = "results",
                Name = "Results & Grades",
                Description = "Official semester results, GPA calculations, and grade cards",
                IconIdentifier = "assessment_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 5
            },
            new()
            {
                Code = "timetable",
                Name = "Class Timetables",
                Description = "Class schedules, room changes, cancellations, and makeup sessions",
                IconIdentifier = "calendar_today_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 6
            },
            new()
            {
                Code = "announcements",
                Name = "Campus Announcements",
                Description = "Institutional circulars, administrative alerts, and campus news",
                IconIdentifier = "campaign_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 7
            },
            new()
            {
                Code = "clubs",
                Name = "Clubs & Societies",
                Description = "Club recruitment drives, campus events, and competitions",
                IconIdentifier = "groups_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 8
            },
            new()
            {
                Code = "campaigns",
                Name = "Campaigns & Events",
                Description = "Institutional initiatives, placement drives, and cultural fests",
                IconIdentifier = "star_outline",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 9
            },
            new()
            {
                Code = "general",
                Name = "General Updates",
                Description = "General administrative notices and informational updates",
                IconIdentifier = "notifications_outlined",
                IsActive = true,
                IsUserConfigurable = true,
                DefaultEnabled = true,
                DisplayOrder = 10
            },
            new()
            {
                Code = "security",
                Name = "Security & Account",
                Description = "Critical login alerts, session revocations, and account status",
                IconIdentifier = "security_outlined",
                IsActive = true,
                IsUserConfigurable = false, // Critical security category cannot be toggled off by ordinary user preferences
                IsSystemCategory = true,
                DefaultEnabled = true,
                DisplayOrder = 11
            }
        };

        foreach (var cat in defaultCategories)
        {
            var exists = await db.NotificationCategories.AnyAsync(c => c.Code == cat.Code);
            if (!exists)
            {
                db.NotificationCategories.Add(cat);
            }
        }

        await db.SaveChangesAsync();
    }
}
