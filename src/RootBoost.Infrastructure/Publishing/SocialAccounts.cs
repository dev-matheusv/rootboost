using Microsoft.Extensions.Configuration;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>Conta de uma marca num canal: o id no canal (usuário do Instagram, Página do Facebook) e o token.</summary>
public sealed record SocialAccount(string BrandKey, SocialChannel Channel, string AccountId, string AccessToken);

/// <summary>
/// Contas vindas da config, nunca do navegador: <c>Social__Accounts__0__Brand=medlycare</c>,
/// <c>Social__Accounts__0__Channel=Instagram</c>, <c>Social__Accounts__0__AccountId=1784...</c>,
/// <c>Social__Accounts__0__AccessToken=...</c> (token só no Railway).
/// </summary>
public sealed class SocialAccounts
{
    public SocialAccounts(IConfiguration config)
    {
        All = config.GetSection("Social:Accounts").GetChildren().Select(s =>
        {
            var key = $"Social:Accounts:{s.Key}";
            if (!Enum.TryParse<SocialChannel>(s["Channel"], true, out var channel))
                throw new InvalidOperationException($"{key}:Channel inválido: '{s["Channel"]}'");
            return new SocialAccount(
                BrandKeys.Validate(s["Brand"] ?? throw new InvalidOperationException($"{key}:Brand vazio")),
                channel,
                s["AccountId"] is { Length: > 0 } id ? id.Trim() : throw new InvalidOperationException($"{key}:AccountId vazio"),
                s["AccessToken"] is { Length: > 0 } token ? token.Trim() : throw new InvalidOperationException($"{key}:AccessToken vazio"));
        }).ToList();
    }

    public IReadOnlyList<SocialAccount> All { get; }

    public SocialAccount For(string brandKey, SocialChannel channel) =>
        All.FirstOrDefault(a => a.BrandKey == brandKey && a.Channel == channel)
        ?? throw new InvalidOperationException($"a marca {brandKey} não tem conta de {channel} configurada");
}
