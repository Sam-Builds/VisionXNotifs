using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class PreferenceService : IPreferenceService
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<PreferenceService> _logger;

    public PreferenceService(NotificationsDbContext db, ILogger<PreferenceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<NotificationCategoryDto>> GetActiveCategoriesAsync()
    {
        var categories = await _db.NotificationCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        return categories.Select(c => new NotificationCategoryDto
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name,
            Description = c.Description,
            IconIdentifier = c.IconIdentifier,
            IsActive = c.IsActive,
            IsUserConfigurable = c.IsUserConfigurable,
            IsSystemCategory = c.IsSystemCategory,
            DefaultEnabled = c.DefaultEnabled,
            DisplayOrder = c.DisplayOrder
        }).ToList();
    }

    public async Task<UserPreferencesResponse> GetUserPreferencesAsync(string userId)
    {
        var activeCategories = await _db.NotificationCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        var userPrefs = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.CategoryCode, p => p, StringComparer.OrdinalIgnoreCase);

        var items = new List<CategoryPreferenceItem>();
        long maxVersion = 1;
        DateTime latestUpdated = DateTime.UtcNow;

        foreach (var cat in activeCategories)
        {
            if (userPrefs.TryGetValue(cat.Code, out var pref))
            {
                // Non-configurable system categories are always enabled
                var isEnabled = !cat.IsUserConfigurable || pref.IsEnabled;
                items.Add(new CategoryPreferenceItem
                {
                    CategoryCode = cat.Code,
                    Enabled = isEnabled
                });

                if (pref.Version > maxVersion)
                {
                    maxVersion = pref.Version;
                }
                if (pref.UpdatedAt > latestUpdated)
                {
                    latestUpdated = pref.UpdatedAt;
                }
            }
            else
            {
                items.Add(new CategoryPreferenceItem
                {
                    CategoryCode = cat.Code,
                    Enabled = cat.DefaultEnabled
                });
            }
        }

        return new UserPreferencesResponse
        {
            Preferences = items,
            Version = maxVersion,
            UpdatedAt = latestUpdated
        };
    }

    public async Task<UserPreferencesResponse> UpdateUserPreferencesAsync(string userId, UpdatePreferencesRequest request)
    {
        var activeCategories = await _db.NotificationCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Code, c => c, StringComparer.OrdinalIgnoreCase);

        var existingPrefs = await _db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var existingDict = existingPrefs.ToDictionary(p => p.CategoryCode, p => p, StringComparer.OrdinalIgnoreCase);

        long currentVersion = existingPrefs.Any() ? existingPrefs.Max(p => p.Version) : 0;
        long nextVersion = currentVersion + 1;
        var now = DateTime.UtcNow;

        foreach (var item in request.Preferences)
        {
            if (!activeCategories.TryGetValue(item.CategoryCode, out var category))
            {
                continue;
            }

            // If system/non-configurable category, force it enabled
            var effectiveEnabled = category.IsUserConfigurable ? item.Enabled : true;

            if (existingDict.TryGetValue(item.CategoryCode, out var existing))
            {
                existing.IsEnabled = effectiveEnabled;
                existing.Version = nextVersion;
                existing.UpdatedAt = now;
                existing.UpdatedFrom = request.UpdatedFrom ?? "App";
            }
            else
            {
                var newPref = new NotificationPreference
                {
                    UserId = userId,
                    CategoryCode = category.Code,
                    IsEnabled = effectiveEnabled,
                    Version = nextVersion,
                    UpdatedAt = now,
                    UpdatedFrom = request.UpdatedFrom ?? "App"
                };
                _db.NotificationPreferences.Add(newPref);
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated preferences for user {UserId} to version {Version}", userId, nextVersion);
        return await GetUserPreferencesAsync(userId);
    }

    public async Task<NotificationCategoryDto> CreateCategoryAsync(CreateNotificationCategoryRequest request)
    {
        var existing = await _db.NotificationCategories.FirstOrDefaultAsync(c => c.Code == request.Code);
        if (existing != null)
        {
            throw new InvalidOperationException($"Category with code '{request.Code}' already exists.");
        }

        var cat = new NotificationCategory
        {
            Code = request.Code.ToLowerInvariant(),
            Name = request.Name,
            Description = request.Description,
            IconIdentifier = request.IconIdentifier,
            IsActive = true,
            IsUserConfigurable = request.IsUserConfigurable,
            DefaultEnabled = request.DefaultEnabled,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.NotificationCategories.Add(cat);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created new notification category: {Code}", cat.Code);

        return new NotificationCategoryDto
        {
            Id = cat.Id,
            Code = cat.Code,
            Name = cat.Name,
            Description = cat.Description,
            IconIdentifier = cat.IconIdentifier,
            IsActive = cat.IsActive,
            IsUserConfigurable = cat.IsUserConfigurable,
            IsSystemCategory = cat.IsSystemCategory,
            DefaultEnabled = cat.DefaultEnabled,
            DisplayOrder = cat.DisplayOrder
        };
    }

    public async Task<NotificationCategoryDto?> UpdateCategoryAsync(Guid id, CreateNotificationCategoryRequest request)
    {
        var cat = await _db.NotificationCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return null;

        cat.Name = request.Name;
        cat.Description = request.Description;
        cat.IconIdentifier = request.IconIdentifier;
        cat.IsUserConfigurable = request.IsUserConfigurable;
        cat.DefaultEnabled = request.DefaultEnabled;
        cat.DisplayOrder = request.DisplayOrder;
        cat.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated notification category '{Code}' ({Name})", cat.Code, cat.Name);

        return new NotificationCategoryDto
        {
            Id = cat.Id,
            Code = cat.Code,
            Name = cat.Name,
            Description = cat.Description,
            IconIdentifier = cat.IconIdentifier,
            IsActive = cat.IsActive,
            IsUserConfigurable = cat.IsUserConfigurable,
            IsSystemCategory = cat.IsSystemCategory,
            DefaultEnabled = cat.DefaultEnabled,
            DisplayOrder = cat.DisplayOrder
        };
    }

    public async Task<bool> IsPushEnabledForCategoryAsync(string userId, string categoryCode)
    {
        var category = await _db.NotificationCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == categoryCode);

        if (category == null)
        {
            // Default to enabled if unknown
            return true;
        }

        if (!category.IsActive)
        {
            return false;
        }

        if (!category.IsUserConfigurable)
        {
            // Critical categories are never suppressible by user preferences
            return true;
        }

        var userPref = await _db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CategoryCode == categoryCode);

        if (userPref != null)
        {
            return userPref.IsEnabled;
        }

        return category.DefaultEnabled;
    }
}
