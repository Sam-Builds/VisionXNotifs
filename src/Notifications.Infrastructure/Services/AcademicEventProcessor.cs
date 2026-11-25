using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class AcademicEventProcessor : IAcademicEventProcessor
{
    private readonly NotificationsDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly IAudienceResolverClient _audienceResolver;
    private readonly ILogger<AcademicEventProcessor> _logger;

    public AcademicEventProcessor(
        NotificationsDbContext db,
        INotificationService notificationService,
        IAudienceResolverClient audienceResolver,
        ILogger<AcademicEventProcessor> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _audienceResolver = audienceResolver;
        _logger = logger;
    }

    public async Task<bool> ProcessInboundAcademicEventAsync(InboundAcademicEventDto eventDto)
    {
        if (string.IsNullOrWhiteSpace(eventDto.EventId))
        {
            throw new ArgumentException("EventId is required.");
        }

        // Idempotency check
        var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventId == eventDto.EventId);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Academic event {EventId} was already processed. Skipping.", eventDto.EventId);
            return true;
        }

        // Determine category code
        var category = DetermineCategory(eventDto.EventType, eventDto.CategoryCode);

        // Resolve recipients
        List<string> recipientUserIds = new();
        if (eventDto.DirectRecipientUserIds != null && eventDto.DirectRecipientUserIds.Count > 0)
        {
            recipientUserIds = eventDto.DirectRecipientUserIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        else if (eventDto.TargetAudience != null)
        {
            recipientUserIds = await _audienceResolver.ResolveAudienceUserIdsAsync(eventDto.TargetAudience);
        }

        _logger.LogInformation("Processing academic event '{EventType}' ({EventId}) for {Count} recipients with category '{Category}'",
            eventDto.EventType, eventDto.EventId, recipientUserIds.Count, category);

        foreach (var userId in recipientUserIds)
        {
            await _notificationService.CreateNotificationRecordAsync(
                userId: userId,
                title: eventDto.Title,
                body: eventDto.Body,
                categoryCode: category,
                routeKey: eventDto.RouteKey,
                routeParams: eventDto.RouteParameters,
                eventId: eventDto.EventId,
                source: $"Academic:{eventDto.EventType}");
        }

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventDto.EventId,
            EventType = eventDto.EventType,
            ProcessedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Successfully processed academic event {EventId}", eventDto.EventId);

        return true;
    }

    private static string DetermineCategory(string eventType, string? fallbackCategory)
    {
        var lower = eventType.ToLowerInvariant();
        if (lower.Contains("attendance")) return "attendance";
        if (lower.Contains("assignment")) return "assignments";
        if (lower.Contains("marks") || lower.Contains("result") || lower.Contains("grade") || lower.Contains("assessment")) return "grades";
        if (lower.Contains("timetable") || lower.Contains("schedule")) return "timetable";
        if (lower.Contains("club") || lower.Contains("society")) return "clubs";
        if (lower.Contains("fee") || lower.Contains("finance") || lower.Contains("payment")) return "financial";
        if (!string.IsNullOrWhiteSpace(fallbackCategory)) return fallbackCategory.ToLowerInvariant();
        return "academic";
    }
}
