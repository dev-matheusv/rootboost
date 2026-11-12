using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RootBoost.Application.Studio;

/// <summary>Voz que existe só no PC do worker (perfil do DMaker), particular de uma marca.</summary>
public sealed record LocalVoice(string Key, string BrandKey, bool IsDefault);

/// <summary>
/// Vozes que o worker informa ter (12/11, pedido do Matheus): o seletor do "Novo vídeo" mostra essas junto com as do
/// banco, pra ninguém precisar lembrar o código. Fica em memória: o worker reenvia a cada 10 minutos, então um deploy
/// só esvazia a lista até o próximo envio. Voz do Matheus nunca entra (regra da casa: ela não sobe pra produção).
/// </summary>
public sealed class LocalVoiceRegistry
{
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, IReadOnlyList<LocalVoice>> _byWorker = new();

    public static bool IsAllowed(string key) => KeyPattern.IsMatch(key) && !key.StartsWith("matheus", StringComparison.Ordinal);

    public void Set(string workerId, IEnumerable<LocalVoice> voices) =>
        _byWorker[workerId] = voices.Where(v => IsAllowed(v.Key) && KeyPattern.IsMatch(v.BrandKey)).Take(50).ToList();

    public IReadOnlyList<LocalVoice> ForBrand(string brandKey) =>
        _byWorker.Values.SelectMany(v => v).Where(v => v.BrandKey == brandKey)
            .GroupBy(v => v.Key).Select(g => g.OrderByDescending(v => v.IsDefault).First())
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Key).ToList();
}
