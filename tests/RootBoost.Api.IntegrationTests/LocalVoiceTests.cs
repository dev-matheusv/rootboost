using RootBoost.Application.Studio;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Vozes do PC do worker no seletor (12/11): só da marca dona, padrão primeiro, voz do Matheus nunca.</summary>
public class LocalVoiceTests
{
    [Fact]
    public void Lists_brand_voices_default_first_and_never_the_owner_voice()
    {
        var r = new LocalVoiceRegistry();
        r.Set("pc1", [
            new("medlyfem2", "medlycare", false), new("medlynat1", "medlycare", true), new("matheus2a", "medlycare", false),
            new("outra", "loja", false), new("../x", "medlycare", false),
        ]);
        Assert.Equal(["medlynat1", "medlyfem2"], r.ForBrand("medlycare").Select(v => v.Key));
        Assert.Equal(["outra"], r.ForBrand("loja").Select(v => v.Key));

        r.Set("pc1", []); // o worker reenvia a lista inteira: voz removida some
        Assert.Empty(r.ForBrand("medlycare"));
    }
}
