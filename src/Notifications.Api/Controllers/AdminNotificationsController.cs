using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Security;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/admin/notifications")]
[Authorize]
public class AdminNotificationsController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly IDeviceService _deviceService;
    private readonly IPreferenceService _preferenceService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AdminNotificationsController> _logger;

    public AdminNotificationsController(
        ICampaignService campaignService,
        IDeviceService deviceService,
        IPreferenceService preferenceService,
        IAuditLogService auditLogService,
        ILogger<AdminNotificationsController> logger)
    {
        _campaignService = campaignService;
        _deviceService = deviceService;
        _preferenceService = preferenceService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private bool CheckAdminAccess() => User.IsAdmin() || User.HasPermission("notifications.read");
    private bool CheckAdminWriteAccess() => User.IsAdmin() || User.HasPermission("notifications.send");

    /// <summary>
    /// Returns aggregated overview metrics, KPIs, and recent delivery activity for the admin dashboard.
    /// </summary>
    [HttpGet("overview")]
    [HttpGet("/api/admin/overview")]
    [ProducesResponseType(typeof(AdminNotificationOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview()
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var metrics = await _campaignService.GetOverviewMetricsAsync();
        return Ok(metrics);
    }

    /// <summary>
    /// Dispatches an immediate test push notification directly to specified installation or admin's devices.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(TestNotificationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendTestNotification([FromBody] TestNotificationRequest request)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        var actorId = User.GetAuthUserId() ?? "admin";
        var actorEmail = User.GetEmail() ?? "admin@visionx.io";

        var result = await _campaignService.SendTestNotificationAsync(actorId, actorEmail, request);
        return Ok(result);
    }

    /// <summary>
    /// Lists all delivery attempts with optional status and failure filtering.
    /// </summary>
    [HttpGet("deliveries")]
    [HttpGet("/api/admin/deliveries")]
    [ProducesResponseType(typeof(List<DeliveryRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllDeliveries(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null,
        [FromQuery] string? failureReason = null)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var clampedLimit = Math.Clamp(limit, 1, 200);
        var clampedOffset = Math.Max(0, offset);

        var deliveries = await _campaignService.GetAllDeliveriesAsync(clampedLimit, clampedOffset, status, failureReason);
        return Ok(deliveries);
    }

    /// <summary>
    /// Lists registered devices across all users for administrator inspection.
    /// </summary>
    [HttpGet("devices")]
    [HttpGet("/api/admin/devices")]
    [ProducesResponseType(typeof(List<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDevices(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var clampedLimit = Math.Clamp(limit, 1, 200);
        var clampedOffset = Math.Max(0, offset);

        var devices = await _deviceService.GetAdminDevicesAsync(clampedLimit, clampedOffset, search);
        return Ok(devices);
    }

    /// <summary>
    /// Deactivates a device registration by device ID.
    /// </summary>
    [HttpDelete("devices/{deviceId:guid}")]
    [HttpPost("devices/{deviceId:guid}/deactivate")]
    [HttpPost("/api/admin/devices/{deviceId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateDevice(Guid deviceId)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var success = await _deviceService.DeactivateDeviceAsync(deviceId, actorEmail);
        if (!success)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Device not found." });
        }

        return Ok(new { success = true, deviceId });
    }

    /// <summary>
    /// Lists all active notification categories for admin management.
    /// </summary>
    [HttpGet("categories")]
    [HttpGet("/api/admin/categories")]
    [ProducesResponseType(typeof(List<NotificationCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories()
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var categories = await _preferenceService.GetActiveCategoriesAsync();
        return Ok(categories);
    }

    /// <summary>
    /// Creates a new notification category.
    /// </summary>
    [HttpPost("categories")]
    [HttpPost("/api/admin/categories")]
    [ProducesResponseType(typeof(NotificationCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateNotificationCategoryRequest request)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        try
        {
            var category = await _preferenceService.CreateCategoryAsync(request);
            return StatusCode(StatusCodes.Status201Created, category);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = "DUPLICATE_CATEGORY", message = ex.Message });
        }
    }

    /// <summary>
    /// Updates an existing notification category.
    /// </summary>
    [HttpPut("categories/{id:guid}")]
    [HttpPut("/api/admin/categories/{id:guid}")]
    [ProducesResponseType(typeof(NotificationCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] CreateNotificationCategoryRequest request)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        var category = await _preferenceService.UpdateCategoryAsync(id, request);
        if (category == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Category not found." });
        }

        return Ok(category);
    }

    /// <summary>
    /// Lists notification templates for reusable campaign composition.
    /// </summary>
    [HttpGet("templates")]
    [HttpGet("/api/admin/templates")]
    [ProducesResponseType(typeof(List<NotificationTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates()
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var templates = await _campaignService.GetTemplatesAsync();
        return Ok(templates);
    }

    /// <summary>
    /// Creates a new notification template.
    /// </summary>
    [HttpPost("templates")]
    [HttpPost("/api/admin/templates")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var template = await _campaignService.CreateTemplateAsync(request, actorEmail);
        return StatusCode(StatusCodes.Status201Created, template);
    }

    /// <summary>
    /// Updates an existing notification template.
    /// </summary>
    [HttpPut("templates/{id:guid}")]
    [HttpPut("/api/admin/templates/{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] CreateTemplateRequest request)
    {
        if (!CheckAdminWriteAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var template = await _campaignService.UpdateTemplateAsync(id, request, actorEmail);
        if (template == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Template not found." });
        }

        return Ok(template);
    }

    /// <summary>
    /// Retrieves notification audit logs.
    /// </summary>
    [HttpGet("audit-logs")]
    [HttpGet("/api/admin/audit-logs")]
    [ProducesResponseType(typeof(List<NotificationAuditLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? action = null)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var clampedLimit = Math.Clamp(limit, 1, 200);
        var logs = await _auditLogService.GetAuditLogsAsync(clampedLimit, offset, action);
        return Ok(logs);
    }
}
