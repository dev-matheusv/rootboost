using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Notifications;

/// <summary>
/// Sends customer emails via the Resend HTTP API. Uses the named HttpClient "resend"
/// (base address + auth header set in DI). Throwing here is fine — the use case wraps every
/// notification call and never lets a mail failure fail the order.
/// </summary>
public sealed class ResendNotifier : INotifier
{
    private readonly IHttpClientFactory _factory;
    private readonly ResendOptions _opts;
    private readonly ILogger<ResendNotifier> _log;

    public ResendNotifier(IHttpClientFactory factory, IOptions<ResendOptions> opts, ILogger<ResendNotifier> log)
    {
        _factory = factory;
        _opts = opts.Value;
        _log = log;
    }

    public Task NotifyOrderConfirmedAsync(Order order, CancellationToken ct = default)
        => SendAsync(order.CustomerEmail,
            "Your order is confirmed 🎉",
            $"<p>Hi {WebEncode(order.ShipTo.Name)},</p>" +
            $"<p>We received your order <strong>{WebEncode(order.PaymentId)}</strong> and it's being prepared for shipping.</p>" +
            "<p>You'll get a tracking link by email as soon as it ships.</p>", ct);

    public Task NotifyTrackingAsync(Order order, CancellationToken ct = default)
        => SendAsync(order.CustomerEmail,
            "Your order has shipped 📦",
            $"<p>Hi {WebEncode(order.ShipTo.Name)},</p>" +
            $"<p>Good news — your order is on its way!</p>" +
            $"<p>Tracking number: <strong>{WebEncode(order.TrackingNumber ?? "")}</strong></p>", ct);

    public Task AlertFulfillmentFailedAsync(Order order, CancellationToken ct = default)
    {
        var to = string.IsNullOrWhiteSpace(_opts.OperatorEmail) ? _opts.FromEmail : _opts.OperatorEmail;
        return SendAsync(to,
            $"[ACTION NEEDED] Paid order failed to fulfill: {order.PaymentId}",
            $"<p>Order <strong>{WebEncode(order.PaymentId)}</strong> ({WebEncode(order.ProductKey)}) was PAID but could not be fulfilled.</p>" +
            $"<p>Reason: {WebEncode(order.FailureReason ?? "unknown")}</p>" +
            $"<p>Customer: {WebEncode(order.CustomerEmail)} — resolve this manually.</p>", ct);
    }

    private async Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            _log.LogWarning("Resend API key not configured; skipping email '{Subject}' to {To}.", subject, to);
            return;
        }

        var http = _factory.CreateClient("resend");
        var body = new
        {
            from = $"{_opts.FromName} <{_opts.FromEmail}>",
            to = new[] { to },
            subject,
            html
        };
        using var res = await http.PostAsJsonAsync("emails", body, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            _log.LogError("Resend send failed ({Status}) for '{Subject}': {Err}", (int)res.StatusCode, subject, err);
            throw new InvalidOperationException($"Resend returned {(int)res.StatusCode}");
        }
    }

    private static string WebEncode(string s) => System.Net.WebUtility.HtmlEncode(s);
}
