using Amazon.S3;
using Amazon.S3.Model;
using RootBoost.Application.Media;

namespace RootBoost.Infrastructure.Media;

/// <summary>
/// Bucket do Railway (S3 compatível, Tigris). Endpoint e credenciais vêm das variáveis do bucket
/// (Media__Endpoint, Media__Bucket, Media__AccessKeyId, Media__SecretAccessKey). URL no estilo virtual-hosted,
/// o padrão do SDK (conferido na doc do Railway em 27/10/2026).
/// </summary>
public sealed class S3MediaStorage : IMediaStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;

    public S3MediaStorage(MediaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket) || string.IsNullOrWhiteSpace(options.Endpoint) ||
            string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey))
            throw new InvalidOperationException("Media:Storage=S3 precisa de Media:Bucket, Endpoint, AccessKeyId e SecretAccessKey.");

        _bucket = options.Bucket;
        _s3 = new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "auto" : options.Region,
            ForcePathStyle = false,
        });
    }

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        request.Headers.ContentLength = length;
        await _s3.PutObjectAsync(request, ct);
    }

    public async Task<StoredObject?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, key, ct);
            return new StoredObject(response.ResponseStream, response.ContentLength, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _s3.DeleteObjectAsync(_bucket, key, ct);

    public void Dispose() => _s3.Dispose();
}

/// <summary>Pasta local (desenvolvimento e testes). Mesmo contrato do bucket.</summary>
public sealed class LocalMediaStorage : IMediaStorage
{
    private readonly string _root;

    public LocalMediaStorage(MediaOptions options) =>
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.LocalPath) ? "media-local" : options.LocalPath);

    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("chave inválida", nameof(key));
        return full;
    }

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken ct = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task<StoredObject?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return Task.FromResult<StoredObject?>(null);
        var stream = File.OpenRead(path);
        var type = RootBoost.Domain.MediaAsset.AllowedTypes.TryGetValue(Path.GetExtension(path), out var t) ? t : "application/octet-stream";
        return Task.FromResult<StoredObject?>(new StoredObject(stream, stream.Length, type));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
