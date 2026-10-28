using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Alerts;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Publishing;

public sealed record MetaConnectionResult(string BrandKey, SocialChannel Channel, string AccountId, bool Ok, string? Error);

/// <summary>
/// Saúde da conexão com a Meta (RB-58): lê o próprio nó de cada conta configurada (<c>GET /{id}?fields=id</c>, a
/// leitura mais simples que existe pro usuário do Instagram e pra Página) com o token dela. Falhou (token vencido,
/// app bloqueado como em 25/10, permissão retirada) abre aviso; voltou, fecha.
/// </summary>
public sealed class MetaConnectionCheck
{
    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly AlertService _alerts;
    private readonly string _version;

    public MetaConnectionCheck(IHttpClientFactory factory, SocialAccounts accounts, AlertService alerts, IOptions<MetaOptions> meta)
    {
        _factory = factory;
        _accounts = accounts;
        _alerts = alerts;
        _version = meta.Value.ApiVersion;
    }

    public async Task<IReadOnlyList<MetaConnectionResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<MetaConnectionResult>();
        foreach (var account in _accounts.All.Where(a => a.Channel is SocialChannel.Instagram or SocialChannel.Facebook))
        {
            var key = AlertService.MetaKey(account.BrandKey, account.Channel);
            try
            {
                using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Get,
                    $"{_version}/{account.AccountId}?fields=id", account.AccessToken, null, ct);
                if (doc.Str("id") != account.AccountId)
                    throw new InvalidOperationException("a Meta respondeu sem o id da conta");
                results.Add(new(account.BrandKey, account.Channel, account.AccountId, true, null));
                await _alerts.ResolveAsync(key, ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                results.Add(new(account.BrandKey, account.Channel, account.AccountId, false, ex.Message));
                await _alerts.RaiseAsync(AlertKind.MetaConnection, key, account.BrandKey,
                    $"Conexão do {account.Channel} ({account.BrandKey}) com a Meta falhou: {ex.Message}. " +
                    "Confira o painel do app e a Central de Suporte Empresarial; token vencido precisa ser trocado no Railway.", ct);
            }
        }
        return results;
    }
}

/// <summary>Roda a checagem da Meta 5 minutos depois de subir e depois uma vez por dia.</summary>
public sealed class MetaConnectionScheduler : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MetaConnectionScheduler> _log;

    public MetaConnectionScheduler(IServiceScopeFactory scopes, ILogger<MetaConnectionScheduler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRun, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var results = await scope.ServiceProvider.GetRequiredService<MetaConnectionCheck>().RunAsync(stoppingToken);
                    _log.LogInformation("Conexão Meta: {Ok} de {Total} contas ok.", results.Count(r => r.Ok), results.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Checagem da conexão Meta falhou.");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }
}
