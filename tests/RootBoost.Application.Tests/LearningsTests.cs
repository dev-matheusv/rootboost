using System.Text.Json;
using RootBoost.Application.Abstractions;

namespace RootBoost.Application.Tests;

/// <summary>Aprendizados versionados (19/11): só o canal pedido entra no prompt, do mais novo pro mais antigo.</summary>
public class LearningsTests
{
    private sealed class OneDocument(KnowledgeDocument doc) : IKnowledgeBase
    {
        public string Playbook => string.Empty;
        public IReadOnlyList<KnowledgeDocument> Documents => [doc];
        public KnowledgeDocument? Find(string name) => name == doc.Name ? doc : null;
    }

    [Fact]
    public void Prompt_keeps_the_channel_and_puts_the_newest_first()
    {
        var raw = JsonDocument.Parse("""
            {"items":[
              {"id":"a","date":"2026-10-23","channel":"video","tested":"voz frase a frase","result":"reprovada","confidence":"media"},
              {"id":"b","date":"2026-11-19","channel":"meta-ads","tested":"posicionamento automático","result":"Audience Network","confidence":"media"},
              {"id":"c","date":"2026-11-19","channel":"video","tested":"pausa de 0,23 s","result":"atropela","confidence":"media"}
            ]}
            """).RootElement.Clone();
        var kb = new OneDocument(new KnowledgeDocument("learnings/learnings", 1, new DateOnly(2026, 11, 19), "teste", null, raw));

        var video = Learnings.ForPrompt(kb, "video").Split(Environment.NewLine);
        Assert.Equal(2, video.Length);
        Assert.StartsWith("- pausa de 0,23 s → atropela", video[0]);
        Assert.DoesNotContain("Audience", string.Join(" ", video));
        Assert.Contains("Audience Network", Learnings.ForPrompt(kb, "meta-ads"));
        Assert.Equal(string.Empty, Learnings.ForPrompt(new OneDocument(new KnowledgeDocument("outro", 1, default, "", null, raw)), "video"));
    }
}
