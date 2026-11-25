using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class DeviceService : IDeviceService
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(NotificationsDbContext db, ILogger<DeviceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DeviceDto> RegisterDeviceAsync(string userId, DeviceRegistrationRequest request)
    {
        var platform = request.Platform.ToLowerInvariant() switch
        {
            "ios" => DevicePlatform.iOS,
            "web" => DevicePlatform.Web,
            _ => DevicePlatform.Android
        };

        // Dummy/placeholder tokens (e.g. legacy device_ prefix) should not be activated for FCM delivery
        var isTokenValidForDelivery = !string.IsNullOrWhiteSpace(request.FcmToken) &&
                                      !request.FcmToken.StartsWith("device_", StringComparison.OrdinalIgnoreCase);

        // If another device record with the same FCM token exists with a different InstallationId, deactivate it
        var duplicateTokens = await _db.Devices
            .Where(d => d.FcmToken == request.FcmToken && d.InstallationId != request.InstallationId && d.IsActive)
            .ToListAsync();

        foreach (var dup in duplicateTokens)
        {
            dup.IsActive = false;
            dup.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Deactivating stale device {DeviceId} with duplicate FCM token", dup.Id);
        }

        var existingDevice = await _db.Devices
            .FirstOrDefaultAsync(d => d.InstallationId == request.InstallationId);

        if (existingDevice != null)
        {
            existingDevice.UserId = userId;
            existingDevice.FcmToken = request.FcmToken;
            existingDevice.Platform = platform;
            existingDevice.DeviceName = request.DeviceName ?? existingDevice.DeviceName;
            existingDevice.AppVersion = request.AppVersion ?? existingDevice.AppVersion;
            existingDevice.OSVersion = request.OSVersion ?? existingDevice.OSVersion;
            existingDevice.IsActive = isTokenValidForDelivery;
            existingDevice.LastSeenAt = DateTime.UtcNow;
            existingDevice.TokenUpdatedAt = DateTime.UtcNow;
            existingDevice.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Updated device {DeviceId} for user {UserId} (IsActive={IsActive})",
                existingDevice.Id, userId, existingDevice.IsActive);
            return ToDto(existingDevice);
        }

        var newDevice = new Device
        {
            InstallationId = request.InstallationId,
            UserId = userId,
            FcmToken = request.FcmToken,
            Platform = platform,
            DeviceName = request.DeviceName,
            AppVersion = request.AppVersion,
            OSVersion = request.OSVersion,
            IsActive = isTokenValidForDelivery,
            LastSeenAt = DateTime.UtcNow,
            TokenUpdatedAt = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Devices.Add(newDevice);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Registered new device {DeviceId} for user {UserId}", newDevice.Id, userId);
        return ToDto(newDevice);
    }

    public async Task<List<DeviceDto>> GetUserDevicesAsync(string userId)
    {
        var devices = await _db.Devices
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.IsActive)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync();

        return devices.Select(ToDto).ToList();
    }

    public async Task<bool> RemoveDeviceAsync(string userId, string installationId)
    {
        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.InstallationId == installationId && d.UserId == userId);

        if (device == null)
        {
            return false;
        }

        device.IsActive = false;
        device.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deactivated device {DeviceId} for user {UserId}", device.Id, userId);
        return true;
    }

    public async Task<List<DeviceDto>> GetAdminDevicesAsync(int limit, int offset, string? search)
    {
        var query = _db.Devices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(d =>
                d.UserId.ToLower().Contains(s) ||
                (d.DeviceName != null && d.DeviceName.ToLower().Contains(s)) ||
                d.InstallationId.ToLower().Contains(s));
        }

        var devices = await query
            .OrderByDescending(d => d.LastSeenAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return devices.Select(ToDto).ToList();
    }

    public async Task<bool> DeactivateDeviceAsync(Guid deviceId, string actorEmail)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null)
        {
            return false;
        }

        device.IsActive = false;
        device.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} deactivated by admin {ActorEmail}", deviceId, actorEmail);
        return true;
    }

    private static DeviceDto ToDto(Device device)
    {
        return new DeviceDto
        {
            Id = device.Id,
            InstallationId = device.InstallationId,
            UserId = device.UserId,
            Platform = device.Platform.ToString(),
            DeviceName = device.DeviceName,
            AppVersion = device.AppVersion,
            OSVersion = device.OSVersion,
            IsActive = device.IsActive,
            LastSeenAt = device.LastSeenAt,
            TokenUpdatedAt = device.TokenUpdatedAt,
            RegisteredAt = device.RegisteredAt,
            LastSuccessfulDeliveryAt = device.LastSuccessfulDeliveryAt
        };
    }
}
