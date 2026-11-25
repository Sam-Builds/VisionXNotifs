using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Notifications.Api.Security;

public sealed class JwksKeyProvider
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JwksKeyProvider> _logger;
    private readonly object _lock = new();

    private List<SecurityKey> _cachedJwksKeys = new();
    private DateTime _lastFetchUtc = DateTime.MinValue;

    public JwksKeyProvider(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<JwksKeyProvider> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IEnumerable<SecurityKey> ResolveSigningKeys(string? kid)
    {
        var keys = new List<SecurityKey>();

        // 1. Static RSA Public Key PEM if configured
        var staticKey = LoadStaticPublicKey();
        if (staticKey != null)
        {
            keys.Add(staticKey);
        }

        // 2. Dynamic JWKS keys fetched from Auth /.well-known/jwks.json
        var needsRefresh = false;
        lock (_lock)
        {
            var expired = DateTime.UtcNow - _lastFetchUtc > TimeSpan.FromMinutes(15);
            var unknownKid = !string.IsNullOrWhiteSpace(kid) &&
                             !keys.Any(k => k.KeyId == kid) &&
                             !_cachedJwksKeys.Any(k => k.KeyId == kid);

            if ((expired || unknownKid) && DateTime.UtcNow - _lastFetchUtc > TimeSpan.FromSeconds(15))
            {
                needsRefresh = true;
            }
            else
            {
                keys.AddRange(_cachedJwksKeys);
            }
        }

        if (needsRefresh)
        {
            RefreshJwksSync();
            lock (_lock)
            {
                keys.AddRange(_cachedJwksKeys);
            }
        }

        return keys;
    }

    private SecurityKey? LoadStaticPublicKey()
    {
        try
        {
            var pem = _configuration["Jwt:PublicKeyPem"] ?? Environment.GetEnvironmentVariable("JWT_PUBLIC_KEY_PEM");
            if (string.IsNullOrWhiteSpace(pem))
            {
                var path = _configuration["Jwt:PublicKeyPath"] ?? Environment.GetEnvironmentVariable("JWT_PUBLIC_KEY_PATH");
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    pem = File.ReadAllText(path);
                }
            }

            if (string.IsNullOrWhiteSpace(pem)) return null;

            var rsa = RSA.Create();
            rsa.ImportFromPem(pem.Replace("\\n", "\n").Trim());
            var kid = _configuration["Jwt:KeyId"];
            return new RsaSecurityKey(rsa)
            {
                KeyId = string.IsNullOrWhiteSpace(kid) ? null : kid
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse configured RSA public key in Notifications API.");
            return null;
        }
    }

    private void RefreshJwksSync()
    {
        var jwksUrl = _configuration["Jwt:JwksUrl"] ?? Environment.GetEnvironmentVariable("JWT_JWKS_URL");
        if (string.IsNullOrWhiteSpace(jwksUrl)) return;

        try
        {
            var client = _httpClientFactory.CreateClient("JwksClient");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = client.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(response);
            var resolved = jwks.GetSigningKeys().ToList();

            lock (_lock)
            {
                _cachedJwksKeys = resolved;
                _lastFetchUtc = DateTime.UtcNow;
            }
            _logger.LogInformation("Refreshed {Count} RS256 public verification key(s) from JWKS {Url}.", resolved.Count, jwksUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed fetching JWKS from {Url}; relying on existing cached or static public keys.", jwksUrl);
            lock (_lock)
            {
                _lastFetchUtc = DateTime.UtcNow;
            }
        }
    }
}
