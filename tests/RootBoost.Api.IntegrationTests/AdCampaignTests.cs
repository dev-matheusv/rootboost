using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Ads;
using RootBoost.Application.Ai;
using RootBoost.Domain;
using RootBoost.Infrastructure.Ads;
using RootBoost.Infrastructure.Ai;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Campanha pela IA (RB-31): proposta, regras do rascunho e criação pausada com retomada.</summary>
public class AdCampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static AdCampaignDraft Draft(string headline = "Ficha no sistema")
    {
        var d = new AdCampaignDraft(BrandKeys.MedlyCare, null, Now);
        d.Update("Teste", AdObjective.Traffic, 2000, ["br"], 25, 55, "https://cdn.example.com/v.mp4", null,
            "Texto do anúncio.", headline, "LEARN_MORE", "https://medlycare.com.br", "motivo", Now);
        return d;
    }

    [Fact]
    public void Draft_guards_budget_age_country_link_and_cta()
    {
        var d = Draft();
        Assert.Equal(["BR"], d.Countries);
        void Bad(Action a) => Assert.Throws<ArgumentException>(a);
        Bad(() => d.Update("T", AdObjective.Traffic, 100, ["BR"], 25, 55, "https://a.com/v.mp4", null, "x", "y", "LEARN_MORE", "https://a.com", null, Now));
        Bad(() => d.Update("T", AdObjective.Traffic, 2000, [], 25, 55, "https://a.com/v.mp4", null, "x", "y", "LEARN_MORE", "https://a.com", null, Now));
        Bad(() => d.Update("T", AdObjective.Traffic, 2000, ["BR"], 15, 55, "https://a.com/v.mp4", null, "x", "y", "LEARN_MORE", "https://a.com", null, Now));
        Bad(() => d.Update("T", AdObjective.Traffic, 2000, ["BR"], 25, 55, "http://a.com/v.mp4", null, "x", "y", "LEARN_MORE", "https://a.com", null, Now));
        Bad(() => d.Update("T", AdObjective.Traffic, 2000, ["BR"], 25, 55, "https://a.com/v.mp4", null, "x", "y", "COMPRE_JA", "https://a.com", null, Now));
    }

    [Fact]
    public void Editing_after_approval_goes_back_to_draft_and_needs_new_approval()
    {
        var d = Draft();
        d.Approve("dono@x", Now);
        Assert.Equal(AdCampaignStatus.Approved, d.Status);
        d.Update("Outro", AdObjective.Traffic, 3000, ["BR"], 25, 55, d.VideoUrl, null, "Texto.", "Título", "LEARN_MORE", d.LinkUrl, null, Now);
        Assert.Equal(AdCampaignStatus.Draft, d.Status);
        Assert.Null(d.ApprovedBy);
        Assert.Throws<InvalidOperationException>(() => d.StartCreating(Now));
    }

    [Fact]
    public async Task Creation_resumes_after_failure_without_duplicating_objects()
    {
        var store = new Store();
        var d = Draft("vai falhar");
        await store.AddAsync(d);
        d.Approve("dono@x", Now);
        var create = new CreateApprovedCampaign(store, new Accounts(), new FakeAdCampaignCreator(), TimeProvider.System, NullLogger<CreateApprovedCampaign>.Instance);

        var first = await create.RunAsync(d);
        Assert.False(first.Ok);
        Assert.Equal(AdCampaignStatus.Failed, d.Status);
        var campaign = d.ExternalCampaignId;
        Assert.NotNull(campaign);

        var second = await create.RunAsync(d);
        Assert.True(second.Ok);
        Assert.Equal(AdCampaignStatus.Created, d.Status);
        Assert.Equal(campaign, d.ExternalCampaignId); // não criou outra campanha
        Assert.NotNull(d.ExternalAdId);
    }

    [Fact]
    public async Task Without_ad_account_nothing_is_created()
    {
        var d = Draft();
        d.Approve("dono@x", Now);
        var create = new CreateApprovedCampaign(new Store(), new Accounts(empty: true), new FakeAdCampaignCreator(), TimeProvider.System, NullLogger<CreateApprovedCampaign>.Instance);
        var r = await create.RunAsync(d);
        Assert.False(r.Ok);
        Assert.Equal(AdCampaignStatus.Approved, d.Status);
        Assert.Null(d.ExternalCampaignId);
    }

    [Fact]
    public async Task Proposal_from_the_ai_respects_the_budget_cap_and_records_cost()
    {
        var usage = new Usage();
        var proposer = new CampaignProposer(new FakeLanguageModel(), usage, TimeProvider.System);
        var job = new CreativeJob(BrandKeys.MedlyCare, CreativeKind.Organic, "Ficha de harmonização", null, Now);
        var p = await proposer.ProposeAsync(BrandKeys.MedlyCare, "MedlyCare", null, job, "https://cdn.example.com/v.mp4", null);
        Assert.Equal(AdCampaignStatus.Draft, p.Draft.Status);
        Assert.InRange(p.Draft.DailyBudgetCents, AdCampaignDraft.MinDailyBudgetCents, 5_000);
        Assert.Equal(job.Id, p.Draft.CreativeJobId);
        Assert.Single(usage.Items, u => u.Purpose == "ads-campaign");
    }

    private static List<AdInsightDaily> Rows(int days, decimal spendPerDay, long impressionsPerDay, long clicksPerDay, long conversionsPerDay)
    {
        var rows = new List<AdInsightDaily>();
        for (var i = 0; i < days; i++)
        {
            var r = new AdInsightDaily(BrandKeys.MedlyCare, "meta", "act_1", AdLevel.Campaign, "c1", "Teste", null, new DateOnly(2026, 11, 1).AddDays(i), "BRL", Now);
            r.SetMetrics("Teste", spendPerDay, impressionsPerDay, clicksPerDay, impressionsPerDay, conversionsPerDay, "BRL", Now);
            rows.Add(r);
        }
        return rows;
    }

    private static AdCampaignDraft Created(AdObjective objective = AdObjective.Traffic)
    {
        var d = new AdCampaignDraft(BrandKeys.MedlyCare, null, Now);
        d.Update("Teste", objective, 2000, ["BR"], 25, 55, "https://cdn.example.com/v.mp4", null, "Texto.", "Título", "LEARN_MORE", "https://medlycare.com.br", null, Now);
        d.Approve("dono@x", Now);
        d.StartCreating(Now);
        d.SetCampaign("c1");
        d.Created("a1", Now);
        return d;
    }

    [Theory]
    [InlineData(1, 10, 300, 3, 0, "Wait")]          // cedo: poucos dias e impressões
    [InlineData(4, 10, 500, 1, 0, "ReviewCreative")] // CTR 0,2%: vídeo não chama atenção
    [InlineData(4, 10, 500, 10, 0, "Scale")]        // CTR 2%: sobe orçamento
    [InlineData(4, 10, 500, 5, 0, "Keep")]          // CTR 1%: deixa rodar
    public void Advisor_for_traffic(int days, decimal spend, long imp, long clicks, long conv, string expected)
    {
        var a = CampaignAdvisor.Advise(Created(), Rows(days, spend, imp, clicks, conv), new AdsStopRule(), new DateOnly(2026, 11, 10));
        Assert.Equal(expected, a.Kind.ToString());
        if (a.Kind == AdviceKind.Scale) Assert.Equal(24m, a.SuggestedDailyBudget);
    }

    [Fact]
    public void Advisor_pauses_leads_campaign_that_spent_without_result_and_respects_manual_pause()
    {
        var c = Created(AdObjective.Leads);
        var a = CampaignAdvisor.Advise(c, Rows(5, 50, 2000, 40, 0), new AdsStopRule(), new DateOnly(2026, 11, 10));
        Assert.Equal(AdviceKind.Pause, a.Kind);
        c.MarkPaused("dono@x", Now);
        Assert.Equal(AdviceKind.Keep, CampaignAdvisor.Advise(c, Rows(5, 50, 2000, 40, 0), new AdsStopRule(), new DateOnly(2026, 11, 10)).Kind);
    }

    private sealed class Store : IAdCampaignStore
    {
        private readonly List<AdCampaignDraft> _items = new();
        public Task AddAsync(AdCampaignDraft draft, CancellationToken ct = default) { _items.Add(draft); return Task.CompletedTask; }
        public Task<AdCampaignDraft?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_items.FirstOrDefault(i => i.Id == id));
        public Task SaveAsync(AdCampaignDraft draft, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdCampaignDraft>> ListByBrandAsync(string brandKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdCampaignDraft>>(_items.Where(i => i.BrandKey == brandKey).ToList());
    }

    private sealed class Accounts(bool empty = false) : IAdAccountDirectory
    {
        public IReadOnlyList<AdAccount> All { get; } = empty ? [] : [new AdAccount(BrandKeys.MedlyCare, FakeAdInsightsReader.FakePlatform, "act_fake")];
    }

    private sealed class Usage : IAiUsageStore
    {
        public List<AiUsageEntry> Items { get; } = new();
        public Task AddAsync(AiUsageEntry entry, CancellationToken ct = default) { Items.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<AiUsageEntry>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AiUsageEntry>>(Items);
    }
}
