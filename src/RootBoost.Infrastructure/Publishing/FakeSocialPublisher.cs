using System.Collections.Concurrent;
using RootBoost.Application.Publishing;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>
/// Publicador de mentira (dry-run), padrão enquanto Social:Live=false. Não chama rede nenhuma: só registra em
/// memória, pra o fluxo inteiro (aprovação, job, retomada) rodar em dev, nos testes e no CI sem credencial.
/// </summary>
public sealed class FakeSocialPublisher : ISocialPublisher
{
    private readonly ConcurrentDictionary<string, PublishedPost> _published = new();

    public FakeSocialPublisher(SocialChannel channel) => Channel = channel;

    public SocialChannel Channel { get; }

    /// <summary>Quantas vezes cada container foi publicado. O teste de idempotência confere que nunca passa de 1.</summary>
    public ConcurrentDictionary<string, int> PublishCount { get; } = new();

    /// <summary>Faz os próximos PublishAsync falharem (simula erro do canal).</summary>
    public int FailNextPublishes { get; set; }

    public Task<string> PrepareAsync(Publication publication, CancellationToken ct = default) =>
        Task.FromResult($"fake-container-{publication.Id:N}");

    public Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default) =>
        Task.FromResult(_published.TryGetValue(externalId, out var post) ? post : null);

    public Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        if (FailNextPublishes > 0)
        {
            FailNextPublishes--;
            throw new InvalidOperationException("erro simulado do canal");
        }
        PublishCount.AddOrUpdate(externalId, 1, (_, n) => n + 1);
        var post = new PublishedPost($"fake-post-{publication.Id:N}", null);
        _published[externalId] = post;
        return Task.FromResult(post);
    }
}
