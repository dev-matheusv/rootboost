using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RootBoost.Api.Auth;

/// <summary>
/// Autentica scripts e ferramentas pelo header `X-Api-Key` (valor em `Orders__ApiKey`).
/// Sem chave configurada o admin fica aberto, como era antes: é o modo de desenvolvimento e o
/// que os testes de integração usam. Em produção a chave está setada e o aviso no boot denuncia
/// se não estiver.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly IConfiguration _config;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var required = _config["Orders:ApiKey"];
        if (string.IsNullOrWhiteSpace(required))
            return Task.FromResult(Success("dev-open"));

        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrEmpty(provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Comparação em tempo constante: tempo de resposta não pode vazar quantos caracteres batem.
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()),
            Encoding.UTF8.GetBytes(required));

        return Task.FromResult(matches ? Success("api-key") : AuthenticateResult.Fail("Invalid API key."));
    }

    private AuthenticateResult Success(string name)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, AuthSetup.AdminRole)], Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }
}
