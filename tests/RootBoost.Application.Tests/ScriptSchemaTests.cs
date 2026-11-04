using System.Text.Json;
using RootBoost.Application.Ai;

namespace RootBoost.Application.Tests;

public class ScriptSchemaTests
{
    private static string[] Enum(JsonElement e) => e.GetProperty("enum").EnumerateArray().Select(x => x.GetString()!).ToArray();

    /// <summary>A IA só escolhe o que está no schema: opção nova fora dele nunca aparece no vídeo.</summary>
    [Fact]
    public void Schema_offers_every_transition_and_look_the_pipeline_accepts()
    {
        var root = FindRoot(ScriptWriter.Schema);
        var block = root.GetProperty("properties").GetProperty("blocos").GetProperty("items").GetProperty("properties");
        Assert.Equal(ScriptWriter.Transitions.Order(), Enum(block.GetProperty("transicao")).Order());
        Assert.Equal(ScriptWriter.Looks.Order(), Enum(root.GetProperty("properties").GetProperty("look")).Order());
    }

    private static JsonElement FindRoot(JsonElement schema) => schema.TryGetProperty("schema", out var inner) ? inner : schema;
}
