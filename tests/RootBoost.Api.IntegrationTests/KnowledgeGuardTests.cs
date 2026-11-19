using System.Text.RegularExpressions;
using RootBoost.Infrastructure.Knowledge;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Portão do conhecimento versionado (plano da Root.IA, 4.6): nada em knowledge/ pode ter dado pessoal ou segredo,
/// porque tudo ali vai pro prompt da IA. E o pacote da marca carrega.
/// </summary>
public class KnowledgeGuardTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "knowledge", "learnings"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("knowledge/ não encontrado"), "knowledge");
    }

    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        ("CPF", new Regex(@"\b(?!000\.000\.000-00)\d{3}\.\d{3}\.\d{3}-\d{2}\b")), // 000.000.000-00 é o modelo de formato
        ("e-mail", new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase)),
        ("telefone", new Regex(@"\(?\b\d{2}\)?\s?9\d{4}-?\d{4}\b")),
        ("chave", new Regex(@"(sk-ant-|sk_live_|sk_test_|EAA[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{20,})")),
    ];

    [Fact]
    public void Knowledge_has_no_personal_data_or_secrets()
    {
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Root(), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".md") || f.EndsWith(".json")))
        {
            var text = File.ReadAllText(file);
            foreach (var (name, pattern) in Forbidden)
                if (pattern.Match(text) is { Success: true } m) found.Add($"{Path.GetRelativePath(Root(), file)}: {name} ({m.Value[..Math.Min(m.Value.Length, 6)]}...)");
        }
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void Brand_package_loads_for_its_brand_only()
    {
        var kb = FileKnowledgeBase.Load(Root());
        Assert.Contains("o que o sistema faz", kb.BrandNotes("medlycare"));
        Assert.Equal(string.Empty, kb.BrandNotes("outra-marca"));
    }
}
