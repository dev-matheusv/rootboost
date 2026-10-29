using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class NarrationVoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static NarrationVoice Laura() => NarrationVoice.Clone("medlycare", "Laura Cabeleireira", "voices/x/ref.webm", null,
        "Laura Souza", NarrationVoice.ConsentFor("Laura Souza", "Salão da Laura"), Now);

    [Fact]
    public void Clone_without_consent_is_refused_and_the_key_carries_the_brand()
    {
        Assert.Throws<ArgumentException>(() => NarrationVoice.Clone("medlycare", "Laura", "voices/x/ref.webm", null, "", "autorizo", Now));
        Assert.Throws<ArgumentException>(() => NarrationVoice.Clone("medlycare", "Laura", "voices/x/ref.webm", null, "Laura", " ", Now));
        var v = Laura();
        Assert.StartsWith("medlycare-laura-cabeleireira-", v.Key);
        Assert.Equal(Now, v.ConsentAt);
        Assert.Contains("Salão da Laura", v.ConsentText);
    }

    [Fact]
    public void Only_the_worker_that_claimed_completes_and_failures_retry_until_the_limit()
    {
        var v = Laura();
        v.Claim("pc", Now, TimeSpan.FromMinutes(15));
        Assert.Throws<InvalidOperationException>(() => v.Complete("outro", [new VoiceSample(1, "a")], Now));
        v.Fail("pc", "dmaker caiu", retry: true, Now);
        Assert.Equal(NarrationVoiceStatus.Queued, v.Status);
        v.Claim("pc", Now, TimeSpan.FromMinutes(15));
        v.Complete("pc", [new VoiceSample(1, "a"), new VoiceSample(2, "b")], Now);
        Assert.Throws<ArgumentException>(() => v.Choose(3, Now));
        v.Choose(2, Now);
        Assert.Equal(2, v.ChosenSeed);
    }

    [Fact]
    public void Bank_voice_only_accepts_the_known_words()
    {
        Assert.Equal("female, middle-aged, low pitch", NarrationVoice.Design("medlycare", "Ana", "female", "middle-aged", "low pitch", Now).Instruct);
        Assert.Throws<ArgumentException>(() => NarrationVoice.Design("medlycare", "Ana", "female; rm -rf", "middle-aged", "low pitch", Now));
    }
}

public class VoiceReviewDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_script_throws_away_the_chosen_voice_and_redo_moves_to_new_seeds()
    {
        var job = new CreativeJob("medlycare", CreativeKind.Organic, "Teste", null, Now);
        job.SetScript("{}", "[]", Now);
        job.QueueRender(Now);
        job.Claim("pc", Now, TimeSpan.FromMinutes(15));
        job.DeliverVoice("pc", [new VoiceTake(1, "a", 0), new VoiceTake(2, "b", 0)], Now);
        Assert.Equal(CreativeJobStatus.VoiceReview, job.Status);
        Assert.Null(job.WorkerId); // não prende o worker esperando gente

        job.RedoVoice(Now);
        Assert.Equal(3, job.Seed);
        Assert.Empty(job.VoiceTakes);
        job.Claim("pc", Now, TimeSpan.FromMinutes(15));
        job.DeliverVoice("pc", [new VoiceTake(3, "c", 0)], Now);
        job.ChooseVoice(3, Now);
        Assert.True(job.VoiceApproved);

        job.Claim("pc", Now, TimeSpan.FromMinutes(15));
        job.Fail("pc", "app fora", retry: false, Now);
        job.SetScript("{\"x\":1}", "[]", Now);
        Assert.False(job.VoiceApproved);
    }
}

public class DemoBrandKeyTests
{
    [Fact]
    public void Only_keys_ending_in_demo_are_demo()
    {
        Assert.True(BrandKeys.IsDemo(BrandKeys.Demo));
        Assert.False(BrandKeys.IsDemo(BrandKeys.MedlyCare));
        Assert.False(BrandKeys.IsDemo("demo-real"));
    }
}

public class RejectJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rejected_video_goes_back_to_the_script_keeping_the_chosen_voice()
    {
        var job = new CreativeJob("medlycare", CreativeKind.Organic, "Teste", null, Now);
        job.SetScript("{}", "[]", Now);
        job.QueueRender(Now);
        job.Claim("pc", Now, TimeSpan.FromMinutes(15));
        job.DeliverVoice("pc", [new VoiceTake(1, "a", 0)], Now);
        job.ChooseVoice(1, Now);
        job.Claim("pc", Now, TimeSpan.FromMinutes(15));
        Assert.False(job.CanBeDeleted); // worker gerando
        job.Deliver("pc", [new CreativeOutput("video", "https://x.test/v.mp4")], null, 1, Now);
        job.Reject(Now);
        Assert.Equal(CreativeJobStatus.ScriptReady, job.Status);
        Assert.Empty(job.Outputs);
        Assert.True(job.VoiceApproved);
        Assert.True(job.CanBeDeleted);
    }
}
