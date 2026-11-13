using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using RootBoost.Application.Inbox;
using RootBoost.Domain;
using RootBoost.Infrastructure.Inbox;

namespace RootBoost.Api.IntegrationTests;

public class InboxParseTests
{
    private const string Business = "17841400000000000";

    [Fact]
    public void Message_from_contact_uses_sender_as_contact()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"m1","created_time":"2026-11-13T12:00:00+0000","from":{"username":"cliente.x","id":"igsid-1"},
             "to":{"data":[{"username":"medlycare","id":"17841400000000000"}]},"message":"Oi, tem agenda?"}
            """);
        var row = InstagramInboxReader.Parse("c1", Business, doc.RootElement)!;
        Assert.False(row.FromBusiness);
        Assert.Equal("igsid-1", row.ContactId);
        Assert.Equal("cliente.x", row.ContactName);
        Assert.Equal("Oi, tem agenda?", row.Text);
        Assert.False(row.HasAttachment);
    }

    [Fact]
    public void Message_from_business_uses_recipient_as_contact_and_empty_text_is_attachment()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"m2","created_time":"2026-11-13T12:05:00+0000","from":{"username":"medlycare","id":"17841400000000000"},
             "to":{"data":[{"username":"cliente.x","id":"igsid-1"}]}}
            """);
        var row = InstagramInboxReader.Parse("c1", Business, doc.RootElement)!;
        Assert.True(row.FromBusiness);
        Assert.Equal("igsid-1", row.ContactId);
        Assert.True(row.HasAttachment);
    }

    [Fact]
    public void Thread_reply_window_counts_from_last_contact_message()
    {
        var at = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var msgs = new[]
        {
            new InboxMessage(BrandKeys.MedlyCare, SocialChannel.Instagram, "c1", "m1", "igsid-1", "cliente.x", false, "Oi", false, at, at),
            new InboxMessage(BrandKeys.MedlyCare, SocialChannel.Instagram, "c1", "m2", "igsid-1", null, true, "Olá!", false, at.AddHours(1), at),
        };
        var t = Assert.Single(InboxThreads.From(msgs));
        Assert.Equal("Olá!", t.LastText);
        Assert.True(t.LastFromBusiness);
        Assert.Equal("cliente.x", t.ContactName);
        Assert.Equal(at.AddHours(24), t.ReplyUntil);
    }
}

public class InboxApiPostgresTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public InboxApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Sync_is_idempotent_and_lists_threads_and_messages()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Inbox:Reader", "Fake")).CreateClient();

        var first = await (await client.PostAsync("/admin/brands/medlycare/inbox/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsync("/admin/brands/medlycare/inbox/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, first.EnumerateArray().Single().GetProperty("inserted").GetInt32());
        Assert.Equal(0, second.EnumerateArray().Single().GetProperty("inserted").GetInt32());

        var threads = await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/inbox");
        Assert.Equal(2, threads.GetArrayLength());
        Assert.Equal("fake-conv-1", threads[0].GetProperty("conversationId").GetString());
        Assert.True(threads[0].GetProperty("canReply").GetBoolean());
        Assert.False(threads[1].GetProperty("canReply").GetBoolean());

        var conv = await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/inbox/fake-conv-1");
        Assert.Equal(3, conv.GetArrayLength());
        Assert.Equal("Quero, quanto custa?", conv[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_reader_sync_says_why()
    {
        var res = await _factory.CreateClient().PostAsync("/admin/brands/medlycare/inbox/sync", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("desligada", await res.Content.ReadAsStringAsync());
    }
}
