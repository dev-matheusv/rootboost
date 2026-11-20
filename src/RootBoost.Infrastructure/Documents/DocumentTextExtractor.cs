using RootBoost.Application.Ai;
using RootIa = Root.Ia.Documents;

namespace RootBoost.Infrastructure.Documents;

/// <summary>
/// Leitura de documento pela Root.IA comum (RB-17, Etapa 1, 19/11): PDF, DOCX, PPTX, XLSX e texto. A porta do
/// RootBoost continua a mesma; por baixo é o <see cref="RootIa.DocumentTextExtractor"/>, sem modelo (imagem desligada,
/// como antes). Erro de leitura vira InvalidOperationException com mensagem segura, como a porta espera.
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly RootIa.DocumentTextExtractor Inner = new();

    public static bool Supports(string fileName) =>
        Inner.CanExtract(fileName, null) && !RootIa.DocumentTextExtractor.RequiresModel(fileName, null);

    public async Task<string> ExtractAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (!Supports(fileName))
            throw new InvalidOperationException($"arquivo {Path.GetExtension(fileName).ToLowerInvariant()} não é aceito: envie PDF, DOCX, PPTX, XLSX ou texto");
        try
        {
            var doc = await Inner.ExtractAsync(content, fileName, null, null, ct);
            return doc.Text;
        }
        catch (RootIa.DocumentExtractionException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }
}
