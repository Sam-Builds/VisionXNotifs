using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(NotificationsDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(
        string? actorId,
        string? actorEmail,
        string action,
        string targetType,
        string? targetId,
        object? details,
        string? correlationId = null,
        string? clientIp = null)
    {
        try
        {
            var log = new NotificationAuditLog
            {
                ActorId = actorId,
                ActorEmail = actorEmail,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                DetailsJson = details != null ? JsonSerializer.Serialize(details) : null,
                CorrelationId = correlationId,
                ClientIp = clientIp,
                Timestamp = DateTime.UtcNow
            };

            _db.NotificationAuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit log entry: {Action} on {TargetType}:{TargetId}", action, targetType, targetId);
        }
    }

    public async Task<List<NotificationAuditLog>> GetAuditLogsAsync(int limit, int offset, string? action)
    {
        var query = _db.NotificationAuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
