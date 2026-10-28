namespace RootBoost.Domain;

public enum AlertKind
{
    /// <summary>Post esgotou as tentativas e ficou Failed.</summary>
    PublicationFailed,

    /// <summary>Sincronização de métricas de uma conta de anúncio falhou.</summary>
    AdsSyncFailed,

    /// <summary>Checagem diária: a conta da Meta (Instagram, Página) não responde com o token configurado.</summary>
    MetaConnection
}

/// <summary>
/// Aviso de operação pro humano (RB-58). Um aviso aberto por <see cref="Key"/>: o mesmo problema repetido só atualiza
/// a contagem e a última vez visto. Fecha sozinho quando o problema some, ou à mão no Console (dispensar).
/// </summary>
public sealed class OpsAlert
{
    public Guid Id { get; private set; }
    public AlertKind Kind { get; private set; }

    /// <summary>Identifica o problema, ex.: <c>pub:{id}</c>, <c>ads:meta:act_1</c>, <c>meta:medlycare:Instagram</c>.</summary>
    public string Key { get; private set; } = default!;

    public string? BrandKey { get; private set; }
    public string Message { get; private set; } = default!;
    public int Count { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>"auto" quando o problema sumiu sozinho; e-mail de quem dispensou no Console.</summary>
    public string? ResolvedBy { get; private set; }

    private OpsAlert() { }

    public OpsAlert(AlertKind kind, string key, string? brandKey, string message, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("chave do aviso vazia", nameof(key));
        Id = Guid.NewGuid();
        Kind = kind;
        Key = key.Trim();
        BrandKey = brandKey;
        Message = Trim(message);
        Count = 1;
        FirstSeenAt = now;
        LastSeenAt = now;
    }

    public bool IsOpen => ResolvedAt is null;

    public void SeenAgain(string message, DateTimeOffset now)
    {
        Message = Trim(message);
        Count++;
        LastSeenAt = now;
    }

    public void Resolve(string by, DateTimeOffset now)
    {
        if (!IsOpen) return;
        ResolvedAt = now;
        ResolvedBy = string.IsNullOrWhiteSpace(by) ? "auto" : by.Trim();
    }

    private static string Trim(string message)
    {
        var m = string.IsNullOrWhiteSpace(message) ? "sem detalhe" : message.Trim();
        return m.Length > 1000 ? m[..1000] : m;
    }
}
