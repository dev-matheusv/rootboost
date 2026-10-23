using System.Text.RegularExpressions;

namespace RootBoost.Domain;

/// <summary>
/// Marca atendida pela plataforma (a loja, o MedlyCare, depois clientes). Pedidos, produtos, campanhas e
/// criativos pertencem a uma marca. Voz, inventário de prova e acervo entram aqui depois (RB-15, RB-16).
/// </summary>
public sealed class Brand
{
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string DefaultLanguage { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private Brand() { }

    public Brand(string key, string name, string defaultLanguage, DateTimeOffset createdAt)
    {
        Key = BrandKeys.Validate(key);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name required", nameof(name));
        Name = name.Trim();
        DefaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage) ? "pt-BR" : defaultLanguage.Trim();
        CreatedAt = createdAt;
    }
}

public static partial class BrandKeys
{
    /// <summary>A loja de dropshipping (PrepMate, StandTall...). Marca padrão de tudo que veio antes das marcas.</summary>
    public const string Store = "rootboost-loja";

    /// <summary>Cliente zero da plataforma de marketing.</summary>
    public const string MedlyCare = "medlycare";

    /// <summary>Chave em minúsculas, números e hífen (vai em URL, filtro e config).</summary>
    public static string Validate(string key)
    {
        var normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(normalized))
            throw new ArgumentException($"brand key inválida: '{key}' (use a-z, 0-9 e hífen, até 64)", nameof(key));
        return normalized;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex KeyPattern();
}
