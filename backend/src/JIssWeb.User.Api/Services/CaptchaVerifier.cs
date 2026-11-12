using System.Net.Http.Json;
using JIssWeb.User.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.User.Api.Services;

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string token, string? remoteIp);
}

public sealed class TurnstileCaptchaVerifier : ICaptchaVerifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CaptchaSettings _settings;
    private readonly ILogger<TurnstileCaptchaVerifier> _logger;

    public TurnstileCaptchaVerifier(
        IHttpClientFactory httpClientFactory,
        IOptions<CaptchaSettings> settings,
        ILogger<TurnstileCaptchaVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string token, string? remoteIp)
    {
        var client = _httpClientFactory.CreateClient("turnstile");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["secret"] = _settings.SecretKey,
                ["response"] = token,
            };
            if (!string.IsNullOrWhiteSpace(remoteIp))
                payload["remoteip"] = remoteIp;

            var response = await client.PostAsync(
                "/turnstile/v0/siteverify",
                new FormUrlEncodedContent(payload),
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile siteverify returned HTTP {Status}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: cts.Token);
            return result?.Success == true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Turnstile siteverify timed out after {Seconds}s", _settings.TimeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile siteverify request failed");
            return false;
        }
    }

    private sealed class TurnstileResponse
    {
        public bool Success { get; set; }
    }
}
