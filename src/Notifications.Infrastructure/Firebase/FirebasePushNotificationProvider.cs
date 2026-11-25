using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notifications.Application.Interfaces;

namespace Notifications.Infrastructure.Firebase;

public class FirebasePushNotificationProvider : IPushNotificationProvider
{
    private readonly ILogger<FirebasePushNotificationProvider> _logger;
    private readonly FirebaseApp? _firebaseApp;
    private readonly bool _isConfigured;

    public string ProviderName => "FirebaseCloudMessaging";

    public FirebasePushNotificationProvider(IConfiguration configuration, ILogger<FirebasePushNotificationProvider> logger)
    {
        _logger = logger;

        try
        {
            var credentialsPath = configuration["Firebase:CredentialsPath"];
            var credentialsJson = configuration["Firebase:CredentialsJson"];
            var projectId = configuration["Firebase:ProjectId"];

            GoogleCredential? credential = null;
            string? extractedProjectId = null;

            if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
            {
                var candidates = new[]
                {
                    "/run/secrets/service-account.json",
                    "/run/secrets/firebase-service-account.json",
                    "/etc/firebase/service-account.json",
                    "./credentials/service-account.json"
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        credentialsPath = candidate;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(credentialsJson))
            {
                credential = GoogleCredential.FromJson(credentialsJson);
                try
                {
                    using var doc = JsonDocument.Parse(credentialsJson);
                    if (doc.RootElement.TryGetProperty("project_id", out var pidElem))
                    {
                        extractedProjectId = pidElem.GetString();
                    }
                }
                catch { }
            }
            else if (!string.IsNullOrWhiteSpace(credentialsPath) && File.Exists(credentialsPath))
            {
                credential = GoogleCredential.FromFile(credentialsPath);
                try
                {
                    var fileContent = File.ReadAllText(credentialsPath);
                    using var doc = JsonDocument.Parse(fileContent);
                    if (doc.RootElement.TryGetProperty("project_id", out var pidElem))
                    {
                        extractedProjectId = pidElem.GetString();
                    }
                }
                catch { }
            }

            // Auto-detect project ID from service account credentials if config is placeholder or missing
            if (!string.IsNullOrWhiteSpace(extractedProjectId) &&
                (string.IsNullOrWhiteSpace(projectId) || projectId == "visionx-app-notifications"))
            {
                projectId = extractedProjectId;
            }

            if (credential != null)
            {
                var appOptions = new AppOptions
                {
                    Credential = credential,
                    ProjectId = projectId
                };

                _firebaseApp = FirebaseApp.GetInstance("visionx-notifications-fcm") ??
                               FirebaseApp.Create(appOptions, "visionx-notifications-fcm");
                _isConfigured = true;
                _logger.LogInformation("Firebase Cloud Messaging initialized successfully for ProjectId={ProjectId}.", projectId);
            }
            else
            {
                _logger.LogWarning("Firebase credentials not configured or file not found. Push notifications will run in mock mode.");
                _isConfigured = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FirebaseApp. Push notifications will run in mock mode.");
            _isConfigured = false;
        }
    }

    public async Task<PushSendResult> SendPushAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || _firebaseApp == null)
        {
            _logger.LogDebug("[Mock Push] FCM not configured. Simulating delivery for token {TokenPrefix}...",
                message.FcmToken.Length > 10 ? message.FcmToken[..10] : message.FcmToken);

            return new PushSendResult
            {
                Success = true,
                ProviderMessageId = $"mock_fcm_{Guid.NewGuid():N}"
            };
        }

        try
        {
            var dataPayload = new Dictionary<string, string>
            {
                ["title"] = message.Title,
                ["body"] = message.Body,
                ["priority"] = message.Priority
            };

            if (!string.IsNullOrEmpty(message.NotificationId))
            {
                dataPayload["notificationId"] = message.NotificationId;
            }

            if (!string.IsNullOrEmpty(message.CategoryCode))
            {
                dataPayload["categoryCode"] = message.CategoryCode;
            }

            if (!string.IsNullOrEmpty(message.RouteKey))
            {
                dataPayload["routeKey"] = message.RouteKey;
            }

            if (message.RouteParameters != null && message.RouteParameters.Count > 0)
            {
                dataPayload["routeParams"] = JsonSerializer.Serialize(message.RouteParameters);
            }

            if (message.CustomData != null)
            {
                foreach (var (k, v) in message.CustomData)
                {
                    if (!dataPayload.ContainsKey(k))
                    {
                        dataPayload[k] = v;
                    }
                }
            }

            var fcmMessage = new Message
            {
                Token = message.FcmToken,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = message.Title,
                    Body = message.Body,
                    ImageUrl = message.ImageUrl
                },
                Data = dataPayload,
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ClickAction = "FLUTTER_NOTIFICATION_CLICK",
                        ChannelId = string.IsNullOrWhiteSpace(message.CategoryCode) ? "general" : message.CategoryCode.ToLowerInvariant(),
                        Icon = "@mipmap/ic_launcher",
                        DefaultSound = true,
                        DefaultVibrateTimings = true,
                        EventTimestamp = DateTime.UtcNow
                    }
                },
                Apns = new ApnsConfig
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["apns-priority"] = message.Priority.Equals("High", StringComparison.OrdinalIgnoreCase) ? "10" : "5"
                    },
                    Aps = new Aps
                    {
                        Alert = new ApsAlert
                        {
                            Title = message.Title,
                            Body = message.Body
                        },
                        Badge = 1,
                        Sound = "default"
                    }
                }
            };

            var messaging = FirebaseMessaging.GetMessaging(_firebaseApp);
            var response = await messaging.SendAsync(fcmMessage, cancellationToken);

            _logger.LogInformation("FCM message sent successfully with MessageId={MessageId}", response);
            return new PushSendResult
            {
                Success = true,
                ProviderMessageId = response
            };
        }
        catch (FirebaseMessagingException fme)
        {
            _logger.LogWarning(fme, "FirebaseMessagingException while sending push to token: {ErrorCode} - {Message}. Target ProjectId={ProjectId}",
                fme.MessagingErrorCode, fme.Message, _firebaseApp?.Options?.ProjectId);

            var isInvalid = fme.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                            fme.MessagingErrorCode == MessagingErrorCode.InvalidArgument ||
                            fme.Message.Contains("not a valid FCM registration token", StringComparison.OrdinalIgnoreCase) ||
                            fme.Message.Contains("Requested entity was not found", StringComparison.OrdinalIgnoreCase);

            return new PushSendResult
            {
                Success = false,
                ErrorCode = fme.MessagingErrorCode.ToString(),
                ErrorMessage = fme.Message,
                IsInvalidToken = isInvalid,
                IsRateLimited = fme.MessagingErrorCode == MessagingErrorCode.QuotaExceeded
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching push notification");
            return new PushSendResult
            {
                Success = false,
                ErrorCode = "UnexpectedError",
                ErrorMessage = ex.Message
            };
        }
    }
}
