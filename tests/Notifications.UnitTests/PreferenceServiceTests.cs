using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.DTOs;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;
using Xunit;

namespace Notifications.UnitTests;

public class PreferenceServiceTests
{
    private async Task<NotificationsDbContext> CreateDbContextWithCategoriesAsync()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new NotificationsDbContext(options);
        await NotificationsDbContext.SeedInitialCategoriesAsync(db);
        return db;
    }

    [Fact]
    public async Task GetUserPreferences_NoCustomPrefs_ReturnsDefaultCategoryStates()
    {
        using var db = await CreateDbContextWithCategoriesAsync();
        var service = new PreferenceService(db, NullLogger<PreferenceService>.Instance);

        var prefs = await service.GetUserPreferencesAsync("user-001");

        Assert.NotNull(prefs);
        Assert.NotEmpty(prefs.Preferences);
        var securityPref = prefs.Preferences.FirstOrDefault(p => p.CategoryCode == "security");
        Assert.NotNull(securityPref);
        Assert.True(securityPref.Enabled);
    }

    [Fact]
    public async Task UpdateUserPreferences_UpdatesCategory_IgnoresSecurityCategoryDisabling()
    {
        using var db = await CreateDbContextWithCategoriesAsync();
        var service = new PreferenceService(db, NullLogger<PreferenceService>.Instance);

        var updateReq = new UpdatePreferencesRequest
        {
            Preferences = new List<CategoryPreferenceItem>
            {
                new() { CategoryCode = "clubs", Enabled = false },
                new() { CategoryCode = "security", Enabled = false } // Attempting to turn off security
            }
        };

        var updated = await service.UpdateUserPreferencesAsync("user-001", updateReq);

        var clubsPref = updated.Preferences.First(p => p.CategoryCode == "clubs");
        Assert.False(clubsPref.Enabled);

        // Security is a system category and must remain enabled regardless of user request
        var securityPref = updated.Preferences.First(p => p.CategoryCode == "security");
        Assert.True(securityPref.Enabled);
    }

    [Fact]
    public async Task IsPushEnabledForCategory_ReflectsPreferences()
    {
        using var db = await CreateDbContextWithCategoriesAsync();
        var service = new PreferenceService(db, NullLogger<PreferenceService>.Instance);

        // Default should be true
        Assert.True(await service.IsPushEnabledForCategoryAsync("user-001", "academic"));

        // Toggle off academic
        await service.UpdateUserPreferencesAsync("user-001", new UpdatePreferencesRequest
        {
            Preferences = new() { new() { CategoryCode = "academic", Enabled = false } }
        });

        Assert.False(await service.IsPushEnabledForCategoryAsync("user-001", "academic"));
    }
}
