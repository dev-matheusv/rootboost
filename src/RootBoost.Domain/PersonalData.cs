using System.Text.Json;

namespace RootBoost.Domain;

/// <summary>Como a marca trata dado pessoal em peça: borrar o que for real, ou só aceitar dado fictício (nem borrado).</summary>
public enum PersonalDataPolicy
{
    Blur,
    FictionalOnly,
}

/// <summary>
/// O que a IA achou na foto. Unchecked: ainda não olhou (vídeo, ou foto sem descrição). Fictional: dado de demonstração
/// conhecido da marca (ex.: Clínica Vitta, CPF 999.000.00x). Uncertain: na dúvida, trata como real.
/// </summary>
public enum PersonalDataStatus
{
    Unchecked,
    None,
    Fictional,
    Real,
    Uncertain,
}

/// <summary>Área a borrar, em fração da imagem (0 a 1), já de pé (orientação do EXIF aplicada).</summary>
public sealed record RedactionBox(double X, double Y, double W, double H)
{
    public bool IsValid => X is >= 0 and <= 1 && Y is >= 0 and <= 1 && W is > 0 and <= 1 && H is > 0 and <= 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Write(IReadOnlyList<RedactionBox> boxes) => JsonSerializer.Serialize(boxes, Json);

    public static IReadOnlyList<RedactionBox> Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<RedactionBox>>(json, Json) ?? [];
}
