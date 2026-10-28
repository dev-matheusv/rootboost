namespace RootBoost.Domain;

/// <summary>
/// Aparelho que aceitou receber avisos do Console (push da PWA, RB-37). O endereço e as chaves vêm do navegador
/// (PushManager.subscribe); o mesmo endereço inscrito de novo só atualiza as chaves.
/// </summary>
public sealed class PushDevice
{
    public Guid Id { get; private set; }
    public string Endpoint { get; private set; } = default!;
    public string P256dh { get; private set; } = default!;
    public string Auth { get; private set; } = default!;
    public string UserEmail { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSentAt { get; private set; }
    public int Failures { get; private set; }

    private PushDevice() { }

    public PushDevice(string endpoint, string p256dh, string auth, string userEmail, DateTimeOffset now)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || endpoint.Length > 1000)
            throw new ArgumentException("endereço de push inválido", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(p256dh) || p256dh.Length > 200 || string.IsNullOrWhiteSpace(auth) || auth.Length > 100)
            throw new ArgumentException("chaves de push inválidas");
        Id = Guid.NewGuid();
        Endpoint = endpoint;
        P256dh = p256dh.Trim();
        Auth = auth.Trim();
        UserEmail = string.IsNullOrWhiteSpace(userEmail) ? "desconhecido" : userEmail.Trim();
        CreatedAt = now;
    }

    public void Renew(string p256dh, string auth, string userEmail)
    {
        P256dh = p256dh.Trim();
        Auth = auth.Trim();
        if (!string.IsNullOrWhiteSpace(userEmail)) UserEmail = userEmail.Trim();
        Failures = 0;
    }

    public void Sent(DateTimeOffset now)
    {
        LastSentAt = now;
        Failures = 0;
    }

    public void Failed() => Failures++;
}
