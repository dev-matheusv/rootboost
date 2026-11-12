using RootBoost.Application.Abstractions;
using RootBoost.Application.Organic;
using RootBoost.Application.Publishing;
using RootBoost.Application.Push;
using RootBoost.Domain;

namespace RootBoost.Application.Reports;

/// <summary>
/// Resumo da semana no celular (12/11, pedido do Matheus): segunda às 9h (Brasília), um aviso por marca com quantos
/// posts saíram e o vídeo que mais funcionou. O toque abre a Visão geral, onde está o resumo inteiro (orgânico e pago)
/// e o botão de sugestões da IA. Marca sem nada na semana não recebe aviso.
/// </summary>
public sealed class WeeklyDigest
{
    private readonly IBrandStore _brands;
    private readonly IPublicationStore _publications;
    private readonly IOrganicPostStore _organic;
    private readonly PushNotifier _push;

    public WeeklyDigest(IBrandStore brands, IPublicationStore publications, IOrganicPostStore organic, PushNotifier push)
    {
        _brands = brands;
        _publications = publications;
        _organic = organic;
        _push = push;
    }

    /// <summary>Segunda entre 9h00 e 9h14 em Brasília: a janela de 15 minutos do agendador.</summary>
    public static bool IsDue(DateTimeOffset now)
    {
        TimeSpan offset;
        try { offset = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo").GetUtcOffset(now); }
        catch (TimeZoneNotFoundException) { offset = TimeSpan.FromHours(-3); }
        var local = now.ToOffset(offset);
        return local.DayOfWeek == DayOfWeek.Monday && local.Hour == 9 && local.Minute < 15;
    }

    public static PushMessage? Compose(Brand brand, int published, RankedPost? best)
    {
        if (published == 0 && best is null) return null;
        var parts = new List<string> { published == 1 ? "1 post saiu" : $"{published} posts saíram" };
        if (best is not null) parts.Add($"melhor vídeo: \"{Clip(ContentLessons.Title(best.Post), 50)}\" (nota {best.Score})");
        return new PushMessage($"Resumo da semana: {brand.Name}", string.Join("; ", parts) + ". Toque pra ver o resumo e as sugestões.",
            "/", $"digest:{brand.Key}");
    }

    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!_push.Enabled) return 0;
        var sent = 0;
        foreach (var brand in await _brands.ListAsync(ct))
        {
            var from = now.AddDays(-7);
            var published = (await _publications.ListByBrandAsync(brand.Key, from, now, ct)).Count(p => p.Status == PublicationStatus.Published);
            var ranked = CreativeRanking.Rank(await _organic.ListByBrandAsync(brand.Key, now.AddDays(-180), ct));
            var best = ranked.Where(r => r.EnoughData && r.Post.PostedAt >= from).OrderByDescending(r => r.Score).FirstOrDefault();
            var message = Compose(brand, published, best);
            if (message is null) continue;
            await _push.SendAsync(message, ct);
            sent++;
        }
        return sent;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
