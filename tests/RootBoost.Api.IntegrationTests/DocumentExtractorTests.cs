using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RootBoost.Infrastructure.Documents;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Etapa 1 da Root.IA (19/11): a porta do RootBoost lê documento pelo pacote comum Root.Ia.Documents.</summary>
public class DocumentExtractorTests
{
    private readonly DocumentTextExtractor _extractor = new();

    [Fact]
    public async Task Reads_text_and_docx_through_the_shared_package()
    {
        Assert.Equal("Agenda\nRelatórios", await _extractor.ExtractAsync("guia.md", new MemoryStream(Encoding.UTF8.GetBytes("Agenda\r\nRelatórios\r\n"))));

        using var docx = new MemoryStream();
        using (var w = WordprocessingDocument.Create(docx, WordprocessingDocumentType.Document, true))
        {
            w.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text("Toque em Mais e depois em Relatórios.")))));
        }
        docx.Position = 0;
        Assert.Contains("Toque em Mais e depois em Relatórios.", await _extractor.ExtractAsync("manual.docx", docx));
    }

    [Fact]
    public async Task Refuses_formats_that_need_a_model_or_are_unknown()
    {
        Assert.False(DocumentTextExtractor.Supports("foto.png")); // imagem pede modelo: segue desligada
        Assert.False(DocumentTextExtractor.Supports("programa.exe"));
        Assert.True(DocumentTextExtractor.Supports("planilha.xlsx"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _extractor.ExtractAsync("programa.exe", new MemoryStream([1, 2, 3])));
    }
}
