using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using RootBoost.Application.Ai;

namespace RootBoost.Infrastructure.Ai;

/// <summary>Claude pelo SDK oficial, com resposta presa ao JSON Schema pedido (structured outputs).</summary>
public sealed class ClaudeLanguageModel : ILanguageModel
{
    private readonly AnthropicClient _client;
    private readonly AiOptions _options;

    public ClaudeLanguageModel(AiOptions options)
    {
        _options = options;
        _client = new AnthropicClient { ApiKey = options.ApiKey };
    }

    public async Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default)
    {
        var schema = request.Schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var message = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _options.ModelFor(request.Purpose),
            MaxTokens = 16000,
            System = request.System,
            Messages = [new UserMessageParam { Content = UserContent(request) }],
            OutputConfig = new OutputConfig { Effort = EffortOf(_options.Effort), Format = new JsonOutputFormat { Schema = schema } },
        }, ct);

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
            return Task.FromResult(new LanguageModelResult(JsonSerializer.Serialize(new { descricao = "Foto de teste do acervo.", tags = new[] { "teste", "acervo" } }), "fake", 0, 0, 0m));
        var ad = request.System.Contains("Anúncio: sem abertura", StringComparison.Ordinal);
        object? opening = ad ? null : new { titulo = "Agenda no papel", subtitulo = "Tudo num lugar só", fala = "Agenda no papel perde paciente sem você perceber." };
        var blocks = ad
            ? new[]
            {
                new { titulo = "Paciente que falta custa caro", subtitulo = "", fala = "Paciente que falta é horário perdido.", cena = "Abrir a agenda do dia." },
                new { titulo = "Confirmação pelo WhatsApp", subtitulo = "Um toque pra confirmar", fala = "Na véspera, a confirmação sai pelo WhatsApp com um clique.", cena = "Clicar em confirmar." },
            }
            : new[]
            {
                new { titulo = "Agenda que se repete", subtitulo = "Marca uma vez", fala = "Você marca a sessão uma vez e o sistema repete nas próximas semanas, sem planilha e sem esquecer ninguém.", cena = "Abrir a agenda, criar sessão semanal." },
                new { titulo = "Evolução pronta", subtitulo = "Texto com nome e data", fala = "Depois do atendimento, a evolução vai direto pro prontuário com o nome do paciente e a data já preenchidos.", cena = "Abrir o prontuário e salvar a evolução." },
                new { titulo = "Confirmação no WhatsApp", subtitulo = "Quem vem amanhã", fala = "Na véspera, a confirmação vai pelo WhatsApp e você sabe quem vem antes de abrir a agenda.", cena = "Mostrar a lista de confirmações." },
            };
        var json = JsonSerializer.Serialize(new
        {
            nome = "roteiro-de-teste",
            abertura = opening,
            blocos = blocks,
            final = new { titulo = "Teste grátis", cta = "Link na bio", fala = ad ? "Testa três meses grátis, sem cartão." : "Feito pra quem atende com hora marcada. Três meses grátis, sem cartão." },
            ganchosAlternativos = new[] { "Quantas sessões você perdeu este mês?", "Sua agenda ainda é no papel?", "Paciente esqueceu de novo?" },
        });
        return Task.FromResult(new LanguageModelResult(json, "fake", 0, 0, 0m));
    }

    /// <summary>Gravação de mentira: clica no primeiro elemento e, a partir do terceiro passo, dá a cena por pronta.</summary>
    private static LanguageModelResult Screen(LanguageModelRequest request)
    {
        var done = request.User.Contains("Passo 3 ", StringComparison.Ordinal) || request.User.Contains("Passo 4 ", StringComparison.Ordinal);
        var first = request.User.Split('\n').FirstOrDefault(l => l.StartsWith("e1:", StringComparison.Ordinal)) is null ? "" : "e1";
        var json = JsonSerializer.Serialize(new { acao = done ? "pronto" : "clicar", alvo = done ? "" : first, texto = "", motivo = done ? "cena mostrada" : "abrir a agenda" });
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
