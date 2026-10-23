using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Pipeline real com o login do Console configurado: dono, chave do JWT e API key dos scripts.
/// </summary>
public sealed class SecuredWebApplicationFactory : CustomWebApplicationFactory
{
    public const string OwnerEmail = "owner@rootboost.test";
    public const string OwnerPassword = "senha-forte-de-teste-123";
    public const string ApiKey = "chave-de-script-de-teste";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Orders:ApiKey", ApiKey);
        builder.UseSetting("Jwt:Key", "chave-de-assinatura-com-mais-de-32-caracteres!");
        builder.UseSetting("Auth:OwnerEmail", OwnerEmail);
        builder.UseSetting("Auth:OwnerName", "Dono");
        builder.UseSetting("Auth:OwnerPasswordHash", new PasswordHasher<object>().HashPassword(new object(), OwnerPassword));
    }
}

public class ConsoleAuthTests : IClassFixture<SecuredWebApplicationFactory>
{
    private readonly SecuredWebApplicationFactory _factory;

    public ConsoleAuthTests(SecuredWebApplicationFactory factory) => _factory = factory;

    private async Task<string> LoginAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/auth/login",
            new { email = SecuredWebApplicationFactory.OwnerEmail, password = SecuredWebApplicationFactory.OwnerPassword });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<LoginBody>();
        return body!.Token;
    }

    [Fact]
    public async Task Login_with_owner_credentials_returns_token_that_opens_me()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<MeBody>("/auth/me");

        Assert.Equal(SecuredWebApplicationFactory.OwnerEmail, me!.Email);
        Assert.Equal("Admin", me.Role);
    }

    [Theory]
    [InlineData(SecuredWebApplicationFactory.OwnerEmail, "senha-errada")]
    [InlineData("outro@rootboost.test", SecuredWebApplicationFactory.OwnerPassword)]
    public async Task Login_with_wrong_credentials_is_rejected(string email, string password)
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_reject_anonymous_callers()
    {
        var client = _factory.CreateClient();
        foreach (var path in new[] { "/orders", "/admin/products/performance", "/admin/traffic/plan", "/admin/growth/plan" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Orders_accept_the_console_token()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/orders")).StatusCode);
    }

    [Fact]
    public async Task Orders_accept_the_script_api_key_and_reject_a_wrong_one()
    {
        var good = _factory.CreateClient();
        good.DefaultRequestHeaders.Add("X-Api-Key", SecuredWebApplicationFactory.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await good.GetAsync("/orders")).StatusCode);

        var bad = _factory.CreateClient();
        bad.DefaultRequestHeaders.Add("X-Api-Key", "chave-errada");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync("/orders")).StatusCode);
    }

    [Fact]
    public async Task A_forged_token_is_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.e30.assinatura-falsa");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/orders")).StatusCode);
    }

    private sealed record LoginBody(string Token, DateTime ExpiresAtUtc, string Name, string Email);
    private sealed record MeBody(string Name, string Email, string Role);
}

public class ConsoleAuthNotConfiguredTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConsoleAuthNotConfiguredTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_answers_503_when_the_owner_is_not_configured()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/auth/login", new { email = "a@b.c", password = "x" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
