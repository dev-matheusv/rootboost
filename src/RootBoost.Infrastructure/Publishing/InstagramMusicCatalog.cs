using System.Text.Json;
using Microsoft.Extensions.Options;
using RootBoost.Application.Publishing;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>
/// Audio API do Instagram, conferida na doc "Audio API" em 03/11/2026: <c>GET /ig_audio?audio_type=music&amp;user_id=</c>
/// e <c>search_query</c> opcional (sem ela, as em alta). Resposta <c>{"audio":[{audio_id, title, display_artist,
/// duration_in_ms, download_url, cover_artwork_thumbnail_uri}]}</c>; <c>download_url</c> é prévia temporária (~1,5 dia).
/// Só traz música liberada pra terceiros; a seleção pode diferir do app.
/// </summary>
public sealed class InstagramMusicCatalog : IMusicCatalog
{
    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;

    public InstagramMusicCatalog(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
    }

    public async Task<IReadOnlyList<MusicTrack>> SearchAsync(string brandKey, string? query, CancellationToken ct = default)
    {
        // Marca de demonstração não tem Instagram (nem publica): a busca só lê o catálogo, pela primeira conta configurada.
        var account = (BrandKeys.IsDemo(brandKey) ? _accounts.All.FirstOrDefault(a => a.Channel == SocialChannel.Instagram) : null)
            ?? _accounts.For(brandKey, SocialChannel.Instagram);
        var path = $"{_version}/ig_audio?audio_type=music&user_id={Uri.EscapeDataString(account.AccountId)}";
        if (!string.IsNullOrWhiteSpace(query)) path += $"&search_query={Uri.EscapeDataString(query.Trim())}";
        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Get, path, account.AccessToken, null, ct);
        if (!doc.RootElement.TryGetProperty("audio", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray()
            .Select(a => new MusicTrack(Text(a, "audio_id"), Text(a, "title"), Text(a, "display_artist"),
                a.TryGetProperty("duration_in_ms", out var d) && d.TryGetInt32(out var ms) ? ms : 0,
                Url(a, "download_url"), Url(a, "cover_artwork_thumbnail_uri") ?? Url(a, "cover_artwork_thumbnail_url")))
            .Where(t => t.Id.Length > 0)
            .ToList();
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";

    private static string? Url(JsonElement e, string name) =>
        Text(e, name) is { Length: > 0 } s && s.StartsWith("https://", StringComparison.Ordinal) ? s : null;
}

/// <summary>Modo de teste (publicação fake): duas músicas fixas, sem rede.</summary>
public sealed class FakeMusicCatalog : IMusicCatalog
{
    public Task<IReadOnlyList<MusicTrack>> SearchAsync(string brandKey, string? query, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MusicTrack>>(
        [
            new MusicTrack("587784541076604", "Birthday Wish", "Shuba", 153760, null, null),
            new MusicTrack("100200300", $"Calm {query}".Trim(), "Teste", 90000, null, null),
        ]);
}
