using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Ai;
using RootBoost.Application.Media;
using RootBoost.Application.Organic;
using RootBoost.Domain;

namespace RootBoost.Application.Studio;

public interface IContentIdeaStore
{
    Task<IReadOnlyList<ContentIdea>> ListAsync(string brandKey, CancellationToken ct = default);
    Task<ContentIdea?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ContentIdea idea, CancellationToken ct = default);
    Task SaveAsync(ContentIdea idea, CancellationToken ct = default);
    Task RemoveAsync(ContentIdea idea, CancellationToken ct = default);
    /// <summary>O tema da fila mais antigo de qualquer marca, ou null.</summary>
    Task<ContentIdea?> NextQueuedAsync(CancellationToken ct = default);
}

/// <summary>
/// Escreve o roteiro de um vídeo do Estúdio com tudo que a marca tem (perfil, produto, acervo liberado, histórico dos
/// vídeos). É o mesmo caminho do botão "Escrever roteiro", usado também pela fila da pauta.
/// </summary>
public sealed class StudioScripts
{
    private readonly IBrandProfileStore _profiles;
    private readonly ScriptWriter _writer;
    private readonly IBrandProductStore _products;
    private readonly IMediaAssetStore _assets;
    private readonly IOrganicPostStore _organic;
    private readonly TimeProvider _time;

    public StudioScripts(IBrandProfileStore profiles, ScriptWriter writer, IBrandProductStore products, IMediaAssetStore assets,
        IOrganicPostStore organic, TimeProvider time)
    {
        _profiles = profiles;
        _writer = writer;
        _products = products;
        _assets = assets;
        _organic = organic;
        _time = time;
    }

    public async Task<(ScriptWriter.Written? Ok, ScriptWriter.Failed? Error)> WriteAsync(CreativeJob job, string? instructions, CancellationToken ct = default)
    {
        var profile = await _profiles.GetAsync(job.BrandKey, ct);
        var policy = profile?.Brand.Privacy ?? PersonalDataPolicy.Blur;
        // Mídia com dado pessoal sem borrar (ou real, em marca só fictício) nem aparece pra IA.
        var library = (await _assets.ListByBrandAsync(job.BrandKey, 100, ct)).Where(m => m.BlockedReason(policy) is null && !m.Finished).ToList();
        var lessons = await ContentLessons.ForBrandAsync(_organic, job.BrandKey, _time.GetUtcNow(), ct);
        var result = await _writer.WriteAsync(job, profile, instructions, ct, await _products.GetAsync(job.BrandKey, ct), library, lessons);
        if (result.Ok is { } ok)
            job.SetScript(ok.Script.GetRawText(), ok.Lines.GetRawText(), _time.GetUtcNow(), ok.Warnings.Select(w => CreativeJob.ScriptWarningPrefix + w).ToList());
        return result;
    }
}

/// <summary>
/// Fila da pauta (17/11): pega o próximo tema da fila, cria o vídeo (explicativo, voz padrão da marca), escreve o
/// roteiro e manda renderizar. Um por vez, pra não gastar IA à toa nem empilhar render. Falha vira nota no tema.
/// </summary>
public sealed class PautaRunner
{
    private readonly IContentIdeaStore _ideas;
    private readonly ICreativeJobStore _jobs;
    private readonly StudioScripts _scripts;
    private readonly TimeProvider _time;
    private readonly ILogger<PautaRunner> _log;

    public PautaRunner(IContentIdeaStore ideas, ICreativeJobStore jobs, StudioScripts scripts, TimeProvider time, ILogger<PautaRunner> log)
    {
        _ideas = ideas;
        _jobs = jobs;
        _scripts = scripts;
        _time = time;
        _log = log;
    }

    /// <summary>Processa um tema. Devolve false quando a fila está vazia.</summary>
    public async Task<bool> RunOneAsync(CancellationToken ct = default)
    {
        var idea = await _ideas.NextQueuedAsync(ct);
        if (idea is null) return false;
        CreativeJob? job = null;
        try
        {
            job = new CreativeJob(idea.BrandKey, CreativeKind.Organic, idea.Request, null, _time.GetUtcNow(), VideoFormat.Explainer);
            if (idea.AutoVoice) job.EnableAutoVoice(_time.GetUtcNow());
            await _jobs.AddAsync(job, ct);
            var (ok, failed) = await _scripts.WriteAsync(job, null, ct);
            if (ok is null)
            {
                await _jobs.SaveAsync(job, ct);
                idea.Fail("a IA não acertou o roteiro: " + string.Join("; ", failed!.Errors.Take(3)), job.Id, _time.GetUtcNow());
            }
            else
            {
                job.QueueRender(_time.GetUtcNow());
                await _jobs.SaveAsync(job, ct);
                idea.Produced(job.Id, _time.GetUtcNow());
                _log.LogInformation("Pauta {Brand}: \"{Title}\" virou o vídeo {Job}.", idea.BrandKey, idea.Title, job.Id);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // IA desligada, sem crédito ou teto do mês: o motivo fica no tema pra pessoa ver na pauta.
            idea.Fail(ex.Message, job?.Id, _time.GetUtcNow());
            _log.LogWarning("Pauta {Brand}: \"{Title}\" falhou: {Error}", idea.BrandKey, idea.Title, ex.Message);
        }
        await _ideas.SaveAsync(idea, ct);
        return true;
    }
}
