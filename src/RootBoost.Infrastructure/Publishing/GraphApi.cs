using System.Net.Http.Headers;
using System.Text.Json;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>Chamadas ao Graph da Meta com o token no cabeçalho (nunca na URL, que vai pro log).</summary>
internal static class GraphApi
{
    public static async Task<JsonDocument> SendAsync(HttpClient http, HttpMethod method, string path, string token,
        IDictionary<string, string>? form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (form is not null) req.Content = new FormUrlEncodedContent(form);

        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(DescribeError((int)res.StatusCode, body));
        return JsonDocument.Parse(body);
    }

    /// <summary>Erro do Graph: <c>{"error":{"message","code","error_subcode"}}</c>.</summary>
    public static string DescribeError(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                var code = e.TryGetProperty("code", out var c) ? c.ToString() : "?";
                var sub = e.TryGetProperty("error_subcode", out var s) ? $"/{s}" : "";
                var msg = e.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"Meta {status} (código {code}{sub}): {msg}";
            }
        }
        catch (JsonException) { }
        return $"Meta {status}";
    }

    public static string Str(this JsonDocument doc, string name) =>
        doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
