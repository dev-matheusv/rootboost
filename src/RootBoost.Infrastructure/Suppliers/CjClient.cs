using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Suppliers;

/// <summary>
/// CJ Dropshipping supplier client. Registered as a singleton so the access token cache survives
/// across requests (CJ rate-limits getAccessToken). Transient HTTP retries are configured on the
/// named HttpClient "cj" in DI; here we only escalate real, non-transient failures as Fail.
/// Payload fields for createOrderV2 confirmed against the CJ API v2.0 docs
/// (https://developers.cjdropshipping.cn/en/api/api2/api/shopping.html).
/// </summary>
public sealed class CjClient : ISupplierClient
{
    private readonly IHttpClientFactory _factory;
    private readonly CjOptions _opts;
    private readonly ILogger<CjClient> _log;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public CjClient(IHttpClientFactory factory, IOptions<CjOptions> opts, ILogger<CjClient> log)
    {
        _factory = factory;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<SupplierOrderResult> CreateOrderAsync(
        string orderReference, CatalogProduct product, ShippingAddress shipTo, int quantity, CancellationToken ct = default)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var http = _factory.CreateClient("cj");
            using var req = new HttpRequestMessage(HttpMethod.Post, "shopping/order/createOrderV2");
            req.Headers.Add("CJ-Access-Token", token);

            // Confirmed createOrderV2 fields. We only send what we can trust; optional CJ fields
            // (payType, taxId, iossNumber, consigneeID...) are omitted intentionally.
            var payload = new
            {
                orderNumber = orderReference,
                shippingCountryCode = shipTo.CountryCode,
                shippingProvince = shipTo.State,
                shippingCity = shipTo.City,
                shippingAddress = shipTo.Line1,
                shippingAddress2 = shipTo.Line2,
                shippingCustomerName = shipTo.Name,
                shippingZip = shipTo.Zip,
                shippingPhone = shipTo.Phone,
                email = "", // buyer email is optional to CJ; kept blank to avoid leaking PII to supplier
                logisticName = product.LogisticName, // choose a US/EU-warehouse line; null lets CJ pick
                remark = (string?)null,
                products = new[] { new { vid = product.SupplierVariantId, quantity } }
            };

            req.Content = JsonContent.Create(payload);
            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                return SupplierOrderResult.Fail($"CJ HTTP {(int)res.StatusCode}: {Truncate(json)}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // CJ envelope: { code, result, message, data }
            var ok = root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "unknown CJ error";
                return SupplierOrderResult.Fail($"CJ rejected order: {msg}");
            }

            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("orderId", out var orderIdEl) &&
                orderIdEl.GetString() is { Length: > 0 } orderId)
            {
                return SupplierOrderResult.Ok(orderId);
            }

            return SupplierOrderResult.Fail($"CJ success but no orderId in response: {Truncate(json)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "CJ createOrderV2 failed for {OrderReference}.", orderReference);
            return SupplierOrderResult.Fail($"CJ exception: {ex.Message}");
        }
    }

    /// <summary>Cached token; refreshes when missing or within 5 minutes of expiry.</summary>
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-5))
                return _token;

            var http = _factory.CreateClient("cj");
            var body = new { email = _opts.Email, apiKey = _opts.ApiKey };
            using var res = await http.PostAsJsonAsync("authentication/getAccessToken", body, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            _token = data.GetProperty("accessToken").GetString()
                     ?? throw new InvalidOperationException("CJ returned no accessToken");

            // CJ returns accessTokenExpiryDate (e.g. "2024-01-01T00:00:00+08:00"); fall back to 12h.
            _tokenExpiresAt = data.TryGetProperty("accessTokenExpiryDate", out var exp)
                              && DateTimeOffset.TryParse(exp.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.AddHours(12);

            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
