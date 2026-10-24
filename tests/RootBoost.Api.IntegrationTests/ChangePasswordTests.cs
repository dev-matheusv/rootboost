using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Troca de senha pelo Console (RB-26), com a variável como reset de emergência. Fábrica própria: muda a senha.</summary>
public sealed class ChangePasswordFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Orders:ApiKey", SecuredWebApplicationFactory.ApiKey);
        builder.UseSetting("Jwt:Key", "chave-de-assinatura-com-mais-de-32-caracteres!");
        builder.UseSetting("Auth:OwnerEmail", SecuredWebApplicationFactory.OwnerEmail);
        builder.UseSetting("Auth:OwnerName", "Dono");
        builder.UseSetting("Auth:OwnerPasswordHash", new PasswordHasher<object>().HashPassword(new object(), SecuredWebApplicationFactory.OwnerPassword));
    }
}

public class ChangePasswordTests : IClassFixture<ChangePasswordFactory>
{
    private const string Email = SecuredWebApplicationFactory.OwnerEmail;
    private readonly ChangePasswordFactory _factory;
    public ChangePasswordTests(ChangePasswordFactory factory) => _factory = factory;

    private sealed record LoginBody(string Token);

    private static Task<HttpResponseMessage> Login(HttpClient c, string password) =>
        c.PostAsJsonAsync("/auth/login", new { email = Email, password });

    private static async Task<HttpClient> Authed(HttpClient c, string password)
    {
        var res = await Login(c, password);
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<LoginBody>())!.Token;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Change_password_rules_and_env_reset_in_order()
    {
        // Um teste só, em sequência: a senha é estado compartilhado do mesmo banco.
        var original = SecuredWebApplicationFactory.OwnerPassword;
        var client = await Authed(_factory.CreateClient(), original);

        var wrong = await client.PostAsJsonAsync("/auth/change-password", new { currentPassword = "errada-errada-123", newPassword = "outra-senha-bem-longa" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var shortOne = await client.PostAsJsonAsync("/auth/change-password", new { currentPassword = original, newPassword = "curta" });
        Assert.Equal(HttpStatusCode.BadRequest, shortOne.StatusCode);

        const string changed = "senha-trocada-pelo-console";
        var ok = await client.PostAsJsonAsync("/auth/change-password", new { currentPassword = original, newPassword = changed });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(_factory.CreateClient(), original)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(_factory.CreateClient(), changed)).StatusCode);

        // Reset de emergência: trocar a variável substitui a senha do banco.
        const string rescue = "senha-de-emergencia-123";
        var rescued = _factory.WithWebHostBuilder(b =>
            b.UseSetting("Auth:OwnerPasswordHash", new PasswordHasher<object>().HashPassword(new object(), rescue)));
        Assert.Equal(HttpStatusCode.OK, (await Login(rescued.CreateClient(), rescue)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(rescued.CreateClient(), changed)).StatusCode);
    }

    [Fact]
    public async Task Script_api_key_cannot_change_the_password()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", SecuredWebApplicationFactory.ApiKey);
        var res = await client.PostAsJsonAsync("/auth/change-password", new { currentPassword = "x", newPassword = "senha-qualquer-longa" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
