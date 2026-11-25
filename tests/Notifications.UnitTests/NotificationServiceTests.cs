using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.DTOs;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;
using Xunit;

namespace Notifications.UnitTests;

public class NotificationServiceTests
{
    private async Task<NotificationsDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new NotificationsDbContext(options);
        await NotificationsDbContext.SeedInitialCategoriesAsync(db);
        return db;
    }

    [Fact]
    public async Task CreateNotification_WithActiveDeviceAndEnabledCategory_EnqueuesDelivery()
    {
        using var db = await CreateContextAsync();
        var prefService = new PreferenceService(db, NullLogger<PreferenceService>.Instance);
        var sessionService = new SessionStateService(db, NullLogger<SessionStateService>.Instance);
        var notifService = new NotificationService(db, prefService, sessionService, NullLogger<NotificationService>.Instance);

        // Register device
        db.Devices.Add(new Device
        {
            InstallationId = "inst-01",
            UserId = "user-1",
            FcmToken = "fcm-tok-1",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var notif = await notifService.CreateNotificationRecordAsync(
            userId: "user-1",
            title: "Assignment Due",
            body: "Your math assignment is due tomorrow.",
            categoryCode: "assignments",
            routeKey: "assignment.details",
            routeParams: new Dictionary<string, string> { ["assignmentId"] = "assign-1" });

        Assert.NotNull(notif);
        var deliveries = await db.NotificationDeliveries.Where(d => d.NotificationId == notif.Id).ToListAsync();
        Assert.Single(deliveries);
        Assert.Equal(DeliveryStatus.Pending, deliveries[0].Status);
    }

    [Fact]
    public async Task CreateNotification_WhenPreferenceDisabled_SuppressesPushDelivery()
    {
        using var db = await CreateContextAsync();
        var prefService = new PreferenceService(db, NullLogger<PreferenceService>.Instance);
        var sessionService = new SessionStateService(db, NullLogger<SessionStateService>.Instance);
        var notifService = new NotificationService(db, prefService, sessionService, NullLogger<NotificationService>.Instance);

        // Turn off clubs preference
        await prefService.UpdateUserPreferencesAsync("user-1", new UpdatePreferencesRequest
        {
            Preferences = new() { new() { CategoryCode = "clubs", Enabled = false } }
        });

        // Register device
        db.Devices.Add(new Device
        {
            InstallationId = "inst-01",
            UserId = "user-1",
            FcmToken = "fcm-tok-1",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var notif = await notifService.CreateNotificationRecordAsync(
            userId: "user-1",
            title: "Club Fest",
            body: "Come to the club fest!",
            categoryCode: "clubs");

        // Inbox item is still created!
        Assert.NotNull(notif);

        // But delivery is suppressed
        var deliveries = await db.NotificationDeliveries.Where(d => d.NotificationId == notif.Id).ToListAsync();
        Assert.Single(deliveries);
        Assert.Equal(DeliveryStatus.Suppressed, deliveries[0].Status);
        Assert.Equal(SuppressionReason.PreferenceDisabled, deliveries[0].SuppressionReason);
    }

    [Fact]
    public async Task MarkAsRead_UpdatesIsRead()
    {
        using var db = await CreateContextAsync();
        var prefService = new PreferenceService(db, NullLogger<PreferenceService>.Instance);
        var sessionService = new SessionStateService(db, NullLogger<SessionStateService>.Instance);
        var notifService = new NotificationService(db, prefService, sessionService, NullLogger<NotificationService>.Instance);

        var notif = await notifService.CreateNotificationRecordAsync(
            userId: "user-1",
            title: "Welcome",
            body: "Welcome to campus",
            categoryCode: "general");

        var countBefore = await notifService.GetUnreadCountAsync("user-1");
        Assert.Equal(1, countBefore);

        var marked = await notifService.MarkAsReadAsync("user-1", notif.Id);
        Assert.True(marked);

        var countAfter = await notifService.GetUnreadCountAsync("user-1");
        Assert.Equal(0, countAfter);
    }
}
