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
