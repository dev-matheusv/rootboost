using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Stripe Checkout (hosted) gateway. Creates a Session priced from OUR catalog, so the browser can
/// never dictate the amount, and returns the Stripe URL the buyer is redirected to. Fulfillment is
/// NOT done here: it happens when checkout.session.completed arrives at the webhook, which is the
/// only source we trust. The request shape below was verified against the live Stripe API.
/// </summary>
public sealed class StripeCheckoutGateway : ICheckoutGateway
{
    private readonly IHttpClientFactory _factory;
    private readonly IProductCatalog _catalog;
    private readonly StripeOptions _opts;
    private readonly ILogger<StripeCheckoutGateway> _log;

    public StripeCheckoutGateway(
        IHttpClientFactory factory, IProductCatalog catalog,
        IOptions<StripeOptions> opts, ILogger<StripeCheckoutGateway> log)
    {
        _factory = factory;
        _catalog = catalog;
        _opts = opts.Value;
        _log = log;
    }

    public string Provider => "stripe";

    public async Task<CreateCheckoutResult> CreateOrderAsync(string productKey, int quantity, string? lang = null, CancellationToken ct = default)
    {
        var product = _catalog.Find(productKey);
        if (product is null) return CreateCheckoutResult.Fail($"unknown product '{productKey}'");
        if (quantity <= 0) quantity = 1;

        // Price is resolved HERE, from the catalog, in the smallest currency unit.
        var unitAmount = (long)Math.Round(product.Price * 100m, MidpointRounding.AwayFromZero);

        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("line_items[0][quantity]", quantity.ToString(CultureInfo.InvariantCulture)),
            new("line_items[0][price_data][currency]", product.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", unitAmount.ToString(CultureInfo.InvariantCulture)),
            new("line_items[0][price_data][product_data][name]", product.DisplayName ?? product.Key),
            // metadata rides along to the webhook so we know what to fulfill.
            new("metadata[productKey]", product.Key),
            new("metadata[quantity]", quantity.ToString(CultureInfo.InvariantCulture)),
            new("payment_intent_data[metadata][productKey]", product.Key),
            new("client_reference_id", product.Key),
            new("success_url", BuildUrl(_opts.SuccessUrl, product, lang)),
            new("cancel_url", BuildUrl(_opts.CancelUrl, product, lang)),
        };

        // We ship physical goods, so Stripe must collect a shipping address for the supplier.
        var countries = (_opts.AllowedCountries ?? "US")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < countries.Length; i++)
            form.Add(new($"shipping_address_collection[allowed_countries][{i}]", countries[i].ToUpperInvariant()));

        try
        {
            var http = _factory.CreateClient("stripe");
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/checkout/sessions")
            {
                Content = new FormUrlEncodedContent(form)
            };
            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("Stripe create session HTTP {Status}: {Body}", (int)res.StatusCode, Truncate(json));
                return CreateCheckoutResult.Fail($"stripe HTTP {(int)res.StatusCode}");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var url = root.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
                return CreateCheckoutResult.Fail("stripe session without id/url");

            return CreateCheckoutResult.OkHosted(id, product.Price * quantity, product.Currency, url);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe create session failed for {ProductKey}.", productKey);
            return CreateCheckoutResult.Fail($"stripe exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Not used by the hosted flow: Stripe captures the payment itself and tells us through the
    /// webhook. Kept to satisfy the port; returning null means "nothing captured here".
    /// </summary>
    public Task<PaymentEvent?> CaptureOrderAsync(string providerOrderId, CancellationToken ct = default)
        => Task.FromResult<PaymentEvent?>(null);

    /// <summary>
    /// Builds the return URL from one configured template. Uses {landingPath} and {lang} because
    /// the landing folder is NOT the catalog key (key "veggiechopper" lives at /kitchen/), and
    /// sending the buyer to the wrong folder after paying would land them on a 404.
    /// Falls back to the product's first configured language when the browser did not send one.
    /// </summary>
    private static string BuildUrl(string template, CatalogProduct product, string? lang)
    {
        if (string.IsNullOrWhiteSpace(template)) return "https://example.com/";

        var language = !string.IsNullOrWhiteSpace(lang) && product.Languages.Contains(lang!, StringComparer.OrdinalIgnoreCase)
            ? lang!.ToLowerInvariant()
            : (product.Languages.Count > 0 ? product.Languages[0] : "en");

        return template
            .Replace("{landingPath}", product.PathOrKey, StringComparison.Ordinal)
            .Replace("{lang}", language, StringComparison.Ordinal)
            .Replace("{productKey}", product.Key, StringComparison.Ordinal);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
