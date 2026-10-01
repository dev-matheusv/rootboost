using System.Security.Cryptography;
using System.Text;
using RootBoost.Infrastructure.Payments;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Invariante 2: nunca confiar num webhook sem assinatura valida. Sem isso qualquer um posta um
/// evento "pago" falso e leva produto de graca.
/// </summary>
public class StripeSignatureTests
{
    private const string Secret = "whsec_test_secret";
    private const string Body = """{"type":"checkout.session.completed"}""";

    private static string Header(string body, string secret, DateTimeOffset when)
    {
        var t = when.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{body}"));
        return $"t={t},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Fact]
    public void Accepts_a_genuine_signature()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(StripeWebhookVerifier.IsSignatureValid(Body, Header(Body, Secret, now), Secret, now));
    }

    [Fact]
    public void Rejects_a_signature_made_with_another_secret()
    {
        var now = DateTimeOffset.UtcNow;
        var forged = Header(Body, "whsec_wrong", now);
        Assert.False(StripeWebhookVerifier.IsSignatureValid(Body, forged, Secret, now));
    }

    [Fact]
    public void Rejects_when_the_body_was_tampered_with()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Header(Body, Secret, now);
        var tampered = """{"type":"checkout.session.completed","evil":true}""";
        Assert.False(StripeWebhookVerifier.IsSignatureValid(tampered, header, Secret, now));
    }

    [Fact]
    public void Rejects_an_old_signature_replay()
    {
        var signedLongAgo = DateTimeOffset.UtcNow.AddMinutes(-30);
        var header = Header(Body, Secret, signedLongAgo);
        Assert.False(StripeWebhookVerifier.IsSignatureValid(Body, header, Secret, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rejects_a_malformed_header()
    {
        Assert.False(StripeWebhookVerifier.IsSignatureValid(Body, "garbage", Secret));
    }
}
