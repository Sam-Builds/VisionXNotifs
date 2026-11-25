using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class SessionStateService : ISessionStateService
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<SessionStateService> _logger;

    public SessionStateService(NotificationsDbContext db, ILogger<SessionStateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessSessionEventAsync(SessionEventWebhookDto eventDto)
    {
        if (string.IsNullOrWhiteSpace(eventDto.EventId))
        {
            throw new ArgumentException("EventId is required.");
        }

        // Check if event was already processed
        var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventId == eventDto.EventId);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Session event {EventId} already processed; skipping.", eventDto.EventId);
            return;
        }

        if (eventDto.EventType.Equals("user.banned", StringComparison.OrdinalIgnoreCase))
        {
            var ban = await _db.UserBanStates.FirstOrDefaultAsync(b => b.UserId == eventDto.UserId);
            if (ban == null)
            {
                _db.UserBanStates.Add(new UserBanState
                {
                    UserId = eventDto.UserId,
                    IsBanned = true,
                    BannedAt = DateTime.UtcNow,
                    Reason = eventDto.Reason,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                ban.IsBanned = true;
                ban.BannedAt = DateTime.UtcNow;
                ban.Reason = eventDto.Reason;
                ban.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (eventDto.EventType.Equals("user.unbanned", StringComparison.OrdinalIgnoreCase))
        {
            var ban = await _db.UserBanStates.FirstOrDefaultAsync(b => b.UserId == eventDto.UserId);
            if (ban != null)
            {
                ban.IsBanned = false;
                ban.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (!string.IsNullOrWhiteSpace(eventDto.SessionId))
        {
            var existingSession = await _db.SessionStates.FirstOrDefaultAsync(s => s.SessionId == eventDto.SessionId);
            var isBlocked = eventDto.Status.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ||
                            eventDto.Status.Equals("Revoked", StringComparison.OrdinalIgnoreCase);

            if (existingSession != null)
            {
                // Terminal revocation rule: once revoked or blocked, a session can never be re-allowed by an out-of-order event
                if (existingSession.Status.Equals("Blocked", StringComparison.OrdinalIgnoreCase) && !isBlocked)
                {
                    _logger.LogWarning("Ignoring attempt to re-allow already blocked session {SessionId}", eventDto.SessionId);
                }
                else if (eventDto.Sequence >= existingSession.Sequence || isBlocked)
                {
                    existingSession.Status = isBlocked ? "Blocked" : "Allowed";
                    existingSession.Reason = eventDto.Reason;
                    existingSession.Sequence = Math.Max(eventDto.Sequence, existingSession.Sequence);
                    existingSession.LastEventAt = DateTime.UtcNow;
                }
            }
            else
            {
                _db.SessionStates.Add(new SessionState
                {
                    SessionId = eventDto.SessionId,
                    UserId = eventDto.UserId,
                    Status = isBlocked ? "Blocked" : "Allowed",
                    Reason = eventDto.Reason,
                    Sequence = eventDto.Sequence,
                    LastEventAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventDto.EventId,
            EventType = eventDto.EventType,
            ProcessedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Processed session event {EventId} for user {UserId}", eventDto.EventId, eventDto.UserId);
    }

    public async Task<bool> IsSessionAllowedAsync(string sessionId, string userId)
    {
        if (await IsUserBannedAsync(userId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return true;
        }

        var session = await _db.SessionStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session != null && session.Status.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task<bool> IsUserBannedAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var ban = await _db.UserBanStates
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId);

        return ban != null && ban.IsBanned;
    }

    public bool VerifyWebhookSignature(string rawBody, string timestamp, string signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        if (long.TryParse(timestamp, out var epochSeconds))
        {
            var eventTime = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            var age = DateTimeOffset.UtcNow - eventTime;
            if (Math.Abs(age.TotalMinutes) > 5)
            {
                _logger.LogWarning("Webhook timestamp expired (age: {Age}s).", age.TotalSeconds);
                return false;
            }
        }
        else
        {
            return false;
        }

        var signedData = $"{timestamp}.{rawBody}";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedData));
        var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}
