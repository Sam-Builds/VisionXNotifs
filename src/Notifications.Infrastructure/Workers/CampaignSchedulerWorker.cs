using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Application.Interfaces;
using Notifications.Domain.Enums;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Workers;

public class CampaignSchedulerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CampaignSchedulerWorker> _logger;

    public CampaignSchedulerWorker(IServiceProvider serviceProvider, ILogger<CampaignSchedulerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CampaignSchedulerWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueCampaignsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred during campaign scheduler processing cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("CampaignSchedulerWorker stopping.");
    }

    private async Task ProcessDueCampaignsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var campaignService = scope.ServiceProvider.GetRequiredService<ICampaignService>();

        var now = DateTime.UtcNow;

        var dueCampaignIds = await db.NotificationCampaigns
            .Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAt.HasValue && c.ScheduledAt.Value <= now)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (dueCampaignIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} scheduled campaigns due for dispatch", dueCampaignIds.Count);

        foreach (var id in dueCampaignIds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation("Dispatching scheduled campaign {CampaignId}...", id);
                await campaignService.ScheduleOrSendCampaignAsync(id, "CampaignSchedulerWorker");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch scheduled campaign {CampaignId}", id);
            }
        }
    }
}
