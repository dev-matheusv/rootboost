using Microsoft.Extensions.Options;
using RootBoost.Application.Publishing;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>
/// Publica foto na Página do Facebook. Conferido na referência "Page Photos" em 24/10/2026:
/// <c>POST /{page-id}/photos</c> com <c>url</c> e <c>caption</c> (o <c>message</c> está depreciado) → <c>id</c> e <c>post_id</c>;
/// link do post = https://www.facebook.com/{post_id}. Token de Página com <c>pages_manage_posts</c>.
/// Vídeo na Página exige upload em partes (Resumable Upload API) e fica pra depois: falha com mensagem clara.
/// </summary>
public sealed class FacebookPagePublisher : ISocialPublisher
{
    private readonly IHttpClientFactory _factory;
    private readonly SocialAccounts _accounts;
    private readonly string _version;

    public FacebookPagePublisher(IHttpClientFactory factory, SocialAccounts accounts, IOptions<MetaOptions> meta)
    {
        _factory = factory;
        _accounts = accounts;
        _version = meta.Value.ApiVersion;
    }

    public SocialChannel Channel => SocialChannel.Facebook;

    /// <summary>Foto na Página é uma chamada só, sem container: aqui só valida e devolve uma referência local.</summary>
    public Task<string> PrepareAsync(Publication publication, CancellationToken ct = default)
    {
        _accounts.For(publication.BrandKey, Channel);
        if (InstagramPublisher.IsVideo(publication.MediaUrl))
            throw new InvalidOperationException("vídeo na Página do Facebook ainda não é suportado (precisa de upload em partes); use imagem ou publique à mão");
        return Task.FromResult($"fb-pending-{publication.Id:N}");
    }

    /// <summary>
    /// Sem container, a API não diz se uma tentativa anterior chegou a publicar. Limite conhecido: se o processo
    /// cair entre o post e a gravação, o job pode repetir. Por isso o publish e o MarkPublished ficam colados.
    /// </summary>
    public Task<PublishedPost?> FindPublishedAsync(Publication publication, string externalId, CancellationToken ct = default) =>
        Task.FromResult<PublishedPost?>(null);

    public async Task<PublishedPost> PublishAsync(Publication publication, string externalId, CancellationToken ct = default)
    {
        var account = _accounts.For(publication.BrandKey, Channel);
        using var doc = await GraphApi.SendAsync(_factory.CreateClient("meta"), HttpMethod.Post,
            $"{_version}/{account.AccountId}/photos", account.AccessToken,
            new Dictionary<string, string> { ["url"] = publication.MediaUrl, ["caption"] = publication.Caption }, ct);

        var postId = doc.Str("post_id");
        var id = postId.Length > 0 ? postId : doc.Str("id");
        if (id.Length == 0) throw new InvalidOperationException("Facebook não devolveu o id do post");
        return new PublishedPost(id, postId.Length > 0 ? $"https://www.facebook.com/{postId}" : null);
    }
}
