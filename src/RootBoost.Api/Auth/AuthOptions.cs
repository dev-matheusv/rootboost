namespace RootBoost.Api.Auth;

/// <summary>Assinatura do token do Console. Mesmo molde do RootFlow (`Jwt__Key` com 32+ caracteres).</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "RootBoost";
    public string Audience { get; set; } = "RootBoost.Console";
    public string Key { get; set; } = string.Empty;
    public int ExpiresInMinutes { get; set; } = 480;
}

/// <summary>
/// Dono do Console. Enquanto o RootBoost tem um operador só, a credencial vem da configuração
/// (`Auth__OwnerEmail`, `Auth__OwnerPasswordHash`); a tabela de usuários entra junto com o Postgres
/// e os workspaces. O hash é gerado com `dotnet run --project src/RootBoost.Api --no-launch-profile -- hash-password`.
/// </summary>
public sealed class OwnerOptions
{
    public const string Section = "Auth";

    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerName { get; set; } = "Owner";
    public string OwnerPasswordHash { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(OwnerEmail) && !string.IsNullOrWhiteSpace(OwnerPasswordHash);
}
