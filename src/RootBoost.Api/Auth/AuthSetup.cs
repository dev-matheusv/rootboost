using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RootBoost.Api.Auth;

/// <summary>
/// Login do Console (JWT) e acesso de scripts (API key) sob uma política só, `Admin`.
/// Substitui a checagem de `X-Api-Key` que estava copiada em cada endpoint.
/// </summary>
public static class AuthSetup
{
    public const string AdminPolicy = "Admin";
    public const string AdminRole = "Admin";
    private const string LoginRateLimit = "login";

    public static IServiceCollection AddConsoleAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<OwnerOptions>(config.GetSection(OwnerOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JwtTokenGenerator>();

        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    // Sem chave configurada nenhum token valida: o login devolve 503 e só a API key funciona.
                    IssuerSigningKey = IsKeyUsable(jwt.Key)
                        ? JwtTokenGenerator.CreateSigningKey(jwt.Key)
                        : new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorization(options =>
            options.AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
                .RequireRole(AdminRole)));

        // Freio contra força bruta no login: 10 tentativas por minuto por IP.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", (LoginRequest req, IOptions<OwnerOptions> ownerOptions,
                                 IOptions<JwtOptions> jwtOptions, JwtTokenGenerator tokens) =>
        {
            var owner = ownerOptions.Value;
            if (!owner.IsConfigured || !IsKeyUsable(jwtOptions.Value.Key))
                return Results.Problem("Console login is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var emailMatches = string.Equals(req.Email?.Trim(), owner.OwnerEmail.Trim(), StringComparison.OrdinalIgnoreCase);
            // Verifica a senha mesmo com e-mail errado, pra o tempo de resposta não revelar se o e-mail existe.
            var passwordOk = VerifyPassword(owner.OwnerPasswordHash, req.Password ?? string.Empty);
            if (!emailMatches || !passwordOk)
                return Results.Unauthorized();

            var token = tokens.Generate(owner.OwnerEmail, owner.OwnerName, AdminRole);
            return Results.Ok(new LoginResponse(token.Token, token.ExpiresAtUtc, owner.OwnerName, owner.OwnerEmail));
        }).RequireRateLimiting(LoginRateLimit);

        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            name = user.FindFirstValue(ClaimTypes.Name),
            email = user.FindFirstValue(ClaimTypes.Email),
            role = user.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization(AdminPolicy);

        return app;
    }

    /// <summary>
    /// `dotnet run --project src/RootBoost.Api -- hash-password` lê a senha da entrada e imprime o hash
    /// pro `Auth__OwnerPasswordHash`. A senha não vai como argumento pra não ficar no histórico do terminal.
    /// </summary>
    public static bool TryRunHashPasswordCommand(string[] args)
    {
        if (args.Length == 0 || args[0] != "hash-password") return false;

        Console.Error.Write("Senha do dono do Console: ");
        var password = ReadPasswordMasked();
        if (password.Length < 12)
        {
            Console.Error.WriteLine("Use pelo menos 12 caracteres.");
            Environment.ExitCode = 1;
            return true;
        }

        // Com a senha mascarada um erro de digitação passa despercebido e o login falha depois
        // sem pista nenhuma. Digitar duas vezes pega isso na hora.
        Console.Error.Write("Repita a senha: ");
        if (ReadPasswordMasked() != password)
        {
            Console.Error.WriteLine("As senhas não conferem. Nada foi gerado.");
            Environment.ExitCode = 1;
            return true;
        }

        Console.WriteLine(new PasswordHasher<object>().HashPassword(new object(), password));
        return true;
    }

    /// <summary>
    /// Lê a senha mostrando `*` no lugar das letras. Com a entrada redirecionada (pipe, script)
    /// não há teclado pra interceptar, então lê a linha inteira.
    /// </summary>
    private static string ReadPasswordMasked()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;

        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length == 0) continue;
                password.Length--;
                Console.Error.Write("\b \b");
                continue;
            }
            if (char.IsControl(key.KeyChar)) continue;
            password.Append(key.KeyChar);
            Console.Error.Write('*');
        }
        Console.Error.WriteLine();
        return password.ToString();
    }

    public static void WarnIfAdminIsOpen(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration["Orders:ApiKey"]) && !app.Environment.IsDevelopment())
            app.Logger.LogWarning("Orders:ApiKey is not set: /orders and /admin/* are OPEN. Set it in production.");
    }

    private static bool VerifyPassword(string hash, string password)
    {
        try
        {
            return new PasswordHasher<object>().VerifyHashedPassword(new object(), hash, password)
                != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsKeyUsable(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length >= 32;
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc, string Name, string Email);
