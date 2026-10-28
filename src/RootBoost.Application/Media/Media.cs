using RootBoost.Domain;

namespace RootBoost.Application.Media;

public sealed record StoredObject(Stream Content, long Length, string ContentType);

/// <summary>Armazenamento de objetos (bucket S3 do Railway em produção; pasta local no desenvolvimento).</summary>
public interface IMediaStorage
{
    Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default);

    /// <summary>Nulo quando o objeto não existe.</summary>
    Task<StoredObject?> GetAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

public interface IMediaAssetStore
{
    Task AddAsync(MediaAsset asset, CancellationToken ct = default);
    Task<MediaAsset?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MediaAsset>> ListByBrandAsync(string brandKey, int limit, CancellationToken ct = default);
    Task RemoveAsync(MediaAsset asset, CancellationToken ct = default);
    Task SaveAsync(MediaAsset asset, CancellationToken ct = default);
}

public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary>"S3" (produção) ou "Local" (desenvolvimento e testes).</summary>
    public string Storage { get; set; } = "Local";
    public string LocalPath { get; set; } = "media-local";
    public string Bucket { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Region { get; set; } = "auto";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";

    /// <summary>Base dos links públicos (a própria API). Ex.: https://rootboost-production.up.railway.app</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Tamanho máximo por arquivo. Padrão 300 MB (folga pra Reels).</summary>
    public long MaxBytes { get; set; } = 300L * 1024 * 1024;
}

/// <summary>
/// Sobe um arquivo: valida tipo e tamanho, grava no armazenamento e só depois registra no banco
/// (sem linha apontando pra objeto que não existe). Falha no registro apaga o objeto.
/// </summary>
public sealed class UploadMedia
{
    private readonly IMediaStorage _storage;
    private readonly IMediaAssetStore _assets;
    private readonly MediaOptions _options;
    private readonly TimeProvider _time;

    public UploadMedia(IMediaStorage storage, IMediaAssetStore assets, MediaOptions options, TimeProvider time)
    {
        _storage = storage;
        _assets = assets;
        _options = options;
        _time = time;
    }

    public async Task<MediaAsset> HandleAsync(string brandKey, string fileName, long length, Stream content, CancellationToken ct = default)
    {
        if (length > _options.MaxBytes)
            throw new ArgumentException($"arquivo maior que o limite de {_options.MaxBytes / (1024 * 1024)} MB");

        var asset = new MediaAsset(brandKey, fileName, length, _time.GetUtcNow());
        await _storage.PutAsync(asset.StorageKey, content, length, asset.ContentType, ct);
        try
        {
            await _assets.AddAsync(asset, ct);
        }
        catch
        {
            await _storage.DeleteAsync(asset.StorageKey, CancellationToken.None);
            throw;
        }
        return asset;
    }
}
