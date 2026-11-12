namespace RootBoost.Domain;

/// <summary>
/// Um post orgânico de uma marca num canal (RB-25), com os números acumulados desde a publicação, como a plataforma
/// devolve. Uma linha por (canal, id no canal): reler atualiza, nunca duplica. Vale pra post feito pela plataforma e
/// pra post feito à mão no app do canal; quando saiu daqui, <see cref="PublicationId"/> liga ao nosso registro.
/// </summary>
public sealed class OrganicPost
{
    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public SocialChannel Channel { get; private set; }
    public string ExternalId { get; private set; } = default!;
    /// <summary>REELS, FEED, STORY (Instagram, campo <c>media_product_type</c>).</summary>
    public string ProductType { get; private set; } = default!;
    /// <summary>VIDEO, IMAGE, CAROUSEL_ALBUM (Instagram, campo <c>media_type</c>).</summary>
    public string MediaType { get; private set; } = default!;
    public string Caption { get; private set; } = default!;
    public string? Permalink { get; private set; }
    public string? ThumbnailUrl { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }
    public Guid? PublicationId { get; private set; }

    public long Views { get; private set; }
    public long Reach { get; private set; }
    public long Likes { get; private set; }
    public long Comments { get; private set; }
    public long Shares { get; private set; }
    public long Saves { get; private set; }
    public long Interactions { get; private set; }
    /// <summary>Tempo médio assistido em milissegundos (só Reels). Nulo quando o canal não devolve.</summary>
    public long? AvgWatchMs { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }

    private OrganicPost() { }

    public OrganicPost(string brandKey, SocialChannel channel, string externalId, DateTimeOffset postedAt)
    {
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("id do post obrigatório", nameof(externalId));
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Channel = channel;
        ExternalId = externalId.Trim();
        PostedAt = postedAt;
        ProductType = string.Empty;
        MediaType = string.Empty;
        Caption = string.Empty;
    }

    public bool IsVideo => MediaType == "VIDEO" || ProductType == "REELS";

    public void Describe(string productType, string mediaType, string? caption, string? permalink, string? thumbnailUrl)
    {
        ProductType = (productType ?? string.Empty).Trim().ToUpperInvariant();
        MediaType = (mediaType ?? string.Empty).Trim().ToUpperInvariant();
        Caption = caption?.Trim() ?? string.Empty;
        if (Caption.Length > 2200) Caption = Caption[..2200];
        Permalink = permalink;
        ThumbnailUrl = thumbnailUrl;
    }

    public void LinkPublication(Guid? publicationId) => PublicationId = publicationId;

    /// <summary>Grava os números. Negativo é dado corrompido: recusa em vez de ranquear errado.</summary>
    public void SetMetrics(long views, long reach, long likes, long comments, long shares, long saves, long interactions,
        long? avgWatchMs, DateTimeOffset fetchedAt)
    {
        if (views < 0 || reach < 0 || likes < 0 || comments < 0 || shares < 0 || saves < 0 || interactions < 0 || avgWatchMs < 0)
            throw new ArgumentOutOfRangeException(nameof(views), "métrica negativa");
        Views = views;
        Reach = reach;
        Likes = likes;
        Comments = comments;
        Shares = shares;
        Saves = saves;
        Interactions = interactions;
        AvgWatchMs = avgWatchMs;
        FetchedAt = fetchedAt;
    }
}
