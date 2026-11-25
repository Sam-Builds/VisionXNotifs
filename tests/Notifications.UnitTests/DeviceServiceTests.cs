using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.DTOs;
using Notifications.Domain.Entities;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;
using Xunit;

namespace Notifications.UnitTests;

public class DeviceServiceTests
{
    private NotificationsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new NotificationsDbContext(options);
    }

    [Fact]
    public async Task RegisterDevice_NewDevice_CreatesRecord()
    {
        using var db = CreateInMemoryDbContext();
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance);

        var request = new DeviceRegistrationRequest
        {
            InstallationId = "inst-001",
            FcmToken = "fcm-token-111",
            Platform = "android",
            DeviceName = "Pixel 8"
        };

        var result = await service.RegisterDeviceAsync("user-123", request);

        Assert.NotNull(result);
        Assert.Equal("inst-001", result.InstallationId);
        Assert.Equal("user-123", result.UserId);
        Assert.True(result.IsActive);

        var count = await db.Devices.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegisterDevice_ExistingDevice_UpdatesTokenAndSeen()
    {
        using var db = CreateInMemoryDbContext();
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance);

        var req1 = new DeviceRegistrationRequest
        {
            InstallationId = "inst-001",
            FcmToken = "old-token",
            Platform = "android"
        };
        await service.RegisterDeviceAsync("user-123", req1);

        var req2 = new DeviceRegistrationRequest
        {
            InstallationId = "inst-001",
            FcmToken = "new-token",
            Platform = "android",
            DeviceName = "Pixel 9"
        };
        var updated = await service.RegisterDeviceAsync("user-123", req2);

        Assert.Equal(1, await db.Devices.CountAsync());
        var device = await db.Devices.FirstAsync();
        Assert.Equal("new-token", device.FcmToken);
        Assert.Equal("Pixel 9", device.DeviceName);
    }

    [Fact]
    public async Task RegisterDevice_DuplicateTokenDifferentInstall_DeactivatesOldDevice()
    {
        using var db = CreateInMemoryDbContext();
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance);

        await service.RegisterDeviceAsync("user-A", new DeviceRegistrationRequest
        {
            InstallationId = "inst-A",
            FcmToken = "shared-token"
        });

        await service.RegisterDeviceAsync("user-B", new DeviceRegistrationRequest
        {
            InstallationId = "inst-B",
            FcmToken = "shared-token"
        });

        var deviceA = await db.Devices.FirstAsync(d => d.InstallationId == "inst-A");
        var deviceB = await db.Devices.FirstAsync(d => d.InstallationId == "inst-B");

        Assert.False(deviceA.IsActive);
        Assert.True(deviceB.IsActive);
    }

    [Fact]
    public async Task RemoveDevice_DeactivatesRegistration()
    {
        using var db = CreateInMemoryDbContext();
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance);

        await service.RegisterDeviceAsync("user-123", new DeviceRegistrationRequest
        {
            InstallationId = "inst-001",
            FcmToken = "tok-1"
        });

        var removed = await service.RemoveDeviceAsync("user-123", "inst-001");
        Assert.True(removed);

        var device = await db.Devices.FirstAsync();
        Assert.False(device.IsActive);
    }

    [Fact]
    public async Task RegisterDevice_DummyToken_RegistersAsInactive()
    {
        using var db = CreateInMemoryDbContext();
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance);

        var result = await service.RegisterDeviceAsync("user-dummy", new DeviceRegistrationRequest
        {
            InstallationId = "inst-dummy-1",
            FcmToken = "device_123456789abcdef_uid"
        });

        Assert.NotNull(result);
        Assert.False(result.IsActive);

        var device = await db.Devices.FirstAsync(d => d.InstallationId == "inst-dummy-1");
        Assert.False(device.IsActive);
    }
}
