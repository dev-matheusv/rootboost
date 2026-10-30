using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Ai;

namespace RootBoost.Infrastructure.Ai;

/// <summary>Claude pelo SDK oficial, com resposta presa ao JSON Schema pedido (structured outputs).</summary>
public sealed class ClaudeLanguageModel : ILanguageModel
{
    private readonly AnthropicClient _client;
    private readonly AiOptions _options;
    private readonly ILogger<ClaudeLanguageModel> _logger;

    public ClaudeLanguageModel(AiOptions options, ILogger<ClaudeLanguageModel> logger)
    {
        _logger = logger;
        _options = options;
        _client = new AnthropicClient { ApiKey = options.ApiKey };
    }

    public async Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default)
    {
        var schema = request.Schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        Message message;
        try
        {
            message = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _options.ModelFor(request.Purpose),
                MaxTokens = 16000,
                System = request.System,
                Messages = [new UserMessageParam { Content = UserContent(request) }],
                OutputConfig = new OutputConfig { Effort = EffortOf(_options.Effort), Format = new JsonOutputFormat { Schema = schema } },
            }, ct);
        }
        catch (Anthropic.Exceptions.AnthropicApiException ex)
        {
            // O corpo da resposta diz o motivo (modelo, imagem, limite); vai pro log, nunca a chave nem o conteúdo enviado.
            _logger.LogWarning(ex, "Claude recusou a chamada {Purpose}: {Status} {Body}", request.Purpose, ex.StatusCode, ex.ResponseBody);
            throw new AiRequestFailedException($"a IA não aceitou o pedido ({(int)ex.StatusCode}); tente de novo ou avise o suporte", ex);
        }

        if (message.StopReason == "refusal")
            throw new InvalidOperationException($"a IA recusou o pedido ({message.StopDetails?.Category})");
        if (message.StopReason == "max_tokens")
            throw new InvalidOperationException("a resposta da IA passou do tamanho máximo");

        var json = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var model = _options.ModelFor(request.Purpose);
        long input = message.Usage.InputTokens, output = message.Usage.OutputTokens;
        return new LanguageModelResult(json, model, input, output, AiPricing.Cost(model, input, output));
    }

    /// <summary>Só texto, ou a foto antes do texto quando houver (ordem recomendada pra imagem).</summary>
    private static MessageContentParam UserContent(LanguageModelRequest request)
    {
        if (string.IsNullOrEmpty(request.ImageJpegBase64)) return request.User;
        List<ContentBlockParam> blocks =
        [
            new ImageBlockParam { Source = new Base64ImageSource { Data = request.ImageJpegBase64, MediaType = MediaType.ImageJpeg } },
            new TextBlockParam { Text = request.User },
        ];
        return blocks;
    }

    private static Effort EffortOf(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}

