using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Publishing;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class InMemoryPublicationStore : IPublicationStore
{
    public List<Publication> Items { get; } = new();
    public int Saves { get; private set; }

    public Task AddAsync(Publication publication, CancellationToken ct = default) { Items.Add(publication); return Task.CompletedTask; }
    public Task<Publication?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
    public Task<IReadOnlyList<Publication>> ListByBrandAsync(string brandKey, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Publication>>(Items.Where(p => p.BrandKey == brandKey).ToList());
    public Task<IReadOnlyList<Publication>> ListDraftsToRemindAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Publication>>(Items.Where(p => p.Status == PublicationStatus.Draft && p.ReminderSentAt == null
            && p.ScheduledFor >= from && p.ScheduledFor <= to).OrderBy(p => p.ScheduledFor).ToList());
    public Task<IReadOnlyList<Publication>> ListDueAsync(DateTimeOffset now, TimeSpan stuckAfter, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Publication>>(Items.Where(p => p.IsDue(now) || p.IsStuck(now, stuckAfter)).ToList());
    public Task SaveAsync(Publication publication, CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
    public Task RemoveAsync(Publication publication, CancellationToken ct = default) { Items.Remove(publication); return Task.CompletedTask; }
}

/// <summary>Publicador de teste: conta publicações por container e pode falhar ou "cair" depois de publicar.</summary>
public sealed class TestPublisher : ISocialPublisher
{
    private readonly Dictionary<string, PublishedPost> _live = new();
    public SocialChannel Channel => SocialChannel.Instagram;
    public Dictionary<string, int> PublishCount { get; } = new();
    public int FailNext { get; set; }
    public int Prepares { get; private set; }

    public Task<string> PrepareAsync(Publication publication, CancellationToken ct = default)
    {
        Prepares++;
        return Task.FromResult($"container-{publication.Id:N}");
    }

    public Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default) =>
        Task.FromResult(_live.TryGetValue(externalId, out var post) ? post : null);

    public Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        if (FailNext > 0) { FailNext--; throw new InvalidOperationException("canal fora"); }
        PublishCount[externalId] = PublishCount.GetValueOrDefault(externalId) + 1;
        return Task.FromResult(_live[externalId] = new PublishedPost($"post-{publication.Id:N}", "https://instagram.com/p/x"));
    }
}

public class PublishingTests
{
    private readonly ManualTime _time = new();
    private readonly InMemoryPublicationStore _store = new();
    private readonly TestPublisher _publisher = new();

    private PublishDuePublications Job() =>
        new(_store, new[] { _publisher }, _time, NullLogger<PublishDuePublications>.Instance);

    private Publication Approved(TimeSpan inFuture)
    {
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg",
            "Agenda para fisioterapia", _time.Now + inFuture, _time.Now);
        p.Approve("dono@exemplo.com", _time.Now);
        _store.Items.Add(p);
        return p;
    }

    [Fact]
    public void Failed_post_can_be_retried_keeping_approval_and_channel_id()
    {
        var p = Approved(TimeSpan.FromMinutes(-1));
        for (var i = 0; i < 3; i++)
        {
            p.StartPublishing(_time.Now);
            if (i == 0) p.RecordExternalId("vid-7", _time.Now);
            p.MarkFailed("processando", 3, _time.Now);
        }
        Assert.Equal(PublicationStatus.Failed, p.Status);
        p.Retry(_time.Now);
        Assert.Equal(PublicationStatus.Approved, p.Status);
        Assert.Equal("vid-7", p.ExternalId);
        Assert.Equal(0, p.Attempts);
        Assert.Throws<InvalidOperationException>(() => p.Retry(_time.Now));
    }

    [Fact]
    public async Task Demo_brand_never_publishes_even_approved_and_due()
    {
        var p = new Publication(BrandKeys.Demo, SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg", "Demo", _time.Now, _time.Now);
        p.Approve("dono@exemplo.com", _time.Now);
        _store.Items.Add(p);
        var result = await Job().RunAsync();
        Assert.Equal(0, result.Published);
        Assert.Equal(PublicationStatus.Approved, p.Status);
    }

    [Fact]
    public void Unapprove_returns_to_draft_only_before_publishing()
    {
        var p = Approved(TimeSpan.FromHours(1));
        p.Unapprove(_time.Now);
        Assert.Equal(PublicationStatus.Draft, p.Status);
        Assert.Null(p.ApprovedBy);
        Assert.Throws<InvalidOperationException>(() => p.Unapprove(_time.Now));
    }

    [Fact]
    public void Editing_the_caption_after_approval_sends_it_back_to_draft()
    {
        var p = Approved(TimeSpan.FromHours(1));
        p.EditCaption("Agenda nova para fisioterapia", _time.Now);
        Assert.Equal(PublicationStatus.Draft, p.Status);
        Assert.Null(p.ApprovedBy);
        Assert.Equal(2, p.CaptionVersion);
    }

    [Fact]
    public void Media_must_be_public_https()
    {
        Assert.Throws<ArgumentException>(() => new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram,
            "http://cdn.exemplo.com/a.jpg", "legenda", _time.Now, _time.Now));
    }

    [Fact]
    public async Task Draft_is_never_published()
    {
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg", "legenda", _time.Now, _time.Now);
        _store.Items.Add(p);
        var result = await Job().RunAsync();
        Assert.Equal(0, result.Published);
        Assert.Equal(PublicationStatus.Draft, p.Status);
        Assert.Empty(_publisher.PublishCount);
    }

    [Fact]
    public async Task Only_due_publications_are_published()
    {
        var due = Approved(TimeSpan.Zero);
        var later = Approved(TimeSpan.FromHours(2));

        var result = await Job().RunAsync();

        Assert.Equal(1, result.Published);
        Assert.Equal(PublicationStatus.Published, due.Status);
        Assert.Equal($"post-{due.Id:N}", due.ExternalId);
        Assert.Equal(PublicationStatus.Approved, later.Status);
    }

    [Fact]
    public async Task Failures_retry_until_the_limit_then_stay_failed()
    {
        var p = Approved(TimeSpan.Zero);
        _publisher.FailNext = 10;

        for (var i = 0; i < PublishDuePublications.MaxAttempts; i++) await Job().RunAsync();

        Assert.Equal(PublicationStatus.Failed, p.Status);
        Assert.Equal(PublishDuePublications.MaxAttempts, p.Attempts);
        Assert.Equal("canal fora", p.Error);
        Assert.Equal(0, (await Job().RunAsync()).Published + (await Job().RunAsync()).Retrying); // fora da fila
    }

    [Fact]
    public async Task Container_is_prepared_once_across_retries()
    {
        var p = Approved(TimeSpan.Zero);
        _publisher.FailNext = 1;

        await Job().RunAsync(); // prepara, falha no publish
        await Job().RunAsync(); // reaproveita o container

        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.Equal(1, _publisher.Prepares);
    }

    [Fact]
    public async Task Crash_after_publishing_is_resumed_without_publishing_twice()
    {
        var p = Approved(TimeSpan.Zero);
        // Simula o processo caindo logo depois do canal publicar: o post está no ar, mas o banco ficou em Publishing.
        p.StartPublishing(_time.Now);
        var container = await _publisher.PrepareAsync(p);
        p.RecordExternalId(container, _time.Now);
        await _publisher.PublishAsync(p, container);

        Assert.Equal(0, (await Job().RunAsync()).Published); // ainda não está "travada"

        _time.Now += PublishDuePublications.StuckAfter;
        var result = await Job().RunAsync();

        Assert.Equal(1, result.Published);
        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.Equal(1, _publisher.PublishCount[container]);
    }

    [Fact]
    public async Task Stuck_before_publishing_is_resumed_and_published_once()
    {
        var p = Approved(TimeSpan.Zero);
        p.StartPublishing(_time.Now); // caiu antes de preparar
        _time.Now += PublishDuePublications.StuckAfter;

        await Job().RunAsync();

        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.Equal(1, _publisher.PublishCount.Values.Single());
    }

    [Fact]
    public async Task Channel_without_publisher_is_manual_never_attempted_and_marked_by_hand()
    {
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.LinkedIn, "https://cdn.exemplo.com/a.mp4", "legenda", _time.Now, _time.Now);
        p.Approve("dono@exemplo.com", _time.Now);
        _store.Items.Add(p);

        var result = await Job().RunAsync();

        Assert.Equal(0, result.Published + result.Failed + result.Retrying);
        Assert.Equal(PublicationStatus.Approved, p.Status);
        Assert.Equal(0, p.Attempts);

        p.MarkPublishedManually("https://www.linkedin.com/feed/update/123", _time.Now);
        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.Equal("https://www.linkedin.com/feed/update/123", p.PublishedUrl);
    }

    [Fact]
    public void Only_an_approved_post_can_be_marked_as_published_by_hand()
    {
        var draft = new Publication(BrandKeys.MedlyCare, SocialChannel.LinkedIn, "https://cdn.exemplo.com/a.mp4", "legenda", _time.Now, _time.Now);
        Assert.Throws<InvalidOperationException>(() => draft.MarkPublishedManually(null, _time.Now));
    }

    [Fact]
    public async Task Editing_after_a_failed_attempt_discards_the_old_container()
    {
        var p = Approved(TimeSpan.Zero);
        _publisher.FailNext = 1;
        await Job().RunAsync();                         // prepara o container com a legenda antiga e falha
        Assert.NotNull(p.ExternalId);

        p.EditCaption("Legenda corrigida para fisioterapia", _time.Now);
        Assert.Null(p.ExternalId);                      // container antigo descartado
        Assert.Equal(0, p.Attempts);
        p.Approve("dono@exemplo.com", _time.Now);
        await Job().RunAsync();

        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.Equal(2, _publisher.Prepares);           // container novo, com a legenda nova
    }

    [Fact]
    public void Changing_media_needs_https_and_a_new_approval()
    {
        var p = Approved(TimeSpan.FromHours(1));
        Assert.Throws<ArgumentException>(() => p.ChangeMedia("http://cdn.exemplo.com/b.mp4", _time.Now));
        p.ChangeMedia("https://cdn.exemplo.com/b.mp4", _time.Now);
        Assert.Equal(PublicationStatus.Draft, p.Status);
        Assert.Equal("https://cdn.exemplo.com/b.mp4", p.MediaUrl);
        Assert.True(p.CanDelete);
    }

    [Fact]
    public void Reel_cover_must_be_https_jpeg_and_changing_it_needs_a_new_approval()
    {
        var p = Approved(TimeSpan.FromHours(1));
        Assert.True(p.ShareToFeed); // padrão: aparece também no feed
        Assert.Throws<ArgumentException>(() => p.SetReelOptions("https://cdn.exemplo.com/capa.png", null, true, _time.Now));
        Assert.Throws<ArgumentException>(() => p.SetReelOptions("http://cdn.exemplo.com/capa.jpg", null, true, _time.Now));
        Assert.Throws<ArgumentException>(() => p.SetReelOptions(null, -1, true, _time.Now));
        Assert.Equal(PublicationStatus.Approved, p.Status);

        p.SetReelOptions("https://cdn.exemplo.com/capa.jpg", null, false, _time.Now);
        Assert.Equal(PublicationStatus.Draft, p.Status);
        Assert.Equal("https://cdn.exemplo.com/capa.jpg", p.CoverUrl);
        Assert.False(p.ShareToFeed);

        p.Approve("dono@exemplo.com", _time.Now);
        p.SetReelOptions("https://cdn.exemplo.com/capa.jpg", null, false, _time.Now); // nada mudou
        Assert.Equal(PublicationStatus.Approved, p.Status);
    }
}
