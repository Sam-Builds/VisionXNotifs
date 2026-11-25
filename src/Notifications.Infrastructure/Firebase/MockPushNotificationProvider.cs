using Microsoft.Extensions.Logging;
using Notifications.Application.Interfaces;

namespace Notifications.Infrastructure.Firebase;

public class MockPushNotificationProvider : IPushNotificationProvider
{
    private readonly ILogger<MockPushNotificationProvider> _logger;

    public MockPushNotificationProvider(ILogger<MockPushNotificationProvider> logger)
    {
        _logger = logger;
    }

    public string ProviderName => "MockFCM";

    public Task<PushSendResult> SendPushAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MockFCM] Push dispatched to token '{TokenPrefix}...': Title='{Title}', Body='{Body}', Route='{RouteKey}'",
            message.FcmToken.Length > 10 ? message.FcmToken[..10] : message.FcmToken,
            message.Title,
            message.Body,
            message.RouteKey ?? "none");

        // Simulate invalid token if token contains "invalid"
        if (message.FcmToken.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new PushSendResult
            {
                Success = false,
                ErrorCode = "registration-token-not-registered",
                ErrorMessage = "Mock invalid registration token",
                IsInvalidToken = true
            });
        }

        var messageId = $"projects/mock-visionx-app/messages/mock_{Guid.NewGuid():N}";
        return Task.FromResult(new PushSendResult
        {
            Success = true,
            ProviderMessageId = messageId
        });
    }
}
