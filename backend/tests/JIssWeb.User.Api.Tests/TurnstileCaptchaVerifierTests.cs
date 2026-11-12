using System.Net;
using JIssWeb.User.Api.Options;
using JIssWeb.User.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace JIssWeb.User.Api.Tests;

public sealed class TurnstileCaptchaVerifierTests
{
    private static TurnstileCaptchaVerifier BuildVerifier(HttpMessageHandler handler, int timeoutSeconds = 5)
    {
        var settings = MsOptions.Create(new CaptchaSettings
        {
            Enabled = true,
            SecretKey = "test-secret",
            TimeoutSeconds = timeoutSeconds,
        });

        var factory = new Mock<IHttpClientFactory>();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com") };
        factory.Setup(f => f.CreateClient("turnstile")).Returns(client);

        return new TurnstileCaptchaVerifier(factory.Object, settings, NullLogger<TurnstileCaptchaVerifier>.Instance);
    }

    [Fact]
    public async Task Returns_true_when_siteverify_succeeds()
    {
        var handler = new StaticHttpHandler(HttpStatusCode.OK, """{"success":true}""");
        var verifier = BuildVerifier(handler);

        var result = await verifier.VerifyAsync("valid-token", null);

        Assert.True(result);
    }

    [Fact]
    public async Task Returns_false_when_siteverify_returns_success_false()
    {
        var handler = new StaticHttpHandler(HttpStatusCode.OK, """{"success":false}""");
        var verifier = BuildVerifier(handler);

        var result = await verifier.VerifyAsync("bad-token", null);

        Assert.False(result);
    }

    [Fact]
    public async Task Returns_false_when_siteverify_returns_non_2xx()
    {
        var handler = new StaticHttpHandler(HttpStatusCode.ServiceUnavailable, "");
        var verifier = BuildVerifier(handler);

        var result = await verifier.VerifyAsync("any-token", null);

        Assert.False(result);
    }

    [Fact]
    public async Task Returns_false_on_timeout_fail_closed()
    {
        // handler delays longer than the configured timeout
        var handler = new DelayedHttpHandler(delay: TimeSpan.FromSeconds(10));
        var verifier = BuildVerifier(handler, timeoutSeconds: 1);

        var result = await verifier.VerifyAsync("any-token", "1.2.3.4");

        Assert.False(result);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private sealed class StaticHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedHttpHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
