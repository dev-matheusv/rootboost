namespace RootBoost.Application.Studio;

/// <summary>
/// Voz pronta do banco (RB-59): descrição do DMaker (gênero, idade, tom) e a semente da amostra que a pessoa ouve no
/// catálogo. A amostra fica no Console (<c>/vozes-banco/{Id}.m4a</c>), sempre com o mesmo texto do dia a dia, pra
/// comparar uma voz com a outra. <see cref="Approved"/> só vira true depois que o Matheus ouve a amostra (o portão de
/// palavras não pega gaguejo nem sotaque).
/// </summary>
public sealed record BankVoice(string Id, string Name, string Gender, string Age, string Pitch, int Seed, bool Approved);

public static class VoiceBank
{
    /// <summary>A primeira feminina é a receita da voz escolhida pro MedlyCare (medlynat1, semente 1).</summary>
    public static readonly IReadOnlyList<BankVoice> All =
    [
        new("clara", "Clara", "female", "young adult", "moderate pitch", 1, false),
        new("bia", "Bia", "female", "young adult", "low pitch", 1, false),
        new("helena", "Helena", "female", "middle-aged", "moderate pitch", 1, false),
        new("marta", "Marta", "female", "middle-aged", "low pitch", 1, false),
        new("lucas", "Lucas", "male", "young adult", "moderate pitch", 1, false),
        new("rafael", "Rafael", "male", "middle-aged", "moderate pitch", 1, false),
        new("paulo", "Paulo", "male", "middle-aged", "low pitch", 1, false),
        new("antonio", "Antônio", "male", "elderly", "low pitch", 1, false),
    ];

    public static BankVoice? Find(string? id) =>
        All.FirstOrDefault(v => string.Equals(v.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
}
