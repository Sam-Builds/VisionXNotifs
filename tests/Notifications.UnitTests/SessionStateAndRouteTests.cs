using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.DTOs;
using Notifications.Application.Routing;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;
using Xunit;

namespace Notifications.UnitTests;

public class SessionStateServiceTests
{
    private NotificationsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new NotificationsDbContext(options);
    }

    [Fact]
    public void VerifyWebhookSignature_ValidSignature_ReturnsTrue()
    {
        using var db = CreateContext();
        var service = new SessionStateService(db, NullLogger<SessionStateService>.Instance);

        var secret = "test_shared_secret_123";
        var payload = "{\"EventId\":\"evt-1\",\"EventType\":\"session.revoked\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var signedData = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedData));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        var isValid = service.VerifyWebhookSignature(payload, timestamp, signature, secret);
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyWebhookSignature_InvalidSignature_ReturnsFalse()
    {
        using var db = CreateContext();
        var service = new SessionStateService(db, NullLogger<SessionStateService>.Instance);

        var secret = "test_shared_secret_123";
        var payload = "{\"EventId\":\"evt-1\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var isValid = service.VerifyWebhookSignature(payload, timestamp, "bad_signature_here", secret);
        Assert.False(isValid);
    }

    [Fact]
    public async Task ProcessSessionEvent_UserBanned_SetsUserBannedState()
    {
        using var db = CreateContext();
        var service = new SessionStateService(db, NullLogger<SessionStateService>.Instance);

        var eventDto = new SessionEventWebhookDto
        {
            EventId = "evt-ban-1",
            EventType = "user.banned",
            SessionId = "sid-123",
            UserId = "user-banned-01",
            Status = "Blocked",
            Reason = "Violation of campus conduct",
            Sequence = 1
        };

        await service.ProcessSessionEventAsync(eventDto);

        var isBanned = await service.IsUserBannedAsync("user-banned-01");
        Assert.True(isBanned);

        var isAllowed = await service.IsSessionAllowedAsync("sid-123", "user-banned-01");
        Assert.False(isAllowed);
    }
}

public class RouteRegistryTests
{
    [Fact]
    public void ValidateRoute_ValidAssignmentRouteWithId_ReturnsValid()
    {
        var result = RouteRegistry.ValidateRoute("assignment.details", new Dictionary<string, string>
        {
            ["assignmentId"] = "assign-xyz"
        });

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateRoute_MissingRequiredParameter_ReturnsInvalid()
    {
        var result = RouteRegistry.ValidateRoute("assignment.details", new Dictionary<string, string>());

        Assert.False(result.IsValid);
        Assert.Contains("requires parameter 'assignmentId'", result.ErrorMessage);
    }

    [Fact]
    public void ValidateRoute_UnknownRouteKey_ReturnsInvalid()
    {
        var result = RouteRegistry.ValidateRoute("unknown.route.nonexistent", null);

        Assert.False(result.IsValid);
        Assert.Contains("Unknown route key", result.ErrorMessage);
    }

    [Fact]
    public void ValidateRoute_NullOrEmptyRoute_ReturnsValid()
    {
        var result = RouteRegistry.ValidateRoute(null, null);
        Assert.True(result.IsValid);
    }
}