/// <summary>Modo de teste (Ai:Mode=Fake): roteiro fixo e válido, sem rede e sem custo. Pra dev e testes.</summary>
public sealed class FakeLanguageModel : ILanguageModel
{
    public Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default)
    {
        if (request.Purpose == "onboarding-chat") return Task.FromResult(Interview(request));
        if (request.Purpose == "screen-recording") return Task.FromResult(Screen(request));
        if (request.Purpose == "media-describe")
            return Task.FromResult(new LanguageModelResult(JsonSerializer.Serialize(new
            {
                descricao = "Foto de teste do acervo.", tags = new[] { "teste", "acervo" },
                foco = new { x = 0.1, y = 0.2, largura = 0.8, altura = 0.5 },
                // Arquivo com "cpf" no pedido do teste vira dado real numa área (exercita o borrado sem IA de verdade).
                dadosPessoais = request.User.Contains("cpf", StringComparison.OrdinalIgnoreCase)
                    ? new { tipo = "real", quais = new[] { "CPF" }, areas = new object[] { new { x = 0.1, y = 0.1, largura = 0.5, altura = 0.2 } } }
                    : new { tipo = "nenhum", quais = Array.Empty<string>(), areas = Array.Empty<object>() },
            }), "fake", 0, 0, 0m));
        var ad = request.System.Contains("Anúncio: sem abertura", StringComparison.Ordinal);
        // Com acervo no prompt, o primeiro bloco usa a primeira mídia listada ("- <id>: foto: ...").
        var firstMedia = request.System.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("- ", StringComparison.Ordinal) && l.Contains(": foto:") || l.StartsWith("- ", StringComparison.Ordinal) && l.Contains(": vídeo:"));
        var mediaId = firstMedia is null ? "" : firstMedia[2..firstMedia.IndexOf(':')];
        object? opening = ad ? null : new { titulo = "Agenda no papel", subtitulo = "Tudo num lugar só", fala = "Agenda no papel perde paciente sem você perceber." };
        var blocks = ad
            ? new[]
            {
                new { titulo = "Paciente que falta custa caro", subtitulo = "", fala = "Paciente que falta é horário perdido.", cena = "Abrir a agenda do dia.", midia = mediaId, transicao = "slideleft", movimento = "pan-left", momento = 1, audio = "narracao", duracao = 0 },
                new { titulo = "Confirmação pelo WhatsApp", subtitulo = "Um toque pra confirmar", fala = "Na véspera, a confirmação sai pelo WhatsApp com um clique.", cena = "Clicar em confirmar.", midia = "", transicao = "fade", movimento = "zoom-in", momento = -1, audio = "narracao", duracao = 0 },
            }
            : new[]
            {
                new { titulo = "Agenda que se repete", subtitulo = "Marca uma vez", fala = "Você marca a sessão uma vez e o sistema repete nas próximas semanas, sem planilha e sem esquecer ninguém.", cena = "Abrir a agenda, criar sessão semanal.", midia = mediaId, transicao = "slideleft", movimento = "pan-left", momento = 1, audio = "narracao", duracao = 0 },
                new { titulo = "Evolução pronta", subtitulo = "Texto com nome e data", fala = "Depois do atendimento, a evolução vai direto pro prontuário com o nome do paciente e a data já preenchidos.", cena = "Abrir o prontuário e salvar a evolução.", midia = "", transicao = "fade", movimento = "zoom-in", momento = -1, audio = "narracao", duracao = 0 },
                new { titulo = "Confirmação no WhatsApp", subtitulo = "Quem vem amanhã", fala = "Na véspera, a confirmação vai pelo WhatsApp e você sabe quem vem antes de abrir a agenda.", cena = "Mostrar a lista de confirmações.", midia = "", transicao = "fade", movimento = "zoom-in", momento = -1, audio = "narracao", duracao = 0 },
            };
        var json = JsonSerializer.Serialize(new
        {
            nome = "roteiro-de-teste",
            abertura = opening,
            blocos = blocks,
            final = new { titulo = "Teste grátis", cta = "Link na bio", fala = ad ? "Testa três meses grátis, sem cartão." : "Feito pra quem atende com hora marcada. Três meses grátis, sem cartão." },
            ganchosAlternativos = new[] { "Quantas sessões você perdeu este mês?", "Sua agenda ainda é no papel?", "Paciente esqueceu de novo?" },
            ideiasEdicao = new[] { "Transições de deslizar entre as cenas", "Zoom no resultado no fim", "Cortes mais rápidos no começo" },
            formato = "explicativo", musicaClima = "calm corporate",
        });
        return Task.FromResult(new LanguageModelResult(json, "fake", 0, 0, 0m));
    }

    /// <summary>Gravação de mentira: clica no primeiro elemento e, a partir do terceiro passo, dá a cena por pronta.</summary>
    private static LanguageModelResult Screen(LanguageModelRequest request)
    {
        var done = request.User.Contains("Passo 3 ", StringComparison.Ordinal) || request.User.Contains("Passo 4 ", StringComparison.Ordinal);
        var first = request.User.Split('\n').FirstOrDefault(l => l.StartsWith("e1:", StringComparison.Ordinal)) is null ? "" : "e1";
        // Elemento com "CPF 123" na página de teste: simula paciente real na tela.
        var realData = request.User.Contains("CPF 123", StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new
        {
            acao = done ? "pronto" : "clicar", alvo = done ? "" : first, texto = "", motivo = realData ? "nome e CPF de paciente na lista" : done ? "cena mostrada" : "abrir a agenda",
            dadoPessoalReal = realData,
        });
        return new LanguageModelResult(json, "fake", 0, 0, 0m);
    }

    /// <summary>Entrevista de mentira: com o site lido, já propõe o cadastro; sem ele, pergunta o que a pessoa vende.</summary>
    private static LanguageModelResult Interview(LanguageModelRequest request)
    {
        var site = request.User.Contains("<conteudo_do_site>", StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new
        {
            resposta = site ? "Li o seu site. Confere se está certo e me diz pra quem é o produto?" : "Oi! O que você vende? Pode mandar o link do site também.",
            pronto = site,
            proposta = new
            {
                nome = site ? "Produto do site" : "", tipo = site ? "app" : "", site = "", app = "", resumo = site ? "Resumo lido do site." : "",
                publico = site ? "Profissionais de saúde" : "", recursos = site ? new[] { "Agenda", "Prontuário" } : Array.Empty<string>(),
                diferenciais = Array.Empty<string>(), oferta = "",
            },
        });
        return new LanguageModelResult(json, "fake", 0, 0, 0m);
    }
}

/// <summary>IA desligada (padrão): toda chamada avisa em vez de tentar.</summary>
public sealed class DisabledLanguageModel : ILanguageModel
{
    public Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default) =>
        throw new AiDisabledException();
}
