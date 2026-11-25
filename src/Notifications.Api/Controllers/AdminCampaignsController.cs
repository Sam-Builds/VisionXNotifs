using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Security;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/admin/campaigns")]
[Authorize]
public class AdminCampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly ILogger<AdminCampaignsController> _logger;

    public AdminCampaignsController(
        ICampaignService campaignService,
        ILogger<AdminCampaignsController> logger)
    {
        _campaignService = campaignService;
        _logger = logger;
    }

    private bool CheckAdminAccess() => User.IsAdmin() || User.HasPermission("notifications.campaigns.manage");

    /// <summary>
    /// Lists all notification campaigns with pagination and optional status filter.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CampaignDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCampaigns(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);
        var clampedOffset = Math.Max(0, offset);

        var campaigns = await _campaignService.GetCampaignsAsync(clampedLimit, clampedOffset, status);
        return Ok(campaigns);
    }

    /// <summary>
    /// Creates a new broadcast or targeted notification campaign.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorId = User.GetAuthUserId() ?? "admin";
        var actorEmail = User.GetEmail() ?? "admin@visionx.io";

        try
        {
            var campaign = await _campaignService.CreateCampaignAsync(actorId, actorEmail, request);
            return CreatedAtAction(nameof(GetCampaignById), new { id = campaign.Id }, campaign);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_REQUEST", message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves full details for a campaign by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCampaignById(Guid id)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var campaign = await _campaignService.GetCampaignByIdAsync(id);
        if (campaign == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = "Campaign not found." });
        }

        return Ok(campaign);
    }

    /// <summary>
    /// Approves and immediately dispatches or activates schedule for a campaign.
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendCampaign(Guid id)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var success = await _campaignService.ScheduleOrSendCampaignAsync(id, actorEmail);
        if (!success)
        {
            return BadRequest(new { code = "CANNOT_SEND", message = "Campaign could not be dispatched (it may already be completed or cancelled)." });
        }

        return Ok(new { success = true, campaignId = id });
    }

    /// <summary>
    /// Cancels a scheduled or draft campaign.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelCampaign(Guid id)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var success = await _campaignService.CancelCampaignAsync(id, actorEmail);
        if (!success)
        {
            return BadRequest(new { code = "CANNOT_CANCEL", message = "Campaign could not be cancelled (it may already be completed)." });
        }

        return Ok(new { success = true, campaignId = id });
    }

    /// <summary>
    /// Updates a draft campaign before sending.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCampaign(Guid id, [FromBody] CreateCampaignRequest request)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        try
        {
            var updated = await _campaignService.UpdateCampaignAsync(id, request, actorEmail);
            if (updated == null)
            {
                return NotFound(new { code = "NOT_FOUND", message = "Campaign not found or cannot be modified." });
            }
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_REQUEST", message = ex.Message });
        }
    }

    /// <summary>
    /// Approves a campaign for sending.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveCampaign(Guid id)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var success = await _campaignService.ApproveCampaignAsync(id, actorEmail);
        return success ? Ok(new { success = true, campaignId = id }) : NotFound();
    }

    /// <summary>
    /// Schedules a campaign for future delivery.
    /// </summary>
    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ScheduleCampaign(Guid id, [FromBody] ScheduleCampaignRequest request)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var actorEmail = User.GetEmail() ?? "admin@visionx.io";
        var success = await _campaignService.ScheduleCampaignAsync(id, request.ScheduledAt, actorEmail);
        return success ? Ok(new { success = true, campaignId = id, request.ScheduledAt }) : BadRequest();
    }

    /// <summary>
    /// Previews recipient count, device count, and preference suppressions for an audience filter.
    /// </summary>
    [HttpPost("preview-audience")]
    [HttpPost("/api/admin/audiences/preview")]
    [ProducesResponseType(typeof(AudiencePreviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewAudience([FromBody] PreviewAudienceRequest request)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var preview = await _campaignService.PreviewAudienceAsync(request.Audience, request.CategoryCode ?? "general");
        return Ok(preview);
    }

    /// <summary>
    /// Retrieves standard audience target categories and options for campaign composition.
    /// </summary>
    [HttpGet("/api/admin/audiences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAudiences()
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var targets = new[]
        {
            new { targetType = "All", label = "All Users (Broadcast)", description = "All registered students and faculty" },
            new { targetType = "Students", label = "Enrolled Students", description = "All active enrolled students across campus" },
            new { targetType = "Faculty", label = "Teaching Faculty", description = "All assigned teaching faculty" },
            new { targetType = "Department", label = "Specific Department", description = "Target students in a department" },
            new { targetType = "Semester", label = "Specific Semester", description = "Target students in a specific semester (1-8)" },
            new { targetType = "Section", label = "Specific Section", description = "Target students enrolled in a section" },
            new { targetType = "CourseOffering", label = "Course Offering / Subject", description = "Students enrolled in a specific course offering" },
            new { targetType = "Club", label = "Club / Society Members", description = "Active members of a campus club" },
            new { targetType = "SpecificUsers", label = "Targeted User IDs", description = "Explicit comma-separated user UUIDs" }
        };

        return Ok(targets);
    }

    /// <summary>
    /// Retrieves paginated delivery logs for a specific campaign.
    /// </summary>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType(typeof(List<DeliveryRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCampaignDeliveries(
        Guid id,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        if (!CheckAdminAccess())
        {
            return Forbid();
        }

        var clampedLimit = Math.Clamp(limit, 1, 200);
        var deliveries = await _campaignService.GetCampaignDeliveriesAsync(id, clampedLimit, offset);
        return Ok(deliveries);
    }
}

public class PreviewAudienceRequest
{
    public AudienceFilterDto? Audience { get; set; }
    public string? CategoryCode { get; set; }
}
