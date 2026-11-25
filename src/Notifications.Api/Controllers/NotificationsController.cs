using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Security;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        ISessionStateService sessionStateService,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves paginated notifications inbox for the authenticated user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedNotificationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetInbox(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] bool? unreadOnly = null,
        [FromQuery] string? category = null)
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

        var clampedLimit = Math.Clamp(limit, 1, 100);
        var clampedOffset = Math.Max(0, offset);

        var result = await _notificationService.GetUserNotificationsAsync(userId, clampedLimit, clampedOffset, unreadOnly, category);
        return Ok(result);
    }

    /// <summary>
    /// Gets the count of unread notifications for badge display.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(new UnreadCountResponse { UnreadCount = count });
    }

    /// <summary>
    /// Retrieves details for a specific notification in user's inbox.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var item = await _notificationService.GetNotificationByIdAsync(userId, id);
        if (item == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Notification not found." });
        }

        return Ok(item);
    }

    /// <summary>
    /// Marks a specific notification as read.
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var success = await _notificationService.MarkAsReadAsync(userId, id);
        if (!success)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Notification not found." });
        }

        return Ok(new { success = true, notificationId = id });
    }

    /// <summary>
    /// Marks all notifications in user's inbox as read.
    /// </summary>
    [HttpPost("read-all")]
    [HttpPatch("read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var count = await _notificationService.MarkAllAsReadAsync(userId);
        return Ok(new { success = true, markedCount = count });
    }

    /// <summary>
    /// Archives/deletes a notification from user's view.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HttpPatch("{id:guid}/archive")]
    [HttpDelete("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveNotification(Guid id)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var success = await _notificationService.ArchiveNotificationAsync(userId, id);
        if (!success)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Notification not found." });
        }

        return Ok(new { success = true, notificationId = id });
    }
}
