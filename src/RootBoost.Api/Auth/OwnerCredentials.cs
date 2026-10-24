using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RootBoost.Domain;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Api.Auth;

/// <summary>
/// Senha efetiva do dono: a do banco, semeada e resetável pela variável <c>Auth__OwnerPasswordHash</c>.
/// Trocar a variável é o caminho de recuperação enquanto não houver "esqueci a senha" por e-mail (RB-26).
/// </summary>
public sealed class OwnerCredentials
{
    public const int MinLength = 12;

    private readonly RootBoostDbContext _db;
    private readonly OwnerOptions _owner;
    private readonly TimeProvider _time;

    public OwnerCredentials(RootBoostDbContext db, IOptions<OwnerOptions> owner, TimeProvider time)
    {
        _db = db;
        _owner = owner.Value;
        _time = time;
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken ct)
    {
        var credential = await EffectiveAsync(ct);
        return Verify(credential.PasswordHash, password);
    }

    public async Task<string?> ChangeAsync(string currentPassword, string newPassword, CancellationToken ct)
    {
        var credential = await EffectiveAsync(ct);
        if (!Verify(credential.PasswordHash, currentPassword)) return "a senha atual não confere";
        if (newPassword.Length < MinLength) return $"a senha nova precisa de pelo menos {MinLength} caracteres";
        if (newPassword == currentPassword) return "a senha nova precisa ser diferente da atual";

        credential.ChangePassword(new PasswordHasher<object>().HashPassword(new object(), newPassword), _time.GetUtcNow());
        await _db.SaveChangesAsync(ct);
        return null;
    }

    private async Task<ConsoleCredential> EffectiveAsync(CancellationToken ct)
    {
        var email = _owner.OwnerEmail.Trim().ToLowerInvariant();
        var credential = await _db.ConsoleCredentials.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (credential is null)
        {
            credential = new ConsoleCredential(email, _owner.OwnerPasswordHash, _time.GetUtcNow());
            _db.ConsoleCredentials.Add(credential);
            await _db.SaveChangesAsync(ct);
        }
        else if (credential.ApplyEnvReset(_owner.OwnerPasswordHash, _time.GetUtcNow()))
        {
            await _db.SaveChangesAsync(ct);
        }
        return credential;
    }

    private static bool Verify(string hash, string password)
    {
        try
        {
            return new PasswordHasher<object>().VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false; // hash malformado na variável não vira erro 500
        }
    }
}
