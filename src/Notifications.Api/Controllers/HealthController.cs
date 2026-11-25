using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Interfaces;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly NotificationsDbContext _db;
    private readonly IPushNotificationProvider _pushProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        NotificationsDbContext db,
        IPushNotificationProvider pushProvider,
        IConfiguration configuration,
        ILogger<HealthController> logger)
    {
        _db = db;
        _pushProvider = pushProvider;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("health")]
    [HttpGet("api/health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "NotificationsService",
            version = "1.0.0",
            pushProvider = _pushProvider.ProviderName,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("health/ready")]
    [HttpGet("api/health/ready")]
    public async Task<IActionResult> GetReadiness()
    {
        bool dbCanConnect = false;
        int pendingDeliveries = 0;
        int activeDevices = 0;

        try
        {
            dbCanConnect = await _db.Database.CanConnectAsync();
            if (dbCanConnect)
            {
                pendingDeliveries = await _db.NotificationDeliveries
                    .CountAsync(d => d.Status == DeliveryStatus.Pending);
                activeDevices = await _db.Devices
                    .CountAsync(d => d.IsActive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness check: database connection failed.");
            dbCanConnect = false;
        }

        var isFcmConfigured = !string.IsNullOrWhiteSpace(_configuration["Firebase:ProjectId"]) ||
                              !string.IsNullOrWhiteSpace(_configuration["Firebase:CredentialsPath"]) ||
                              !string.IsNullOrWhiteSpace(_configuration["Firebase:CredentialsJson"]);

        var isReady = dbCanConnect;

        var result = new
        {
            status = isReady ? "ready" : "degraded",
            service = "NotificationsService",
            checks = new
            {
                database = dbCanConnect ? "connected" : "unreachable",
                fcmProvider = _pushProvider.ProviderName,
                fcmConfigured = isFcmConfigured,
                pendingDeliveries,
                activeDevices
            },
            timestamp = DateTime.UtcNow
        };

        return isReady ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
