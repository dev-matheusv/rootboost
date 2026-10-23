using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Copy;
using RootBoost.Domain;

namespace RootBoost.Application.Publishing;

/// <summary>Post publicado no canal.</summary>
public sealed record PublishedPost(string ExternalId, string? Url);

/// <summary>
/// Publica num canal (Instagram, Facebook, LinkedIn). Duas etapas, como o Instagram exige: preparar (cria o
/// container e devolve o id) e publicar. Separar as duas é o que deixa o job retomar sem publicar duas vezes.
/// </summary>
public interface ISocialPublisher
{
    SocialChannel Channel { get; }

    /// <summary>Cria o container ou o upload no canal e devolve o id. Ainda não aparece pra ninguém.</summary>
    Task<string> PrepareAsync(Publication publication, CancellationToken ct = default);

    /// <summary>Se aquele container já virou post (o processo caiu depois de publicar), devolve o post.</summary>
    Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default);

    Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default);
}

public interface IPublicationStore
{
    Task AddAsync(Publication publication, CancellationToken ct = default);
    Task<Publication?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Publication>> ListByBrandAsync(string brandKey, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Aprovadas no horário e travadas em Publishing há mais de <paramref name="stuckAfter"/>.</summary>
    Task<IReadOnlyList<Publication>> ListDueAsync(DateTimeOffset now, TimeSpan stuckAfter, CancellationToken ct = default);

    Task SaveAsync(Publication publication, CancellationToken ct = default);
}

public sealed record ApprovalResult(bool Approved, string? Error, CopyCheckResult? Checklist);

/// <summary>Aprovação humana, sempre depois da checklist automática. Peça bloqueada não pode ser aprovada.</summary>
public sealed class ApprovePublication
{
    private readonly IPublicationStore _store;
    private readonly IBrandProfileStore _profiles;
    private readonly IKnowledgeBase _knowledge;
    private readonly TimeProvider _time;

    public ApprovePublication(IPublicationStore store, IBrandProfileStore profiles, IKnowledgeBase knowledge, TimeProvider time)
    {
        _store = store;
        _profiles = profiles;
        _knowledge = knowledge;
        _time = time;
    }

    public async Task<ApprovalResult> HandleAsync(string brandKey, Guid id, string approvedBy, CancellationToken ct = default)
    {
        var publication = await _store.GetAsync(id, ct);
        if (publication is null || publication.BrandKey != brandKey) return new(false, "publicação não encontrada", null);

        var profile = await _profiles.GetAsync(brandKey, ct);
        var checklist = CopyChecklist.Check(new CopyPiece(publication.Caption), _knowledge, profile);
        if (checklist.Blocked) return new(false, "a checklist bloqueou a legenda", checklist);

        try
        {
            publication.Approve(approvedBy, _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new(false, ex.Message, checklist);
        }

        await _store.SaveAsync(publication, ct);
        return new(true, null, checklist);
    }
}

public sealed record PublishRunResult(int Published, int Failed, int Retrying);

/// <summary>
/// Job que publica o que está aprovado e no horário. Pode rodar de novo a qualquer momento, inclusive depois de
/// o processo cair no meio: grava o id do container antes de publicar e, antes de repetir, pergunta ao canal se
/// aquilo já foi publicado.
/// </summary>
public sealed class PublishDuePublications
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

    private readonly IPublicationStore _store;
    private readonly IReadOnlyDictionary<SocialChannel, ISocialPublisher> _publishers;
    private readonly TimeProvider _time;
    private readonly ILogger<PublishDuePublications> _log;

    public PublishDuePublications(IPublicationStore store, IEnumerable<ISocialPublisher> publishers, TimeProvider time,
        ILogger<PublishDuePublications> log)
    {
        _store = store;
        _publishers = publishers.ToDictionary(p => p.Channel);
        _time = time;
        _log = log;
    }

    public async Task<PublishRunResult> RunAsync(CancellationToken ct = default)
    {
        int published = 0, failed = 0, retrying = 0;
        foreach (var publication in await _store.ListDueAsync(_time.GetUtcNow(), StuckAfter, ct))
        {
            var outcome = await PublishOneAsync(publication, ct);
            if (outcome == PublicationStatus.Published) published++;
            else if (outcome == PublicationStatus.Failed) failed++;
            else retrying++;
        }
        return new(published, failed, retrying);
    }

    private async Task<PublicationStatus> PublishOneAsync(Publication publication, CancellationToken ct)
    {
        if (!_publishers.TryGetValue(publication.Channel, out var publisher))
        {
            publication.StartPublishing(_time.GetUtcNow());
            publication.MarkFailed($"nenhum publicador configurado pra {publication.Channel}", MaxAttempts, _time.GetUtcNow());
            await _store.SaveAsync(publication, ct);
            return publication.Status;
        }

        try
        {
            publication.StartPublishing(_time.GetUtcNow());
            await _store.SaveAsync(publication, ct);

            // Retomada: se já existe container, o post pode já estar no ar. Nunca publicar duas vezes.
            if (publication.ExternalId is { } existing &&
                await publisher.FindPublishedAsync(publication, existing, ct) is { } alreadyLive)
            {
                publication.MarkPublished(alreadyLive.ExternalId, alreadyLive.Url, _time.GetUtcNow());
                await _store.SaveAsync(publication, ct);
                return publication.Status;
            }

            if (publication.ExternalId is null)
            {
                var containerId = await publisher.PrepareAsync(publication, ct);
                publication.RecordExternalId(containerId, _time.GetUtcNow());
                await _store.SaveAsync(publication, ct); // grava ANTES de publicar
            }

            var post = await publisher.PublishAsync(publication, publication.ExternalId!, ct);
            publication.MarkPublished(post.ExternalId, post.Url, _time.GetUtcNow());
            await _store.SaveAsync(publication, ct);
            return publication.Status;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao publicar {PublicationId} no {Channel}.", publication.Id, publication.Channel);
            publication.MarkFailed(ex.Message, MaxAttempts, _time.GetUtcNow());
            await _store.SaveAsync(publication, ct);
            return publication.Status;
        }
    }
}
