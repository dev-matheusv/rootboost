namespace RootBoost.Domain;

/// <summary>
/// Senha do dono do Console, guardada no banco (só o hash). A variável <c>Auth__OwnerPasswordHash</c> é a
/// semente e o reset de emergência: quando ela muda, substitui a senha do banco. Enquanto não muda, vale a
/// senha trocada pelo Console.
/// </summary>
public sealed class ConsoleCredential
{
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;

    /// <summary>Hash da variável que originou ou resetou esta senha. Diferente da variável atual = reset pedido.</summary>
    public string SourceEnvHash { get; private set; } = default!;

    public DateTimeOffset UpdatedAt { get; private set; }

    private ConsoleCredential() { }

    public ConsoleCredential(string email, string envHash, DateTimeOffset now)
    {
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = envHash;
        SourceEnvHash = envHash;
        UpdatedAt = now;
    }

    public bool ApplyEnvReset(string envHash, DateTimeOffset now)
    {
        if (envHash == SourceEnvHash) return false;
        PasswordHash = envHash;
        SourceEnvHash = envHash;
        UpdatedAt = now;
        return true;
    }

    public void ChangePassword(string newHash, DateTimeOffset now)
    {
        PasswordHash = newHash;
        UpdatedAt = now;
    }
}
