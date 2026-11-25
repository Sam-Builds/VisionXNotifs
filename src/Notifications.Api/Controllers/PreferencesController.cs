using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Security;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/preferences")]
[Route("api/notifications/preferences")]
[Authorize]
public class PreferencesController : ControllerBase
{
    private readonly IPreferenceService _preferenceService;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<PreferencesController> _logger;

    public PreferencesController(
        IPreferenceService preferenceService,
        ISessionStateService sessionStateService,
        ILogger<PreferencesController> logger)
    {
        _preferenceService = preferenceService;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves notification category preferences for the authenticated user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserPreferencesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPreferences()
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        var result = await _preferenceService.GetUserPreferencesAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Updates notification preferences for the authenticated user.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(UserPreferencesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMyPreferences([FromBody] UpdatePreferencesRequest request)
    {
        var userId = User.GetAuthUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { code = "INVALID_AUTH_CLAIM", message = "Missing sub claim." });
        }

        if (request.Preferences == null)
        {
            return BadRequest(new { code = "INVALID_REQUEST", message = "Preferences array is required." });
        }

        var result = await _preferenceService.UpdateUserPreferencesAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Lists all active notification categories available for preferences or subscriptions.
    /// </summary>
    [HttpGet("categories")]
    [HttpGet("/api/notifications/categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<NotificationCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveCategories()
    {
        var categories = await _preferenceService.GetActiveCategoriesAsync();
        return Ok(categories);
    }
}
