using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notifications.Application.DTOs;
using Notifications.Application.Interfaces;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Services;

public class AudienceResolverClient : IAudienceResolverClient
{
    private readonly HttpClient _httpClient;
    private readonly NotificationsDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AudienceResolverClient> _logger;

    public AudienceResolverClient(
        HttpClient httpClient,
        NotificationsDbContext db,
        IConfiguration configuration,
        ILogger<AudienceResolverClient> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<string>> ResolveAudienceUserIdsAsync(AudienceFilterDto filter, CancellationToken cancellationToken = default)
    {
        // 1. Explicit user IDs
        if (filter.SpecificUserIds != null && filter.SpecificUserIds.Count > 0)
        {
            var userIds = filter.SpecificUserIds;
            if (filter.ExcludedUserIds != null && filter.ExcludedUserIds.Count > 0)
            {
                var excludeSet = new HashSet<string>(filter.ExcludedUserIds, StringComparer.OrdinalIgnoreCase);
                userIds = userIds.Where(u => !excludeSet.Contains(u)).ToList();
            }
            return userIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // 2. Query VisionX Backend internal resolution endpoint if configured
        var backendUrl = _configuration["VisionXBackend:BaseUrl"] ?? "http://localhost:8000";
        try
        {
            var endpoint = $"{backendUrl.TrimEnd('/')}/api/internal/audiences/resolve";
            _logger.LogInformation("Calling VisionXBackend audience resolution endpoint: {Endpoint}", endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, filter, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AudienceResolutionResult>(cancellationToken: cancellationToken);
                if (result != null && result.UserIds != null)
                {
                    var resolved = result.UserIds;
                    if (filter.ExcludedUserIds != null && filter.ExcludedUserIds.Count > 0)
                    {
                        var excludeSet = new HashSet<string>(filter.ExcludedUserIds, StringComparer.OrdinalIgnoreCase);
                        resolved = resolved.Where(u => !excludeSet.Contains(u)).ToList();
                    }
                    return resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
            else
            {
                _logger.LogWarning("VisionXBackend audience resolution returned HTTP {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve audience from VisionXBackend. Falling back to local device database.");
        }

        // 3. Fallback: If TargetType is "All" or backend is offline, return all known user IDs with active devices
        if (filter.TargetType.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            filter.TargetType.Equals("Students", StringComparison.OrdinalIgnoreCase))
        {
            var knownUsers = await _db.Devices
                .AsNoTracking()
                .Where(d => d.IsActive)
                .Select(d => d.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (filter.ExcludedUserIds != null && filter.ExcludedUserIds.Count > 0)
            {
                var excludeSet = new HashSet<string>(filter.ExcludedUserIds, StringComparer.OrdinalIgnoreCase);
                knownUsers = knownUsers.Where(u => !excludeSet.Contains(u)).ToList();
            }

            return knownUsers;
        }

        return new List<string>();
    }

    private sealed class AudienceResolutionResult
    {
        public List<string>? UserIds { get; set; }
        public int TotalCount { get; set; }
    }
}
