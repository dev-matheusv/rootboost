using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Ai;

namespace RootBoost.Infrastructure.Ai;

/// <summary>
/// Lê uma página pública pro cadastro por conversa, sem abrir porta pra rede interna (SSRF): só http/https nas portas
/// padrão, todo IP resolvido tem que ser público, a conexão vai pro IP já conferido (sem troca de DNS no meio), os
/// redirecionamentos são seguidos à mão e conferidos de novo, com tempo e tamanho limitados. Devolve só o texto.
/// </summary>
public sealed class SafeWebPageReader : IWebPageReader
{
    private const int MaxBytes = 1_000_000;
    private const int MaxRedirects = 3;
    private static readonly Regex Scripts = new(@"<(script|style|noscript|svg)[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private readonly ILogger<SafeWebPageReader> _log;

    public SafeWebPageReader(ILogger<SafeWebPageReader> log) => _log = log;

    public async Task<string?> ReadTextAsync(Uri url, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = ConnectToPublicAddressAsync,
            }) { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RootBoostCadastro", "1.0"));

            var current = url;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!Allowed(current)) return null;
                using var res = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } next)
                {
                    current = next.IsAbsoluteUri ? next : new Uri(current, next);
                    continue;
                }
                if (!res.IsSuccessStatusCode) return null;
                var type = res.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
                    return null;
                var html = await ReadLimitedAsync(res, ct);
                return ToText(html);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or InvalidOperationException)
        {
            _log.LogInformation("Não li o site {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    public static bool Allowed(Uri u) =>
        (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && u.IsDefaultPort && !string.IsNullOrEmpty(u.Host) && !u.IsLoopback;

    /// <summary>Endereço público de verdade: fora de loopback, rede privada, link-local, CGNAT, multicast e afins.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224 || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
        }
        return false;
    }

    /// <summary>Resolve, confere todos os IPs e conecta no primeiro público: o DNS não troca de endereço entre conferir e usar.</summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new HttpRequestException("endereço interno recusado");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses[0], ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) break;
        }
        var charset = res.Content.Headers.ContentType?.CharSet;
        Encoding enc;
        try { enc = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { enc = Encoding.UTF8; }
        return enc.GetString(buffer.GetBuffer(), 0, (int)Math.Min(buffer.Length, MaxBytes));
    }

    public static string ToText(string html)
    {
        var noScripts = Scripts.Replace(html, " ");
        var text = WebUtility.HtmlDecode(Tags.Replace(noScripts, " "));
        return Spaces.Replace(text, " ").Trim();
    }
}
