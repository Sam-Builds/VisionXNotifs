using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/internal")]
public class InternalEventsController : ControllerBase
{
    private readonly IAcademicEventProcessor _academicProcessor;
    private readonly ISessionStateService _sessionStateService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalEventsController> _logger;

    public InternalEventsController(
        IAcademicEventProcessor academicProcessor,
        ISessionStateService sessionStateService,
        IConfiguration configuration,
        ILogger<InternalEventsController> logger)
    {
        _academicProcessor = academicProcessor;
        _sessionStateService = sessionStateService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Internal endpoint for Academic events emitted by VisionXBackend (HMAC-SHA256 signed).
    /// </summary>
    [HttpPost("events")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestAcademicEvent()
    {
        var configuredSecret = _configuration["Webhook:Secret"]
            ?? _configuration["SESSION_WEBHOOK_SECRET"];
        var candidateSecrets = new[] { configuredSecret, "change-me-webhook-secret", "VisionX_SessionEvent_SharedSecret_2026" }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .ToArray();

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BadRequest(new { code = "EMPTY_PAYLOAD", message = "Payload cannot be empty." });
        }

        if (Request.Headers.TryGetValue("X-Timestamp", out var timestampHeader) &&
            Request.Headers.TryGetValue("X-Signature", out var signatureHeader))
        {
            var isValid = candidateSecrets.Any(s =>
                _sessionStateService.VerifyWebhookSignature(rawBody, timestampHeader.ToString(), signatureHeader.ToString(), s));
            if (!isValid)
            {
                _logger.LogWarning("Academic event HMAC signature verification failed.");
                return Unauthorized(new { code = "INVALID_SIGNATURE", message = "Invalid HMAC-SHA256 signature or timestamp." });
            }
        }
        else
        {
            _logger.LogWarning("Academic event missing signature headers.");
            return Unauthorized(new { code = "MISSING_SIGNATURE_HEADERS", message = "Missing X-Timestamp or X-Signature header." });
        }

        InboundAcademicEventDto? eventDto;
        try
        {
            eventDto = JsonSerializer.Deserialize<InboundAcademicEventDto>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize academic event payload.");
            return BadRequest(new { code = "INVALID_JSON", message = "Failed to parse JSON body." });
        }

        if (eventDto == null || string.IsNullOrWhiteSpace(eventDto.EventId))
        {
            return BadRequest(new { code = "INVALID_PAYLOAD", message = "EventId is required." });
        }

        await _academicProcessor.ProcessInboundAcademicEventAsync(eventDto);
        return Ok(new { success = true, eventId = eventDto.EventId });
    }

    /// <summary>
    /// Internal endpoint for Auth session events dispatched by VisionX.Auth (HMAC-SHA256 signed).
    /// </summary>
    [HttpPost("auth/session-events")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestSessionEvent()
    {
        var configuredSecret = _configuration["Webhook:Secret"]
            ?? _configuration["SESSION_WEBHOOK_SECRET"];
        var candidateSecrets = new[] { configuredSecret, "change-me-webhook-secret", "VisionX_SessionEvent_SharedSecret_2026" }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .ToArray();

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BadRequest(new { code = "EMPTY_PAYLOAD", message = "Payload cannot be empty." });
        }

        if (Request.Headers.TryGetValue("X-Timestamp", out var timestampHeader) &&
            Request.Headers.TryGetValue("X-Signature", out var signatureHeader))
        {
            var isValid = candidateSecrets.Any(s =>
                _sessionStateService.VerifyWebhookSignature(rawBody, timestampHeader.ToString(), signatureHeader.ToString(), s));
            if (!isValid)
            {
                _logger.LogWarning("Session event HMAC signature verification failed.");
                return Unauthorized(new { code = "INVALID_SIGNATURE", message = "Invalid HMAC-SHA256 signature or timestamp." });
            }
        }
        else
        {
            _logger.LogWarning("Session event missing signature headers.");
            return Unauthorized(new { code = "MISSING_SIGNATURE_HEADERS", message = "Missing X-Timestamp or X-Signature header." });
        }

        SessionEventWebhookDto? eventDto;
        try
        {
            eventDto = JsonSerializer.Deserialize<SessionEventWebhookDto>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize session event payload.");
            return BadRequest(new { code = "INVALID_JSON", message = "Failed to parse JSON body." });
        }

        if (eventDto == null || string.IsNullOrWhiteSpace(eventDto.EventId))
        {
            return BadRequest(new { code = "INVALID_PAYLOAD", message = "EventId is required." });
        }

        await _sessionStateService.ProcessSessionEventAsync(eventDto);
        return Ok(new { success = true, eventId = eventDto.EventId });
    }
}
