using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RootBoost.Application.Ai;
using UglyToad.PdfPig;

namespace RootBoost.Infrastructure.Documents;

/// <summary>
/// Texto de PDF, DOCX e texto puro (mesma leitura do extrator do RootFlow, até o Root.IA comum do RB-17).
/// Página de PDF que não abre é pulada; o resto do documento segue.
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly string[] PlainText = [".txt", ".md", ".csv", ".html", ".htm"];

    public static bool Supports(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".pdf" or ".docx" || PlainText.Contains(ext);
    }

    public async Task<string> ExtractAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        // PdfPig e OpenXml pedem stream com busca: copia pra memória (o limite do upload segura o tamanho).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var text = ext switch
        {
            ".pdf" => Pdf(buffer),
            ".docx" => Docx(buffer),
            _ when PlainText.Contains(ext) => Encoding.UTF8.GetString(buffer.ToArray()),
            _ => throw new InvalidOperationException($"arquivo {ext} não é aceito: envie PDF, DOCX ou texto"),
        };
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string Pdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        using var pages = document.GetPages().GetEnumerator();
        while (true)
        {
            try { if (!pages.MoveNext()) break; }
            catch (Exception) { break; } // dicionário de página quebrado: para aqui
            try
            {
                var text = pages.Current.Text;
                if (string.IsNullOrWhiteSpace(text)) text = string.Join(" ", pages.Current.GetWords().Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(text)) builder.AppendLine(text);
            }
            catch (Exception) { /* página ilegível: pula */ }
        }
        return builder.ToString();
    }

    private static string Docx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var builder = new StringBuilder();
        foreach (var p in body.Descendants<Paragraph>())
            if (!string.IsNullOrWhiteSpace(p.InnerText)) builder.AppendLine(p.InnerText);
        return builder.ToString();
    }
}
