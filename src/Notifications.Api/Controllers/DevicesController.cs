using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Security;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IDeviceService deviceService,
        ISessionStateService sessionStateService,
        ILogger<DevicesController> logger)
    {
        _deviceService = deviceService;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    /// <summary>
    /// Registers or refreshes a device FCM token for the authenticated user.
    /// </summary>
    [HttpPost]
    [HttpPost("register")]
    [ProducesResponseType(typeof(DeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var sessionId = User.GetSessionId();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var isAllowed = await _sessionStateService.IsSessionAllowedAsync(sessionId, userId);
            if (!isAllowed)
            {
                return Unauthorized(new { code = "SESSION_REVOKED", message = "Session has been revoked or account is banned." });
            }
        }

        if (string.IsNullOrWhiteSpace(request.InstallationId) || string.IsNullOrWhiteSpace(request.FcmToken))
        {
            return BadRequest(new { code = "INVALID_REQUEST", message = "InstallationId and FcmToken are required." });
        }

        var result = await _deviceService.RegisterDeviceAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves active registered devices for the authenticated user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyDevices()
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var devices = await _deviceService.GetUserDevicesAsync(userId);
        return Ok(devices);
    }

    /// <summary>
    /// Deactivates a device registration by installation ID (e.g. on logout or app uninstall).
    /// </summary>
    [HttpDelete("{installationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateDevice(string installationId)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var removed = await _deviceService.RemoveDeviceAsync(userId, installationId);
        if (!removed)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Device not found or not owned by user." });
        }

        return Ok(new { success = true, message = "Device registration deactivated." });
    }
}
