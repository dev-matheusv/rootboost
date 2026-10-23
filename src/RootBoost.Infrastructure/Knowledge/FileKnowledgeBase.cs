using System.Globalization;
using System.Text.Json;
using RootBoost.Application.Abstractions;

namespace RootBoost.Infrastructure.Knowledge;

/// <summary>
/// Carrega knowledge/ uma vez no boot. Arquivo inválido derruba o boot de propósito: melhor um deploy que
/// não sobe do que a IA gerando copy com uma regra corrompida ou pela metade.
/// </summary>
public sealed class FileKnowledgeBase : IKnowledgeBase
{
    private readonly Dictionary<string, KnowledgeDocument> _byName;

    private FileKnowledgeBase(string playbook, Dictionary<string, KnowledgeDocument> byName)
    {
        Playbook = playbook;
        _byName = byName;
    }

    public string Playbook { get; }

    public IReadOnlyList<KnowledgeDocument> Documents => _byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

    public KnowledgeDocument? Find(string name) => _byName.GetValueOrDefault(name.Trim('/').ToLowerInvariant());

    public static FileKnowledgeBase Load(string root)
    {
        if (!Directory.Exists(root))
            throw new InvalidOperationException($"Pasta de conhecimento não encontrada: {root}");

        var playbookPath = Path.Combine(root, "copy", "playbook.md");
        if (!File.Exists(playbookPath))
            throw new InvalidOperationException($"Playbook não encontrado: {playbookPath}");

        var docs = new Dictionary<string, KnowledgeDocument>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var name = Path.ChangeExtension(Path.GetRelativePath(root, file), null)!
                .Replace('\\', '/').ToLowerInvariant();
            docs[name] = Parse(name, File.ReadAllText(file));
        }

        return new FileKnowledgeBase(File.ReadAllText(playbookPath), docs);
    }

    /// <summary>Valida o envelope comum: version, updatedAt, source, items com id único e não vazio.</summary>
    public static KnowledgeDocument Parse(string name, string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"knowledge/{name}.json não é JSON válido: {ex.Message}", ex);
        }

        int version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        if (version < 1) throw Invalid(name, "'version' precisa ser inteiro >= 1");

        if (!root.TryGetProperty("updatedAt", out var u) ||
            !DateOnly.TryParseExact(u.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var updatedAt))
            throw Invalid(name, "'updatedAt' precisa ser uma data yyyy-MM-dd");

        var source = root.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(source)) throw Invalid(name, "'source' é obrigatório (de onde veio a regra)");

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw Invalid(name, "'items' precisa ser uma lista");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) throw Invalid(name, "todo item precisa de 'id'");
            if (!ids.Add(id)) throw Invalid(name, $"id repetido: '{id}'");
        }

        var notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
        return new KnowledgeDocument(name, version, updatedAt, source!, notes, root);
    }

    private static InvalidOperationException Invalid(string name, string why) =>
        new($"knowledge/{name}.json inválido: {why}");
}
