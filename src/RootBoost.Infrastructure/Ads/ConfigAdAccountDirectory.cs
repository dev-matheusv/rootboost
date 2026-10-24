using Microsoft.Extensions.Configuration;
using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Ads;

/// <summary>
/// Contas de anúncios vindas da config: <c>Ads__Accounts__0__Brand=medlycare</c>,
/// <c>Ads__Accounts__0__Platform=meta</c>, <c>Ads__Accounts__0__AccountId=act_123</c>.
/// Com o leitor Fake e nenhuma conta configurada, usa uma conta de mentira do MedlyCare.
/// </summary>
public sealed class ConfigAdAccountDirectory : IAdAccountDirectory
{
    public ConfigAdAccountDirectory(IConfiguration config, bool fakeReader)
    {
        var accounts = config.GetSection("Ads:Accounts").GetChildren()
            .Select(s => new AdAccount(
                BrandKeys.Validate(s["Brand"] ?? throw new InvalidOperationException($"Ads:Accounts:{s.Key}:Brand vazio")),
                (s["Platform"] ?? "meta").Trim().ToLowerInvariant(),
                s["AccountId"] is { Length: > 0 } id ? id.Trim() : throw new InvalidOperationException($"Ads:Accounts:{s.Key}:AccountId vazio"),
                string.IsNullOrWhiteSpace(s["ConversionAction"]) ? null : s["ConversionAction"]!.Trim()))
            .ToList();

        if (accounts.Count == 0 && fakeReader)
            accounts.Add(new AdAccount(BrandKeys.MedlyCare, FakeAdInsightsReader.FakePlatform, "act_fake_medlycare"));

        All = accounts;
    }

    public IReadOnlyList<AdAccount> All { get; }
}
