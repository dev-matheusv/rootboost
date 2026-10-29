using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RootBoost.Api.Auth;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Application.UseCases;
using RootBoost.Domain;
using RootBoost.Infrastructure;
using RootBoost.Infrastructure.Persistence;

if (AuthSetup.TryRunHashPasswordCommand(args)) return;

var builder = WebApplication.CreateBuilder(args);

// Railway/Render/Fly inject the port via $PORT. Bind to it when present (local dev uses the default).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// All adapters wired here; behavior is config-driven (see Infrastructure.DependencyInjection).
builder.Services.AddInfrastructure(builder.Configuration, AppContext.BaseDirectory);

// CORS so the (separately hosted) landing pages can call the checkout endpoints.
// Set Cors:AllowedOrigins (comma-separated) in prod; empty = allow any (dev convenience).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (origins.Length == 0) p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

// Console (JWT) e scripts (API key) sob a política Admin.
builder.Services.AddConsoleAuth(builder.Configuration);
// Upload de mídia (RB-33): o formulário aceita até o limite da mídia (+1 MB de folga pros campos).
var mediaMaxBytes = builder.Configuration.GetValue<long?>("Media:MaxBytes") ?? 300L * 1024 * 1024;
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = mediaMaxBytes + 1024 * 1024);

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
AuthSetup.WarnIfAdminIsOpen(app);
app.MapAuthEndpoints();

// Dev convenience: when LANDING_DIR is set, serve that folder's static landing at the API root,
// so a local end-to-end test runs from a single origin (no CORS). Not used in production.
var landingDir = Environment.GetEnvironmentVariable("LANDING_DIR");
if (!string.IsNullOrWhiteSpace(landingDir) && Directory.Exists(landingDir))
{
    var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(landingDir);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
}

// Schema no boot: migrações do EF, iguais em produção, no dev local e nos testes (cria o banco se não existir).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<RootBoostDbContext>().Database.Migrate();
}

app.MapGet("/health", () => Results.Ok("ok"));

// Mídia pública (RB-33): o Instagram e o Facebook baixam daqui. Id aleatório (impossível de adivinhar) e é conteúdo
// feito pra publicar. Link estável: não expira como o link assinado do bucket.
app.MapGet("/media/{id:guid}/{name}", async (Guid id, string name, HttpContext ctx, RootBoost.Application.Media.IMediaAssetStore assets,
    RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null) return Results.NotFound();
    // Dado pessoal real ou na dúvida: o link público só entrega a versão borrada (ou nada, até alguém borrar).
    var key = asset.HidesOriginal ? asset.RedactedStorageKey : asset.StorageKey;
    if (key is null) return Results.NotFound();
    var stored = await storage.GetAsync(key, ct);
    if (stored is null) return Results.NotFound();
    ctx.Response.ContentLength = stored.Length;
    ctx.Response.Headers.CacheControl = asset.HidesOriginal ? "no-store" : "public, max-age=86400";
    return Results.Stream(stored.Content, asset.HidesOriginal ? "image/jpeg" : asset.ContentType);
});

// --- Landing view beacon (public): conta 1 visita por produto pra medir conversão. Sem PII. ---
// A landing dispara isto no load. Só contamos productKeys que existem no catálogo (evita lixo de
// bot com chave arbitrária). Analytics nunca derruba nada: erro vira log e segue.
app.MapPost("/track/view", async (TrackViewRequest req, IVisitStore visits, IProductCatalog catalog, ILoggerFactory lf, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ProductKey) || catalog.Find(req.ProductKey) is null)
        return Results.NoContent();
    try { await visits.RecordViewAsync(req.ProductKey, req.Lang, ct); }
    catch (Exception ex) { lf.CreateLogger("TrackView").LogWarning(ex, "view record failed"); }
    return Results.NoContent();
});

// --- Orders dashboard (protected by an API key header) -----------------------
// ?brand=medlycare filtra por marca; sem o parâmetro, todas.
app.MapGet("/orders", async (HttpContext ctx, IOrderRepository repo, CancellationToken ct) =>
{
    if (!TryReadBrand(ctx, out var brand)) return Results.BadRequest(new { error = "brand inválida" });
    var orders = await repo.ListAsync(200, ct, brand);
    return Results.Ok(orders.Select(OrderDto.From));
}).RequireAuthorization(AuthSetup.AdminPolicy);

// --- Conhecimento geral (Admin): o que a IA e a checklist usam. Leitura; mudar é PR no knowledge/. ---
app.MapGet("/admin/knowledge", (IKnowledgeBase kb) => Results.Ok(new
{
    playbook = "copy/playbook",
    documents = kb.Documents.Select(d => new
    {
        name = d.Name, version = d.Version, updatedAt = d.UpdatedAt, source = d.Source, notes = d.Notes,
        items = d.Items.GetArrayLength()
    })
})).RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/knowledge/{**name}", (string name, IKnowledgeBase kb) =>
{
    if (string.Equals(name, "copy/playbook", StringComparison.OrdinalIgnoreCase))
        return Results.Text(kb.Playbook, "text/markdown; charset=utf-8");
    var doc = kb.Find(name);
    return doc is null ? Results.NotFound() : Results.Json(doc.Raw);
}).RequireAuthorization(AuthSetup.AdminPolicy);

// Modo da publicação, pro Console avisar quando é teste (Social:Live=false) e não mentir quando ligar.
// --- Avisos de operação (RB-58). Abertos primeiro pelos mais recentes; dispensar fecha à mão. ---
app.MapGet("/admin/alerts", async (RootBoost.Application.Alerts.IAlertStore store, CancellationToken ct) =>
    Results.Ok((await store.ListOpenAsync(ct)).Select(a => new
    {
        id = a.Id, kind = a.Kind.ToString(), key = a.Key, brandKey = a.BrandKey, message = a.Message, count = a.Count,
        firstSeenAt = a.FirstSeenAt, lastSeenAt = a.LastSeenAt,
    }))).RequireAuthorization(AuthSetup.AdminPolicy);

app.MapPost("/admin/alerts/{id:guid}/dismiss", async (Guid id, System.Security.Claims.ClaimsPrincipal user,
    RootBoost.Application.Alerts.IAlertStore store, TimeProvider time, CancellationToken ct) =>
{
    var alert = await store.GetAsync(id, ct);
    if (alert is null) return Results.NotFound();
    alert.Resolve(user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.Identity?.Name ?? "console", time.GetUtcNow());
    await store.SaveAsync(alert, ct);
    return Results.NoContent();
}).RequireAuthorization(AuthSetup.AdminPolicy);

// Checagem da conexão com a Meta sob demanda (a mesma que roda 1x por dia). Só com Social:Live=true.
app.MapPost("/admin/alerts/check-meta", async (IServiceProvider sp, CancellationToken ct) =>
    sp.GetService<RootBoost.Infrastructure.Publishing.MetaConnectionCheck>() is { } check
        ? Results.Ok(await check.RunAsync(ct))
        : Results.BadRequest(new { error = "publicação real desligada (Social:Live=false): não há conta da Meta pra checar" }))
    .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/publications/mode", (IConfiguration cfg) =>
    Results.Ok(new { live = string.Equals(cfg["Social:Live"], "true", StringComparison.OrdinalIgnoreCase) }))
    .RequireAuthorization(AuthSetup.AdminPolicy);

// Roda o job de publicação agora (o agendador já roda a cada minuto; isto é pra teste e operação).
app.MapPost("/admin/publications/run", async (RootBoost.Application.Publishing.PublishDuePublications job, CancellationToken ct) =>
    Results.Ok(await job.RunAsync(ct))).RequireAuthorization(AuthSetup.AdminPolicy);

// --- Anúncios (RB-13): só leitura. Sincroniza agora e mostra as contas ligadas (sem token nenhum). ---
app.MapPost("/admin/ads/sync", async (int? days, RootBoost.Application.Ads.SyncAdInsights sync, CancellationToken ct) =>
    Results.Ok(await sync.RunAsync(days ?? RootBoost.Application.Ads.SyncAdInsights.DefaultDays, ct)))
    .RequireAuthorization(AuthSetup.AdminPolicy);

app.MapGet("/admin/ads/accounts", (RootBoost.Application.Ads.IAdAccountDirectory accounts, IConfiguration cfg) =>
    Results.Ok(new { reader = cfg["Ads:Reader"] ?? "None", accounts = accounts.All }))
    .RequireAuthorization(AuthSetup.AdminPolicy);

// Marca como teste um pedido que entrou antes da marcação automática (sandbox antigo). Só tira dos números;
// o pedido continua no histórico.
app.MapPost("/admin/orders/{paymentId}/mark-test", async (string paymentId, IOrderRepository orders, CancellationToken ct) =>
{
    var order = await orders.GetByPaymentIdAsync(paymentId, ct);
    if (order is null) return Results.NotFound();
    order.MarkAsTest();
    await orders.UpdateAsync(order, ct);
    return Results.Ok(OrderDto.From(order));
}).RequireAuthorization(AuthSetup.AdminPolicy);

// --- Marcas (Admin): a loja, o MedlyCare e quem mais entrar. ---
app.MapGet("/admin/brands", async (IBrandStore brands, CancellationToken ct) =>
    Results.Ok((await brands.ListAsync(ct)).Select(b => new { key = b.Key, name = b.Name, defaultLanguage = b.DefaultLanguage })))
    .RequireAuthorization(AuthSetup.AdminPolicy);

// --- Perfil da marca (Admin): voz, inventário de prova e frases reais. A IA só afirma o que está ativo aqui. ---
var brandProfile = app.MapGroup("/admin/brands/{key}").RequireAuthorization(AuthSetup.AdminPolicy);

brandProfile.MapGet("/profile", async (string key, IBrandProfileStore store, TimeProvider time, CancellationToken ct) =>
{
    var profile = await store.GetAsync(key.ToLowerInvariant(), ct);
    if (profile is null) return Results.NotFound();
    var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    return Results.Ok(new
    {
        brand = new { key = profile.Brand.Key, name = profile.Brand.Name, defaultLanguage = profile.Brand.DefaultLanguage },
        voice = profile.Voice,
        proof = profile.Proof.Select(p => new
        {
            id = p.Id, kind = p.Kind.ToString(), statement = p.Statement, source = p.Source,
            verifiedAt = p.VerifiedAt, reviewBy = p.ReviewBy, active = p.Active, needsReview = p.NeedsReview(today)
        }),
        phrases = profile.Phrases.Select(p => new { id = p.Id, text = p.Text, source = p.Source, collectedAt = p.CollectedAt })
    });
});

brandProfile.MapPost("/proof", async (string key, ProofInput input, IBrandProfileStore store, TimeProvider time, CancellationToken ct) =>
{
    if (await store.GetAsync(key.ToLowerInvariant(), ct) is null) return Results.NotFound();
    if (!TryBuildProof(key, input, time, out var item, out var error)) return Results.BadRequest(new { error });
    await store.AddProofAsync(item!, ct);
    return Results.Created($"/admin/brands/{item!.BrandKey}/profile", new { id = item.Id });
});

// Checklist automática (RB-18): roda antes de qualquer peça chegar ao humano. Não publica nada.
brandProfile.MapPost("/copy/check", async (string key, CopyCheckRequest req, IBrandProfileStore store, IKnowledgeBase kb, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "texto vazio" });
    var profile = await store.GetAsync(key.ToLowerInvariant(), ct);
    if (profile is null) return Results.NotFound();
    var result = RootBoost.Application.Copy.CopyChecklist.Check(
        new RootBoost.Application.Copy.CopyPiece(req.Text, req.Channel, req.Field), kb, profile);
    return Results.Ok(new
    {
        blocked = result.Blocked,
        findings = result.Findings.Select(f => new
        {
            id = f.Id, group = f.Group, status = f.Status.ToString(), blocking = f.Blocking, message = f.Message, fix = f.Fix
        })
    });
});

// --- Publicação própria (RB-21). Nada publica sem aprovação; Social:Live=false = publicador de mentira. ---
brandProfile.MapPost("/publications", async (string key, PublicationInput input, IBrandStore brands,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == brandKey)) return Results.NotFound();
    if (!Enum.TryParse<SocialChannel>(input.Channel, true, out var channel) || !Enum.IsDefined(channel))
        return Results.BadRequest(new { error = $"canal inválido: '{input.Channel}'" });
    try
    {
        var publication = new Publication(brandKey, channel, input.MediaUrl, input.Caption, input.ScheduledFor, time.GetUtcNow());
        await store.AddAsync(publication, ct);
        return Results.Created($"/admin/brands/{brandKey}/publications/{publication.Id}", PublicationDto.From(publication));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

brandProfile.MapGet("/publications", async (string key, DateTimeOffset? from, DateTimeOffset? to,
    RootBoost.Application.Publishing.IPublicationStore store, CancellationToken ct) =>
    Results.Ok((await store.ListByBrandAsync(key.ToLowerInvariant(), from, to, ct)).Select(PublicationDto.From)));

brandProfile.MapPut("/publications/{id:guid}/caption", async (string key, Guid id, CaptionInput input,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.EditCaption(input.Caption, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

brandProfile.MapPut("/publications/{id:guid}/schedule", async (string key, Guid id, ScheduleInput input,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.Reschedule(input.ScheduledFor, time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

brandProfile.MapPut("/publications/{id:guid}/media", async (string key, Guid id, MediaInput input,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.ChangeMedia(input.MediaUrl ?? "", time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

// Reels (RB-48): capa (JPEG do bucket ou quadro do vídeo) e se aparece também no feed. Mudou, pede aprovação de novo.
brandProfile.MapPut("/publications/{id:guid}/reel", async (string key, Guid id, ReelOptionsInput input,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    if (publication.Channel != SocialChannel.Instagram || !RootBoost.Infrastructure.Publishing.InstagramPublisher.IsVideo(publication.MediaUrl))
        return Results.BadRequest(new { error = "capa e feed só valem pra Reels (vídeo no Instagram)" });
    try { publication.SetReelOptions(input.CoverUrl, input.ThumbOffsetMs, input.ShareToFeed ?? true, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

// Canal manual (LinkedIn): depois de postar à mão, registra aqui (fica Publicado com o link).
brandProfile.MapPost("/publications/{id:guid}/mark-published", async (string key, Guid id, ManualPublishedInput input,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.MarkPublishedManually(input.Url, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

brandProfile.MapDelete("/publications/{id:guid}", async (string key, Guid id,
    RootBoost.Application.Publishing.IPublicationStore store, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    if (!publication.CanDelete) return Results.Conflict(new { error = $"publicação em {publication.Status} não pode ser apagada" });
    await store.RemoveAsync(publication, ct);
    return Results.NoContent();
});

brandProfile.MapPost("/publications/{id:guid}/approve", async (string key, Guid id, System.Security.Claims.ClaimsPrincipal user,
    RootBoost.Application.Publishing.ApprovePublication approve, RootBoost.Application.Publishing.IPublicationStore store, CancellationToken ct) =>
{
    var who = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.Identity?.Name ?? "desconhecido";
    var result = await approve.HandleAsync(key.ToLowerInvariant(), id, who, ct);
    if (result.Error == "publicação não encontrada") return Results.NotFound();
    var checklist = result.Checklist?.Findings.Select(f => new { id = f.Id, status = f.Status.ToString(), blocking = f.Blocking, message = f.Message, fix = f.Fix });
    if (!result.Approved) return Results.UnprocessableEntity(new { error = result.Error, checklist });
    return Results.Ok(new { publication = PublicationDto.From((await store.GetAsync(id, ct))!), checklist });
});

brandProfile.MapPost("/publications/{id:guid}/retry", async (string key, Guid id,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.Retry(time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

brandProfile.MapPost("/publications/{id:guid}/unapprove", async (string key, Guid id,
    RootBoost.Application.Publishing.IPublicationStore store, TimeProvider time, CancellationToken ct) =>
{
    var publication = await store.GetAsync(id, ct);
    if (publication is null || publication.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { publication.Unapprove(time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(publication, ct);
    return Results.Ok(PublicationDto.From(publication));
});

brandProfile.MapGet("/ads/summary", async (string key, int? days, RootBoost.Application.Ads.IAdInsightStore store,
    RootBoost.Application.Ads.AdsStopRule rule, TimeProvider time, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    var window = Math.Clamp(days ?? 7, 1, 90);
    var until = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var since = until.AddDays(-(window - 1));
    var rows = await store.ListAsync(brandKey, since, until, AdLevel.Campaign, ct);
    return Results.Ok(RootBoost.Application.Ads.AdsSummary.Build(brandKey, since, until, rows, rule));
});

// --- Mídia (RB-33): sobe pro bucket e devolve um link público estável, servido pela API. ---
static string MediaPublicUrl(RootBoost.Application.Media.MediaOptions options, HttpRequest req, MediaAsset a)
{
    var baseUrl = string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? $"{req.Scheme}://{req.Host}" : options.PublicBaseUrl.TrimEnd('/');
    return $"{baseUrl}/media/{a.Id:N}/{a.FileName}";
}

static object MediaDto(RootBoost.Application.Media.MediaOptions options, HttpRequest req, MediaAsset a) => new
{
    id = a.Id, brandKey = a.BrandKey, fileName = a.FileName, contentType = a.ContentType, sizeBytes = a.SizeBytes,
    isVideo = a.IsVideo, createdAt = a.CreatedAt, url = MediaPublicUrl(options, req, a), description = a.Description, tags = a.Tags,
    personalData = a.PersonalData.ToString(), personalDataKinds = a.PersonalDataKinds, redactionBoxes = a.RedactionBoxes,
    redacted = a.RedactedStorageKey is not null, hidesOriginal = a.HidesOriginal, finished = a.Finished, focus = a.Focus,
};

brandProfile.MapPost("/media", async (string key, IFormFile file, HttpRequest req, IBrandStore brands,
    RootBoost.Application.Media.UploadMedia upload, RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == brandKey)) return Results.NotFound();
    try
    {
        await using var stream = file.OpenReadStream();
        var asset = await upload.HandleAsync(brandKey, file.FileName, file.Length, stream, ct);
        return Results.Created(MediaPublicUrl(options, req, asset), MediaDto(options, req, asset));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(mediaMaxBytes + 1024 * 1024));

// Acervo (RB-16): a IA descreve a foto; a pessoa pode corrigir à mão.
brandProfile.MapPost("/media/{id:guid}/describe", async (string key, Guid id, HttpRequest req, IBrandStore brands,
    RootBoost.Application.Media.IMediaAssetStore assets, RootBoost.Application.Ai.MediaDescriber describer,
    RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    var brand = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == asset.BrandKey);
    try
    {
        if (!await describer.DescribeAsync(asset, brand, ct))
            return Results.BadRequest(new { error = "não consegui abrir essa foto (use JPG, PNG ou WebP de até 40 MB)" });
    }
    catch (RootBoost.Application.Ai.AiDisabledException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (RootBoost.Application.Ai.AiRequestFailedException ex) { return Results.Json(new { error = ex.Message }, statusCode: 502); }
    await assets.SaveAsync(asset, ct);
    return Results.Ok(MediaDto(options, req, asset));
});

// Original de item com dado pessoal: só com login (o link público entrega a versão borrada).
brandProfile.MapGet("/media/{id:guid}/original", async (string key, Guid id, RootBoost.Application.Media.IMediaAssetStore assets,
    RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    var stored = await storage.GetAsync(asset.StorageKey, ct);
    return stored is null ? Results.NotFound() : Results.Stream(stored.Content, asset.ContentType);
});

// A pessoa ajusta onde borrar (ou diz o que é: fictício, real, sem dado); a versão borrada é refeita na hora.
brandProfile.MapPut("/media/{id:guid}/redaction", async (string key, Guid id, RedactionInput input, HttpRequest req,
    RootBoost.Application.Media.IMediaAssetStore assets, RootBoost.Application.Ai.MediaDescriber describer,
    RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    if (asset.IsVideo) return Results.BadRequest(new { error = "borrado automático só em foto" });
    var status = asset.PersonalData;
    if (input.Status is { } raw && (!Enum.TryParse(raw, true, out status) || status == PersonalDataStatus.Unchecked))
        return Results.BadRequest(new { error = "use None, Fictional, Real ou Uncertain" });
    var boxes = (input.Boxes ?? []).Select(b => new RedactionBox(b.X, b.Y, b.W, b.H)).ToList();
    // Marcou área num item "sem dado": passa a ser dúvida (a pessoa viu algo que a IA não viu).
    if (input.Status is null && boxes.Count > 0 && status is PersonalDataStatus.None or PersonalDataStatus.Unchecked) status = PersonalDataStatus.Uncertain;
    try { asset.MarkPersonalData(status, asset.PersonalDataKinds, boxes); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await describer.RedactAsync(asset, null, ct);
    await assets.SaveAsync(asset, ct);
    return Results.Ok(MediaDto(options, req, asset));
});

// Vídeo pronto (reel finalizado) ou bruto: pronto fica fora dos trechos dos roteiros.
brandProfile.MapPut("/media/{id:guid}/finished", async (string key, Guid id, MediaFinishedInput input, HttpRequest req,
    RootBoost.Application.Media.IMediaAssetStore assets, RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    asset.MarkFinished(input.Finished);
    await assets.SaveAsync(asset, ct);
    return Results.Ok(MediaDto(options, req, asset));
});

brandProfile.MapGet("/privacy", async (string key, IBrandStore brands, CancellationToken ct) =>
    (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == key.ToLowerInvariant()) is { } b
        ? Results.Ok(new { policy = b.Privacy.ToString(), fictionalMarkers = b.FictionalMarkers })
        : Results.NotFound());

brandProfile.MapPut("/media/{id:guid}/description", async (string key, Guid id, MediaDescriptionInput input, HttpRequest req,
    RootBoost.Application.Media.IMediaAssetStore assets, RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    asset.Describe(input.Description, input.Tags);
    await assets.SaveAsync(asset, ct);
    return Results.Ok(MediaDto(options, req, asset));
});

// Vozes da marca (RB-59): gravar a própria voz (com autorização falada) ou escolher uma do banco; o worker cria no
// DMaker e devolve duas amostras pra ouvir. Gravação e amostras ficam privadas: só saem por aqui, com login.
brandProfile.MapGet("/voices/script", async (string key, string? person, IBrandStore brands, CancellationToken ct) =>
{
    var brand = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == key.ToLowerInvariant());
    if (brand is null) return Results.NotFound();
    var who = string.IsNullOrWhiteSpace(person) ? "(seu nome)" : person.Trim();
    return Results.Ok(new { consent = NarrationVoice.ConsentFor(who, brand.Name), reading = NarrationVoice.ReadingText });
});

brandProfile.MapGet("/voices", async (string key, RootBoost.Application.Studio.INarrationVoiceStore voices, CancellationToken ct) =>
    Results.Ok((await voices.ListByBrandAsync(key.ToLowerInvariant(), ct)).Select(NarrationVoiceDto.From)));

brandProfile.MapPost("/voices/clone", async (string key, [Microsoft.AspNetCore.Mvc.FromForm] VoiceCloneForm form, IBrandStore brands,
    RootBoost.Application.Studio.INarrationVoiceStore voices, RootBoost.Application.Media.IMediaStorage storage, TimeProvider time, CancellationToken ct) =>
{
    var brand = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == key.ToLowerInvariant());
    if (brand is null) return Results.NotFound();
    if (form.File is null || form.File.Length == 0) return Results.BadRequest(new { error = "gravação vazia" });
    if (form.File.Length > VoiceAudio.MaxBytes) return Results.BadRequest(new { error = "gravação maior que 15 MB" });
    var ext = Path.GetExtension(form.File.FileName).ToLowerInvariant();
    if (!VoiceAudio.Types.TryGetValue(ext, out var contentType)) return Results.BadRequest(new { error = "use áudio webm, ogg, m4a, mp4, mp3 ou wav" });
    if (!form.Consent) return Results.BadRequest(new { error = "a autorização é obrigatória" });
    try
    {
        var storageKey = $"voices/{brand.Key}/{Guid.NewGuid():N}/referencia{ext}";
        var voice = NarrationVoice.Clone(brand.Key, form.Name ?? "", storageKey, NarrationVoice.ReadingText, form.Person ?? "",
            NarrationVoice.ConsentFor(form.Person ?? "", brand.Name), time.GetUtcNow());
        await using (var stream = form.File.OpenReadStream())
            await storage.PutAsync(storageKey, stream, form.File.Length, contentType, ct);
        await voices.AddAsync(voice, ct);
        return Results.Created($"/admin/brands/{brand.Key}/voices/{voice.Id}", NarrationVoiceDto.From(voice));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(VoiceAudio.MaxBytes + 1024 * 1024));

brandProfile.MapPost("/voices/design", async (string key, VoiceDesignInput input, IBrandStore brands,
    RootBoost.Application.Studio.INarrationVoiceStore voices, TimeProvider time, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == brandKey)) return Results.NotFound();
    try
    {
        var voice = NarrationVoice.Design(brandKey, input.Name ?? "", input.Gender ?? "", input.Age ?? "", input.Pitch ?? "", time.GetUtcNow());
        await voices.AddAsync(voice, ct);
        return Results.Created($"/admin/brands/{brandKey}/voices/{voice.Id}", NarrationVoiceDto.From(voice));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

brandProfile.MapGet("/voices/{id:guid}/samples/{seed:int}", async (string key, Guid id, int seed,
    RootBoost.Application.Studio.INarrationVoiceStore voices, RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var voice = await voices.GetAsync(id, ct);
    var sample = voice is null || voice.BrandKey != key.ToLowerInvariant() ? null : voice.Samples.FirstOrDefault(x => x.Seed == seed);
    if (sample is null) return Results.NotFound();
    var obj = await storage.GetAsync(sample.StorageKey, ct);
    return obj is null ? Results.NotFound() : Results.Stream(obj.Content, obj.ContentType);
});

brandProfile.MapPost("/voices/{id:guid}/choose", async (string key, Guid id, VoiceChooseInput input,
    RootBoost.Application.Studio.INarrationVoiceStore voices, TimeProvider time, CancellationToken ct) =>
{
    var voice = await voices.GetAsync(id, ct);
    if (voice is null || voice.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { voice.Choose(input.Seed, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await voices.SaveAsync(voice, ct);
    return Results.Ok(NarrationVoiceDto.From(voice));
});

brandProfile.MapDelete("/voices/{id:guid}", async (string key, Guid id, RootBoost.Application.Studio.INarrationVoiceStore voices,
    RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var voice = await voices.GetAsync(id, ct);
    if (voice is null || voice.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    if (voice.Status == NarrationVoiceStatus.Processing) return Results.Conflict(new { error = "o worker está criando esta voz; espere terminar" });
    // Apaga gravação e amostras daqui. A voz no DMaker do PC some na próxima rodada do worker (ele confere a lista).
    foreach (var k in voice.Samples.Select(x => x.StorageKey).Append(voice.ReferenceStorageKey).OfType<string>())
        await storage.DeleteAsync(k, ct);
    await voices.RemoveAsync(voice, ct);
    return Results.NoContent();
});

brandProfile.MapGet("/media", async (string key, HttpRequest req, RootBoost.Application.Media.IMediaAssetStore assets,
    RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
    Results.Ok((await assets.ListByBrandAsync(key.ToLowerInvariant(), 200, ct)).Select(a => MediaDto(options, req, a))));

// Apagar só se nenhum post ainda não publicado usa o arquivo (senão o post quebraria no horário).
brandProfile.MapDelete("/media/{id:guid}", async (string key, Guid id, RootBoost.Application.Media.IMediaAssetStore assets,
    RootBoost.Application.Media.IMediaStorage storage, RootBoost.Application.Publishing.IPublicationStore publications, CancellationToken ct) =>
{
    var asset = await assets.GetAsync(id, ct);
    if (asset is null || asset.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    var inUse = (await publications.ListByBrandAsync(asset.BrandKey, null, null, ct))
        .Any(p => p.Status != PublicationStatus.Published && p.MediaUrl.Contains($"/media/{asset.Id:N}/", StringComparison.OrdinalIgnoreCase));
    if (inUse) return Results.Conflict(new { error = "arquivo usado num post ainda não publicado" });
    await storage.DeleteAsync(asset.StorageKey, ct);
    await assets.RemoveAsync(asset, ct);
    return Results.NoContent();
});

// --- Estúdio (RB-42): jobs de criativo. O Console cria e libera; o worker local (PC com DMaker) puxa e entrega. ---
brandProfile.MapPost("/creative-jobs", async (string key, CreativeJobInput input, IBrandStore brands,
    RootBoost.Application.Studio.ICreativeJobStore store, RootBoost.Application.Studio.INarrationVoiceStore voices, TimeProvider time, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == brandKey)) return Results.NotFound();
    if (!Enum.TryParse<CreativeKind>(input.Kind, true, out var kind) || !Enum.IsDefined(kind))
        return Results.BadRequest(new { error = $"formato inválido: '{input.Kind}' (use Organic, Ad ou Prospecting)" });
    try
    {
        // Voz cadastrada na plataforma só narra a própria marca (voz de pessoa nunca vai pra outro cliente).
        if (!string.IsNullOrWhiteSpace(input.VoiceKey) && await voices.FindByKeyAsync(input.VoiceKey.Trim(), ct) is { } v && v.BrandKey != brandKey)
            return Results.BadRequest(new { error = "esta voz é de outra marca" });
        var job = new CreativeJob(brandKey, kind, input.Theme, input.VoiceKey, time.GetUtcNow());
        await store.AddAsync(job, ct);
        return Results.Created($"/admin/brands/{brandKey}/creative-jobs/{job.Id}", CreativeJobDto.From(job));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Revisão da voz: ouvir (com login), escolher ou pedir outras duas.
brandProfile.MapGet("/creative-jobs/{id:guid}/voice/{seed:int}", async (string key, Guid id, int seed,
    RootBoost.Application.Studio.ICreativeJobStore store, RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    var take = job is null || job.BrandKey != key.ToLowerInvariant() ? null : job.VoiceTakes.FirstOrDefault(t => t.Seed == seed);
    if (take is null) return Results.NotFound();
    var obj = await storage.GetAsync(take.StorageKey, ct);
    return obj is null ? Results.NotFound() : Results.Stream(obj.Content, "audio/mp4");
});

brandProfile.MapPost("/creative-jobs/{id:guid}/voice/choose", async (string key, Guid id, VoiceChooseInput input,
    RootBoost.Application.Studio.ICreativeJobStore store, TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { job.ChooseVoice(input.Seed, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    return Results.Ok(CreativeJobDto.From(job));
});

brandProfile.MapPost("/creative-jobs/{id:guid}/voice/redo", async (string key, Guid id,
    RootBoost.Application.Studio.ICreativeJobStore store, TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try { job.RedoVoice(time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    return Results.Ok(CreativeJobDto.From(job));
});

// Retrato do estúdio pra marca: computadores ligados, posição de cada job dela na fila e o tempo médio de um render.
brandProfile.MapGet("/studio/status", async (string key, RootBoost.Application.Studio.IStudioStatusStore status,
    RootBoost.Application.Studio.ICreativeJobStore jobs, TimeProvider time, CancellationToken ct) =>
{
    var now = time.GetUtcNow();
    var workers = await status.ListWorkersAsync(ct);
    var queue = await status.QueueAsync(ct);
    var mine = (await jobs.ListByBrandAsync(key.ToLowerInvariant(), ct)).Where(j => j.Status == CreativeJobStatus.Queued).Select(j => j.Id).ToHashSet();
    return Results.Ok(new
    {
        // 3 min: o worker bate a cada rodada (60 s) e o progresso a cada 30 s; folga pra um passo lento.
        workersOnline = workers.Count(w => now - w.LastSeenAt < TimeSpan.FromMinutes(3)),
        lastSeenAt = workers.FirstOrDefault()?.LastSeenAt,
        averageRenderSeconds = await status.AverageRenderSecondsAsync(ct),
        queueSize = queue.Count,
        positions = queue.Select((id, i) => (id, i)).Where(x => mine.Contains(x.id)).ToDictionary(x => x.id.ToString(), x => x.i + 1),
    });
});

brandProfile.MapGet("/creative-jobs", async (string key, RootBoost.Application.Studio.ICreativeJobStore store, CancellationToken ct) =>
    Results.Ok((await store.ListByBrandAsync(key.ToLowerInvariant(), ct)).Select(CreativeJobDto.From)));

brandProfile.MapGet("/creative-jobs/{id:guid}", async (string key, Guid id, RootBoost.Application.Studio.ICreativeJobStore store, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    return job is null || job.BrandKey != key.ToLowerInvariant() ? Results.NotFound() : Results.Ok(CreativeJobDto.From(job));
});

// Roteiro e falas nos contratos do pacote; recusa com a lista do que não bate (contagem de falas, trechos, travessão).
brandProfile.MapPut("/creative-jobs/{id:guid}/script", async (string key, Guid id, ScriptInput input,
    RootBoost.Application.Studio.ICreativeJobStore store, IBrandProfileStore profiles, IKnowledgeBase knowledge,
    TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    var errors = RootBoost.Application.Studio.ScriptContract.Validate(input.Roteiro, input.Falas);
    if (errors.Count > 0) return Results.UnprocessableEntity(new { error = "roteiro ou falas fora do contrato", errors });
    // Portões do roteiro (RB-44): checklist de copy da marca, duração do formato e número em algarismo.
    var review = RootBoost.Application.Studio.ScriptGates.Review(job.Kind, input.Roteiro, input.Falas, knowledge,
        await profiles.GetAsync(job.BrandKey, ct));
    if (review.Errors.Count > 0) return Results.UnprocessableEntity(new { error = "a checklist de copy bloqueou o roteiro", errors = review.Errors });
    var warnings = review.Warnings.Select(w => CreativeJob.ScriptWarningPrefix + w).ToList();
    try { job.SetScript(input.Roteiro.GetRawText(), input.Falas.GetRawText(), time.GetUtcNow(), warnings); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    return Results.Ok(CreativeJobDto.From(job));
});

// A IA escreve roteiro e falas com o perfil, as provas e o playbook (RB-45); passa pelos mesmos portões.
brandProfile.MapPost("/creative-jobs/{id:guid}/write-script", async (string key, Guid id, WriteScriptInput? input,
    RootBoost.Application.Studio.ICreativeJobStore store, IBrandProfileStore profiles, RootBoost.Application.Ai.ScriptWriter writer,
    RootBoost.Application.Ai.IBrandProductStore products, RootBoost.Application.Media.IMediaAssetStore assets, TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    try
    {
        var profile = await profiles.GetAsync(job.BrandKey, ct);
        var policy = profile?.Brand.Privacy ?? PersonalDataPolicy.Blur;
        // Mídia com dado pessoal sem borrar (ou real, em marca só fictício) nem aparece pra IA.
        var library = (await assets.ListByBrandAsync(job.BrandKey, 100, ct)).Where(m => m.BlockedReason(policy) is null && !m.Finished).ToList();
        var (ok, failed) = await writer.WriteAsync(job, profile, input?.Instructions, ct, await products.GetAsync(job.BrandKey, ct), library);
        if (ok is null) return Results.UnprocessableEntity(new { error = "a IA não acertou o roteiro em duas tentativas", errors = failed!.Errors });
        job.SetScript(ok.Script.GetRawText(), ok.Lines.GetRawText(), time.GetUtcNow(),
            ok.Warnings.Select(w => CreativeJob.ScriptWarningPrefix + w).ToList());
        await store.SaveAsync(job, ct);
        return Results.Ok(new { job = CreativeJobDto.From(job), hooks = ok.Hooks, costUsd = ok.CostUsd });
    }
    catch (RootBoost.Application.Ai.AiDisabledException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// --- Produto da marca (RB-60): a IA conduz a conversa e propõe; a pessoa confere e salva. ---
brandProfile.MapGet("/product", async (string key, RootBoost.Application.Ai.IBrandProductStore products, CancellationToken ct) =>
    await products.GetAsync(key.ToLowerInvariant(), ct) is { } p ? Results.Ok(ProductDto.From(p)) : Results.NoContent());

brandProfile.MapPut("/product", async (string key, ProductInput input, IBrandStore brands,
    RootBoost.Application.Ai.IBrandProductStore products, TimeProvider time, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == brandKey)) return Results.NotFound();
    var current = await products.GetAsync(brandKey, ct);
    var product = current ?? new BrandProduct(brandKey, time.GetUtcNow());
    try
    {
        product.Update(input.Name, input.Kind, input.Url, input.AppUrl, input.Summary ?? string.Empty, input.Audience ?? string.Empty,
            input.Features ?? [], input.Differentials ?? [], input.Offer, time.GetUtcNow());
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    if (current is null) await products.AddAsync(product, ct);
    else await products.SaveAsync(product, ct);
    return Results.Ok(ProductDto.From(product));
});

brandProfile.MapPost("/onboarding/chat", async (string key, OnboardingChatInput input, IBrandStore brands,
    RootBoost.Application.Ai.IBrandProductStore products, RootBoost.Application.Ai.ProductInterviewer interviewer, CancellationToken ct) =>
{
    var brandKey = key.ToLowerInvariant();
    var brand = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == brandKey);
    if (brand is null) return Results.NotFound();
    var turns = (input.Messages ?? []).Select(m => new RootBoost.Application.Ai.ChatTurn(m.Role == "user" ? "user" : "assistant",
        (m.Text ?? string.Empty).Length > 4000 ? m.Text![..4000] : m.Text ?? string.Empty)).ToList();
    try
    {
        var reply = await interviewer.NextAsync(brandKey, brand.Name, await products.GetAsync(brandKey, ct), turns, ct);
        return Results.Ok(reply);
    }
    catch (RootBoost.Application.Ai.AiDisabledException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// Custo da IA (todas as marcas): total, por finalidade e por marca. Pra acompanhar se a plataforma está ficando cara.
app.MapGet("/admin/ai/usage", async (int? days, RootBoost.Application.Ai.IAiUsageStore usage, TimeProvider time, CancellationToken ct) =>
{
    var window = Math.Clamp(days ?? 30, 1, 365);
    var rows = await usage.ListSinceAsync(time.GetUtcNow().AddDays(-window), ct);
    static List<RootBoost.Application.Ai.AiUsageLine> By(IEnumerable<RootBoost.Application.Ai.AiUsageEntry> r, Func<RootBoost.Application.Ai.AiUsageEntry, string> key) =>
        r.GroupBy(key).Select(g => new RootBoost.Application.Ai.AiUsageLine(g.Key, g.Sum(x => x.CostUsd), g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens)))
         .OrderByDescending(l => l.CostUsd).ToList();
    return Results.Ok(new { days = window, summary = new RootBoost.Application.Ai.AiUsageSummary(rows.Sum(r => r.CostUsd), rows.Count,
        By(rows, r => r.Purpose), By(rows, r => r.BrandKey ?? "geral")) });
}).RequireAuthorization(AuthSetup.AdminPolicy);

brandProfile.MapGet("/ai/status", (RootBoost.Application.Ai.AiOptions ai) =>
    Results.Ok(new { mode = ai.Mode.ToString(), enabled = ai.Mode == RootBoost.Application.Ai.AiMode.Fake ||
        (ai.Mode == RootBoost.Application.Ai.AiMode.Claude && !string.IsNullOrWhiteSpace(ai.ApiKey)) }));

brandProfile.MapPost("/creative-jobs/{id:guid}/render", async (string key, Guid id,
    RootBoost.Application.Studio.ICreativeJobStore store, RootBoost.Application.Media.IMediaAssetStore assets, IBrandStore brands,
    TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    // Trava de dado pessoal: mídia do acervo que não pode entrar impede o render, com o motivo.
    var policy = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == job.BrandKey)?.Privacy ?? PersonalDataPolicy.Blur;
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(job.Script ?? "", "acervo-([0-9a-f]{32})"))
        if (Guid.TryParseExact(m.Groups[1].Value, "N", out var mid) && await assets.GetAsync(mid, ct) is { } a && a.BlockedReason(policy) is { } why)
            return Results.BadRequest(new { error = $"a mídia {a.FileName} {why}; borre no Acervo ou troque a cena" });
    try { job.QueueRender(time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    return Results.Ok(CreativeJobDto.From(job));
});

// Revisado: vira rascunhos de publicação. Vídeo do Instagram (com a capa, se veio) e o outro vídeo pra Facebook e
// LinkedIn, do mesmo jeito do "Importar reel pronto". Só cria o canal que tiver legenda; nada é aprovado aqui.
brandProfile.MapPost("/creative-jobs/{id:guid}/publications", async (string key, Guid id, JobPublicationsInput input,
    RootBoost.Application.Studio.ICreativeJobStore jobs, RootBoost.Application.Publishing.IPublicationStore publications,
    TimeProvider time, CancellationToken ct) =>
{
    var job = await jobs.GetAsync(id, ct);
    if (job is null || job.BrandKey != key.ToLowerInvariant()) return Results.NotFound();
    if (job.Status != CreativeJobStatus.Review) return Results.BadRequest(new { error = $"só job em revisão vira post (status atual: {job.Status})" });
    var outputs = job.Outputs;
    string? Url(string role) => outputs.FirstOrDefault(o => string.Equals(o.Role, role, StringComparison.OrdinalIgnoreCase))?.Url;
    var igVideo = Url("video-instagram");
    var siteVideo = Url("video") ?? igVideo;
    var cover = Url("cover-instagram");

    var plan = new List<(SocialChannel Channel, string Media, string Caption)>();
    if (!string.IsNullOrWhiteSpace(input.Instagram))
    {
        if (igVideo is null) return Results.BadRequest(new { error = "job sem o vídeo do Instagram (papel video-instagram)" });
        plan.Add((SocialChannel.Instagram, igVideo, input.Instagram));
    }
    if (!string.IsNullOrWhiteSpace(input.Facebook))
    {
        if (siteVideo is null) return Results.BadRequest(new { error = "job sem vídeo pro Facebook (papel video)" });
        plan.Add((SocialChannel.Facebook, siteVideo, input.Facebook));
    }
    if (!string.IsNullOrWhiteSpace(input.LinkedIn))
    {
        if (siteVideo is null) return Results.BadRequest(new { error = "job sem vídeo pro LinkedIn (papel video)" });
        plan.Add((SocialChannel.LinkedIn, siteVideo, input.LinkedIn));
    }
    if (plan.Count == 0) return Results.BadRequest(new { error = "informe a legenda de pelo menos um canal" });

    var now = time.GetUtcNow();
    var created = new List<Publication>();
    try
    {
        foreach (var (channel, media, caption) in plan)
        {
            var publication = new Publication(job.BrandKey, channel, media, caption, input.ScheduledFor, now);
            if (channel == SocialChannel.Instagram && cover is not null) publication.SetReelOptions(cover, null, true, now);
            created.Add(publication);
        }
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
    foreach (var publication in created) await publications.AddAsync(publication, ct);
    job.MarkDone(now);
    await jobs.SaveAsync(job, ct);
    return Results.Ok(new { job = CreativeJobDto.From(job), publications = created.Select(PublicationDto.From) });
});

// --- Push da PWA (RB-37): o Console inscreve o aparelho e o servidor avisa pendência e problema. ---
var push = app.MapGroup("/admin/push").RequireAuthorization(AuthSetup.AdminPolicy);

push.MapGet("/config", (RootBoost.Application.Push.PushOptions options) =>
    Results.Ok(new { enabled = options.Enabled, publicKey = options.Enabled ? options.PublicKey : null }));

push.MapPost("/devices", async (PushDeviceInput input, System.Security.Claims.ClaimsPrincipal user,
    RootBoost.Application.Push.IPushDeviceStore devices, RootBoost.Application.Push.PushOptions options, TimeProvider time, CancellationToken ct) =>
{
    if (!options.Enabled) return Results.Conflict(new { error = "push desligado no servidor" });
    var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.Identity?.Name ?? string.Empty;
    try
    {
        if (await devices.GetByEndpointAsync(input.Endpoint, ct) is { } existing)
        {
            existing.Renew(input.Keys.P256dh, input.Keys.Auth, email);
            await devices.SaveAsync(existing, ct);
        }
        else await devices.AddAsync(new PushDevice(input.Endpoint, input.Keys.P256dh, input.Keys.Auth, email, time.GetUtcNow()), ct);
        return Results.NoContent();
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

push.MapPost("/devices/remove", async (PushRemoveInput input, RootBoost.Application.Push.IPushDeviceStore devices, CancellationToken ct) =>
{
    if (await devices.GetByEndpointAsync(input.Endpoint, ct) is { } d) await devices.RemoveAsync(d, ct);
    return Results.NoContent();
});

// Roda o lembrete de aprovação na hora (o agendador roda a cada 15 min). Útil pra testar e no Console.
push.MapPost("/remind", async (RootBoost.Application.Push.RemindPendingApprovals remind, CancellationToken ct) =>
    Results.Ok(new { reminded = await remind.RunAsync(ct) }));

push.MapPost("/test", async (RootBoost.Application.Push.PushNotifier notifier, CancellationToken ct) =>
{
    if (!notifier.Enabled) return Results.Conflict(new { error = "push desligado no servidor" });
    var sent = await notifier.SendAsync(new RootBoost.Application.Push.PushMessage(
        "Teste do Console", "Os avisos estão chegando neste aparelho.", "/", "teste"), ct);
    return Results.Ok(new { sent });
});

// Rotas do worker: chave própria no cabeçalho X-Worker-Key (Studio:WorkerKey). Sem chave configurada, nada abre.
var worker = app.MapGroup("/worker").AddEndpointFilter(async (ctx, next) =>
{
    var options = ctx.HttpContext.RequestServices.GetRequiredService<RootBoost.Application.Studio.StudioOptions>();
    if (string.IsNullOrWhiteSpace(options.WorkerKey)) return Results.NotFound();
    var sent = ctx.HttpContext.Request.Headers["X-Worker-Key"].ToString();
    var ok = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(sent), System.Text.Encoding.UTF8.GetBytes(options.WorkerKey));
    return ok ? await next(ctx) : Results.Unauthorized();
});

worker.MapPost("/jobs/claim", async (WorkerClaimInput input, HttpRequest req, RootBoost.Application.Studio.ICreativeJobStore store, IBrandStore brands,
    RootBoost.Application.Studio.StudioOptions options, RootBoost.Application.Media.IMediaAssetStore assets,
    RootBoost.Application.Media.MediaOptions mediaOptions, RootBoost.Application.Studio.INarrationVoiceStore voices, TimeProvider time, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.WorkerId)) return Results.BadRequest(new { error = "workerId obrigatório" });
    var job = await store.ClaimNextAsync(input.WorkerId, time.GetUtcNow(), options.Lease, ct);
    if (job is null) return Results.NoContent();
    // Mídias do acervo citadas no roteiro ("acervo-<id>"): o worker baixa pelo link público (que entrega a versão borrada).
    var privacy = (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == job.BrandKey)?.Privacy ?? PersonalDataPolicy.Blur;
    var media = new List<WorkerMediaDto>();
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(job.Script ?? "", "acervo-([0-9a-f]{32})"))
    {
        if (!Guid.TryParseExact(m.Groups[1].Value, "N", out var mid) || media.Any(x => x.Id == m.Groups[1].Value)) continue;
        if (await assets.GetAsync(mid, ct) is not { } a || a.BrandKey != job.BrandKey) continue; // apagada: o worker acusa
        // Barrada pela proteção de dado pessoal (inclusive numa nova tentativa): falha já, com o motivo e o caminho.
        if (a.BlockedReason(privacy) is { } why)
        {
            job.Fail(input.WorkerId, $"a foto {a.FileName} está bloqueada pela proteção de dado pessoal ({why}). " +
                "Resolva no Acervo e peça outra versão do roteiro.", retry: false, time.GetUtcNow());
            await store.SaveAsync(job, ct);
            return Results.NoContent();
        }
        media.Add(new WorkerMediaDto(m.Groups[1].Value, MediaPublicUrl(mediaOptions, req, a), a.IsVideo, a.Focus));
    }
    // Voz da plataforma com semente escolhida pela pessoa: vira a semente padrão do job (o worker soma as tentativas).
    var seed = job.Seed;
    if (seed is null && job.VoiceKey is { } vk && await voices.FindByKeyAsync(vk, ct) is { BrandKey: var vb, ChosenSeed: { } chosen } && vb == job.BrandKey)
        seed = chosen;
    return Results.Ok(WorkerJobDto.From(job) with { Media = media, Seed = seed });
});

worker.MapPost("/jobs/{id:guid}/progress", async (Guid id, WorkerProgressInput input, RootBoost.Application.Studio.ICreativeJobStore store,
    RootBoost.Application.Studio.StudioOptions options, RootBoost.Application.Studio.IStudioStatusStore status, TimeProvider time, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(input.WorkerId) && input.WorkerId.Length <= 100) await status.BeatAsync(input.WorkerId, input.Note, time.GetUtcNow(), ct);
    var job = await store.GetAsync(id, ct);
    if (job is null) return Results.NotFound();
    try { job.ReportProgress(input.WorkerId, input.Percent, input.Note, time.GetUtcNow(), options.Lease); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.Conflict(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    return Results.NoContent();
});

worker.MapPost("/jobs/{id:guid}/deliver", async (Guid id, WorkerDeliverInput input, RootBoost.Application.Studio.ICreativeJobStore store,
    RootBoost.Application.Push.PushNotifier notifier, TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null) return Results.NotFound();
    try
    {
        var outputs = (input.Outputs ?? []).Select(o => new CreativeOutput(o.Role ?? string.Empty, o.Url ?? string.Empty)).ToList();
        job.Deliver(input.WorkerId, outputs, input.Warnings, input.Seed, time.GetUtcNow());
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    // Vídeo pronto: avisa no celular pra revisar (aprovação continua no Console).
    await notifier.SendAsync(new RootBoost.Application.Push.PushMessage(
        "Vídeo pronto pra revisar", job.Theme, "/studio", $"job:{job.Id:N}"), ct);
    return Results.Ok(CreativeJobDto.From(job));
});

// Revisão da voz (29/10): o worker sobe as versões (take-<semente>, com unmatched-<semente>) e o job espera a escolha.
worker.MapPost("/jobs/{id:guid}/voice", async (Guid id, HttpRequest req, RootBoost.Application.Studio.ICreativeJobStore store,
    RootBoost.Application.Media.IMediaStorage storage, RootBoost.Application.Push.PushNotifier notifier, TimeProvider time, CancellationToken ct) =>
{
    var form = await req.ReadFormAsync(ct);
    var workerId = form["workerId"].ToString();
    var job = await store.GetAsync(id, ct);
    if (job is null) return Results.NotFound();
    if (job.Status != CreativeJobStatus.Rendering || job.WorkerId != workerId) return Results.Conflict(new { error = "este job não está com este worker" });
    var takes = new List<VoiceTake>();
    foreach (var f in form.Files)
    {
        if (!f.Name.StartsWith("take-", StringComparison.Ordinal) || !int.TryParse(f.Name[5..], out var seed) || seed is < 1 or > 9999) continue;
        if (f.Length > VoiceAudio.MaxBytes || Path.GetExtension(f.FileName).ToLowerInvariant() != ".m4a")
            return Results.BadRequest(new { error = $"versão de voz inválida: {f.FileName}" });
        var key = $"studio/{job.BrandKey}/{job.Id:N}/voz-{seed}.m4a";
        await using (var stream = f.OpenReadStream()) await storage.PutAsync(key, stream, f.Length, "audio/mp4", ct);
        _ = int.TryParse(form[$"unmatched-{seed}"].ToString(), out var unmatched);
        takes.Add(new VoiceTake(seed, key, Math.Max(0, unmatched)));
    }
    try { job.DeliverVoice(workerId, takes, time.GetUtcNow()); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    await notifier.SendAsync(new RootBoost.Application.Push.PushMessage("Voz pronta pra ouvir", job.Theme, "/studio", $"voz:{job.Id:N}"), ct);
    return Results.Ok(CreativeJobDto.From(job));
}).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4 * VoiceAudio.MaxBytes));

worker.MapPost("/jobs/{id:guid}/fail", async (Guid id, WorkerFailInput input, RootBoost.Application.Studio.ICreativeJobStore store,
    RootBoost.Application.Push.PushNotifier notifier, TimeProvider time, CancellationToken ct) =>
{
    var job = await store.GetAsync(id, ct);
    if (job is null) return Results.NotFound();
    try { job.Fail(input.WorkerId, input.Error ?? string.Empty, input.Retry, time.GetUtcNow()); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.Conflict(new { error = ex.Message }); }
    await store.SaveAsync(job, ct);
    // Falhou de vez (sem nova tentativa): avisa no celular com o motivo.
    if (job.Status == CreativeJobStatus.Failed)
        await notifier.SendAsync(new RootBoost.Application.Push.PushMessage("Vídeo não saiu", $"{job.Theme}: {job.Error}", "/studio", $"job:{job.Id:N}"), ct);
    return Results.Ok(CreativeJobDto.From(job));
});

// Sinal de vida: o worker bate a cada rodada (e cada progresso conta como batida).
worker.MapPost("/heartbeat", async (WorkerProgressInput input, RootBoost.Application.Studio.IStudioStatusStore status, TimeProvider time, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.WorkerId) || input.WorkerId.Length > 100) return Results.BadRequest(new { error = "workerId obrigatório" });
    await status.BeatAsync(input.WorkerId, input.Note, time.GetUtcNow(), ct);
    return Results.NoContent();
});

// Vozes (RB-59): o worker pega a próxima, baixa a gravação por aqui (nunca por link público), cria no DMaker e
// devolve as amostras.
worker.MapPost("/voices/claim", async (WorkerClaimInput input, RootBoost.Application.Studio.INarrationVoiceStore voices,
    RootBoost.Application.Studio.StudioOptions options, TimeProvider time, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.WorkerId)) return Results.BadRequest(new { error = "workerId obrigatório" });
    var v = await voices.ClaimNextAsync(input.WorkerId, time.GetUtcNow(), options.Lease, ct);
    return v is null ? Results.NoContent() : Results.Ok(new
    {
        v.Id, v.BrandKey, v.Key, v.Name, Kind = v.Kind.ToString(), v.Instruct, v.ReferenceText, v.Attempts,
        HasReference = v.ReferenceStorageKey is not null,
    });
});

worker.MapGet("/voices/{id:guid}/reference", async (Guid id, string workerId, RootBoost.Application.Studio.INarrationVoiceStore voices,
    RootBoost.Application.Media.IMediaStorage storage, CancellationToken ct) =>
{
    var v = await voices.GetAsync(id, ct);
    if (v is null || v.Status != NarrationVoiceStatus.Processing || v.WorkerId != workerId || v.ReferenceStorageKey is null) return Results.NotFound();
    var obj = await storage.GetAsync(v.ReferenceStorageKey, ct);
    return obj is null ? Results.NotFound() : Results.Stream(obj.Content, obj.ContentType, Path.GetFileName(v.ReferenceStorageKey));
});

worker.MapPost("/voices/{id:guid}/complete", async (Guid id, HttpRequest req, RootBoost.Application.Studio.INarrationVoiceStore voices,
    RootBoost.Application.Media.IMediaStorage storage, TimeProvider time, CancellationToken ct) =>
{
    var form = await req.ReadFormAsync(ct);
    var workerId = form["workerId"].ToString();
    var v = await voices.GetAsync(id, ct);
    if (v is null) return Results.NotFound();
    if (v.Status != NarrationVoiceStatus.Processing || v.WorkerId != workerId) return Results.Conflict(new { error = "esta voz não está com este worker" });
    var samples = new List<VoiceSample>();
    foreach (var f in form.Files)
    {
        if (!f.Name.StartsWith("sample-", StringComparison.Ordinal) || !int.TryParse(f.Name[7..], out var seed) || seed is < 1 or > 9999) continue;
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        if (!VoiceAudio.Types.TryGetValue(ext, out var type) || f.Length > VoiceAudio.MaxBytes) return Results.BadRequest(new { error = $"amostra inválida: {f.FileName}" });
        var sk = $"voices/{v.BrandKey}/{v.Id:N}/amostra-{seed}{ext}";
        await using (var stream = f.OpenReadStream()) await storage.PutAsync(sk, stream, f.Length, type, ct);
        samples.Add(new VoiceSample(seed, sk));
    }
    try { v.Complete(workerId, samples, time.GetUtcNow()); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    await voices.SaveAsync(v, ct);
    return Results.Ok(NarrationVoiceDto.From(v));
}).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(3 * VoiceAudio.MaxBytes));

worker.MapPost("/voices/{id:guid}/fail", async (Guid id, WorkerFailInput input, RootBoost.Application.Studio.INarrationVoiceStore voices,
    TimeProvider time, CancellationToken ct) =>
{
    var v = await voices.GetAsync(id, ct);
    if (v is null) return Results.NotFound();
    try { v.Fail(input.WorkerId ?? "", input.Error ?? "", input.Retry, time.GetUtcNow()); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    await voices.SaveAsync(v, ct);
    return Results.NoContent();
});

// Vozes que existem na plataforma, com a marca dona e a semente escolhida: o worker usa pra travar a marca e a semente.
worker.MapGet("/voices/keys", async (RootBoost.Application.Studio.INarrationVoiceStore voices, IBrandStore brands, CancellationToken ct) =>
{
    var keys = new List<object>();
    foreach (var b in await brands.ListAsync(ct))
        keys.AddRange((await voices.ListByBrandAsync(b.Key, ct)).Select(v => new { v.Key, v.BrandKey, v.ChosenSeed, Status = v.Status.ToString() }));
    return Results.Ok(keys);
});

// Gravação de tela (RB-50): o worker manda a foto e os elementos; a IA devolve o próximo passo. A chave fica aqui.
worker.MapPost("/ai/next-action", async (ScreenStateInput input, RootBoost.Application.Ai.ScreenAgent agent, IBrandStore brands, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Scene) || string.IsNullOrWhiteSpace(input.Image) || input.Image.Length > 2_000_000)
        return Results.BadRequest(new { error = "cena e foto (JPEG base64, até ~1,5 MB) são obrigatórias" });
    try
    {
        var state = new RootBoost.Application.Ai.ScreenState(input.BrandKey ?? string.Empty, input.Scene, input.Url ?? string.Empty,
            (input.Elements ?? []).Select(e => new RootBoost.Application.Ai.ScreenElement(e.Id ?? string.Empty, e.Role ?? string.Empty, e.Text ?? string.Empty)).ToList(),
            input.History ?? [], Math.Max(0, input.Step), input.Image,
            (await brands.ListAsync(ct)).FirstOrDefault(b => b.Key == input.BrandKey?.ToLowerInvariant())?.FictionalMarkers);
        return Results.Ok(await agent.NextAsync(state, ct));
    }
    catch (RootBoost.Application.Ai.AiDisabledException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// O worker sobe vídeo, capa e grade pelo mesmo bucket do Console e recebe o link público pra entregar no job.
worker.MapPost("/media/{brandKey}", async (string brandKey, IFormFile file, HttpRequest req, IBrandStore brands,
    RootBoost.Application.Media.UploadMedia upload, RootBoost.Application.Media.IMediaAssetStore assets, RootBoost.Application.Media.MediaOptions options, CancellationToken ct) =>
{
    var key = brandKey.ToLowerInvariant();
    if (!(await brands.ListAsync(ct)).Any(b => b.Key == key)) return Results.NotFound();
    try
    {
        await using var stream = file.OpenReadStream();
        var asset = await upload.HandleAsync(key, file.FileName, file.Length, stream, ct);
        if (asset.IsVideo)
        {
            asset.MarkFinished(true); // entrega do estúdio: reel pronto, nunca trecho de outro vídeo
            await assets.SaveAsync(asset, ct);
        }
        return Results.Created(MediaPublicUrl(options, req, asset), MediaDto(options, req, asset));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(mediaMaxBytes + 1024 * 1024));

brandProfile.MapPost("/proof/{id:guid}/retire", async (string key, Guid id, IBrandProfileStore store, CancellationToken ct) =>
    await store.RetireProofAsync(key.ToLowerInvariant(), id, ct) ? Results.NoContent() : Results.NotFound());

brandProfile.MapPost("/phrases", async (string key, PhraseInput input, IBrandProfileStore store, CancellationToken ct) =>
{
    if (await store.GetAsync(key.ToLowerInvariant(), ct) is null) return Results.NotFound();
    try
    {
        var phrase = new CustomerPhrase(key, input.Text, input.Source, input.CollectedAt);
        await store.AddPhraseAsync(phrase, ct);
        return Results.Created($"/admin/brands/{phrase.BrandKey}/profile", new { id = phrase.Id });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

brandProfile.MapPut("/voice", async (string key, BrandVoice voice, IBrandProfileStore store, CancellationToken ct) =>
    await store.SetVoiceAsync(key.ToLowerInvariant(), voice, ct) ? Results.NoContent() : Results.NotFound());

brandProfile.MapPost("/profile/import", async (string key, ProfileImport input, IBrandProfileStore store, TimeProvider time, CancellationToken ct) =>
{
    var proof = new List<ProofItem>();
    foreach (var p in input.Proof ?? [])
    {
        if (!TryBuildProof(key, p, time, out var item, out var error)) return Results.BadRequest(new { error, statement = p.Statement });
        proof.Add(item!);
    }
    var phrases = new List<CustomerPhrase>();
    try { phrases.AddRange((input.Phrases ?? []).Select(p => new CustomerPhrase(key, p.Text, p.Source, p.CollectedAt))); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }

    if (await store.GetAsync(key.ToLowerInvariant(), ct) is null) return Results.NotFound();
    var imported = await store.ImportIfEmptyAsync(key.ToLowerInvariant(), input.Voice ?? BrandVoice.Empty, proof, phrases, ct);
    return imported
        ? Results.Ok(new { imported = true, proof = proof.Count, phrases = phrases.Count })
        : Results.Conflict(new { error = "o perfil desta marca já tem dados; use os endpoints de edição" });
});

// --- Payment webhook (PayPal in prod, Test verifier in dev) -------------------
app.MapPost("/webhook/payment", async (HttpContext ctx, IPaymentVerifier verifier, PlaceOrderOnPayment useCase, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("PaymentWebhook");
    var raw = await ReadBodyAsync(ctx);
    var headers = HeaderMap(ctx);

    PaymentEvent? pay;
    try { pay = await verifier.VerifyAndParseAsync(raw, headers, ct); }
    catch (Exception ex) { log.LogError(ex, "Payment verifier threw."); return Results.Ok(); }

    if (pay is null) return Results.Ok(); // unverified or irrelevant event -> ack and ignore

    var result = await useCase.HandleAsync(pay, ct: ct);
    // Always 200 so the processor stops retrying; the outcome is our record to act on.
    return Results.Ok(new { outcome = result.Outcome.ToString(), paymentId = pay.PaymentId, error = result.Error });
});

// --- Stripe webhook: checkout.session.completed -> pedido no fornecedor ---------
// Mesma rede de seguranca do /webhook/payment: assinatura verificada antes de confiar,
// idempotencia por PaymentId no caso de uso, e sempre 200 pro Stripe parar de reenviar.
app.MapPost("/webhook/stripe", async (HttpContext ctx, RootBoost.Infrastructure.Payments.StripeWebhookVerifier verifier,
                                      PlaceOrderOnPayment useCase, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("StripeWebhook");
    var raw = await ReadBodyAsync(ctx);
    var headers = HeaderMap(ctx);

    PaymentEvent? pay;
    try { pay = await verifier.VerifyAndParseAsync(raw, headers, ct); }
    catch (Exception ex) { log.LogError(ex, "Stripe verifier threw."); return Results.Ok(); }

    if (pay is null) return Results.Ok(); // assinatura invalida ou evento irrelevante

    var result = await useCase.HandleAsync(pay, ct: ct);
    return Results.Ok(new { outcome = result.Outcome.ToString(), paymentId = pay.PaymentId, error = result.Error });
});

// --- Checkout hospedado (Stripe): cria a sessao e devolve a URL pra redirecionar ----
// O preco vem do catalogo no servidor (invariante 3). A mesma trava de fulfillment do
// PayPal vale aqui: em LIVE nao deixamos cobrar produto sem VID.
app.MapPost("/checkout/session", async (CreateOrderRequest req, ICheckoutGateway gateway,
                                        IProductCatalog catalog, IConfiguration cfg, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ProductKey))
        return Results.BadRequest(new { error = "productKey required" });

    var isTestMode = string.Equals(cfg["Payments:Verifier"], "Test", StringComparison.OrdinalIgnoreCase);
    var product = catalog.Find(req.ProductKey);
    if (!isTestMode && product is { IsFulfillable: false })
        return Results.BadRequest(new { error = "product not available for purchase yet" });

    var result = await gateway.CreateOrderAsync(req.ProductKey, req.Quantity <= 0 ? 1 : req.Quantity, req.Lang, ct);
    if (!result.Success) return Results.BadRequest(new { error = result.Error });

    return Results.Ok(new { id = result.ProviderOrderId, url = result.RedirectUrl });
});

// --- CJ tracking webhook ------------------------------------------------------
// TODO: confirm the exact CJ webhook payload (field names for order id + tracking number) in the
// CJ docs and adjust the extraction below. We parse defensively and ignore anything we can't read.
app.MapPost("/webhook/cj", async (HttpContext ctx, AttachTracking useCase, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("CjWebhook");
    var raw = await ReadBodyAsync(ctx);
    try
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        // CJ payloads vary by event; try a few likely field names without inventing a contract.
        var supplierOrderId = FirstString(root, "orderId", "cjOrderId", "orderNum");
        var tracking = FirstString(root, "trackNumber", "trackingNumber", "logisticNo");
        if (supplierOrderId is null) { log.LogWarning("CJ webhook without recognizable order id: {Body}", Trunc(raw)); return Results.Ok(); }
        await useCase.HandleAsync(supplierOrderId, tracking, ct);
    }
    catch (Exception ex) { log.LogError(ex, "CJ webhook parse failed: {Body}", Trunc(raw)); }
    return Results.Ok();
});

// --- PayPal server-side checkout ----------------------------------------------
// Price is resolved from the catalog HERE, not sent by the browser. The landing calls:
//   createOrder  -> POST /paypal/create-order { productKey, quantity }  -> { id }
//   onApprove    -> POST /paypal/capture-order { orderId }              -> fulfills
app.MapPost("/paypal/create-order", async (CreateOrderRequest req, ICheckoutGateway gateway, IProductCatalog catalog, IConfiguration cfg, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ProductKey))
        return Results.BadRequest(new { error = "productKey required" });

    // Segurança de fulfillment: em modo LIVE (Payments:Verifier != Test = pagamento REAL) NÃO deixa
    // iniciar checkout de produto não faturável (VID da CJ ainda TODO). Assim nunca capturamos dinheiro
    // de algo que não dá pra enviar (evita disputa). Em modo Test (sandbox) segue liberado pra testar.
    var isTestMode = string.Equals(cfg["Payments:Verifier"], "Test", StringComparison.OrdinalIgnoreCase);
    var product = catalog.Find(req.ProductKey);
    if (!isTestMode && product is { IsFulfillable: false })
        return Results.BadRequest(new { error = "product not available for purchase yet" });

    var result = await gateway.CreateOrderAsync(req.ProductKey, req.Quantity <= 0 ? 1 : req.Quantity, req.Lang, ct);
    return result.Success
        ? Results.Ok(new { id = result.ProviderOrderId })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/paypal/capture-order", async (HttpContext http, CaptureOrderRequest req, ICheckoutGateway gateway, PlaceOrderOnPayment useCase, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.OrderId))
        return Results.BadRequest(new { error = "orderId required" });

    var pay = await gateway.CaptureOrderAsync(req.OrderId, ct);
    if (pay is null)
        return Results.BadRequest(new { error = "capture did not complete" });

    // Sinais pro Meta CAPI: fbp/fbc/sourceUrl vêm do navegador (corpo); IP/UA do request (X-Forwarded-For atrás de proxy).
    var clientIp = http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
    if (string.IsNullOrWhiteSpace(clientIp)) clientIp = http.Connection.RemoteIpAddress?.ToString();
    var conv = new ConversionContext(
        Fbp: req.Fbp, Fbc: req.Fbc, ClientIp: clientIp,
        UserAgent: http.Request.Headers.UserAgent.ToString(), EventSourceUrl: req.SourceUrl);

    var result = await useCase.HandleAsync(pay, conv, ct);
    // The payment already succeeded; report the fulfillment outcome. The customer's money is
    // captured regardless — a NeedsHuman outcome means we alerted an operator to finish it.
    return Results.Ok(new { outcome = result.Outcome.ToString(), paymentId = pay.PaymentId });
});

// --- Traffic autopilot (dry-run): mostra as decisões que a automação tomaria ------
app.MapGet("/admin/traffic/plan", async (HttpContext ctx, RootBoost.Application.Traffic.TrafficAutopilot autopilot, IConfiguration cfg, CancellationToken ct) =>
{
    var report = await autopilot.RunAsync(ct);
    return Results.Ok(report);
}).RequireAuthorization(AuthSetup.AdminPolicy);

// --- Growth planner (dry-run): projeta orgânico vs pago pro orçamento que o usuário mantém ------
app.MapGet("/admin/growth/plan", (HttpContext ctx, IProductCatalog catalog, RootBoost.Application.Traffic.GrowthPlanner planner, IConfiguration cfg) =>
{
    var q = ctx.Request.Query;
    var productKey = q["productKey"].FirstOrDefault() ?? "carorganizer";
    var price = catalog.Find(productKey)?.Price ?? 29.99m;
    decimal cost = decimal.TryParse(q["unitCost"].FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 12m;
    decimal budget = decimal.TryParse(q["budget"].FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0m;
    double cvr = double.TryParse(q["cvr"].FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.02;

    var plan = planner.Plan(new RootBoost.Application.Traffic.GrowthInputs(
        UnitPrice: price, UnitCost: cost, MonthlyAdBudget: budget, StoreConversionRate: cvr));
    return Results.Ok(plan);
}).RequireAuthorization(AuthSetup.AdminPolicy);

// --- Product performance (API key): "qual converte mais" cruzando visitas x pedidos pagos. ---
// ?days=N limita a janela (ex.: ?days=7 = últimos 7 dias). Sem days = tudo.
app.MapGet("/admin/products/performance", async (HttpContext ctx, ProductPerformanceReport report, IConfiguration cfg, CancellationToken ct) =>
{
    DateTimeOffset? since = null;
    if (int.TryParse(ctx.Request.Query["days"].FirstOrDefault(), out var d) && d > 0)
        since = DateTimeOffset.UtcNow.AddDays(-d);

    if (!TryReadBrand(ctx, out var brand)) return Results.BadRequest(new { error = "brand inválida" });
    var result = await report.BuildAsync(since, ct, brand);
    return Results.Ok(result);
}).RequireAuthorization(AuthSetup.AdminPolicy);

app.Run();

// --- helpers ------------------------------------------------------------------
static async Task<string> ReadBodyAsync(HttpContext ctx)
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static Dictionary<string, string> HeaderMap(HttpContext ctx)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var h in ctx.Request.Headers)
        map[h.Key] = h.Value.ToString();
    return map;
}

static string? FirstString(JsonElement root, params string[] names)
{
    foreach (var n in names)
        if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
            return s;
    return null;
}

static string Trunc(string s) => s.Length <= 400 ? s : s[..400];

// Clean projection for the /orders response (enum as string, address flattened).
static bool TryBuildProof(string brandKey, ProofInput input, TimeProvider time, out ProofItem? item, out string? error)
{
    item = null;
    error = null;
    if (!Enum.TryParse<ProofKind>(input.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
    {
        error = $"tipo de prova inválido: '{input.Kind}' (use {string.Join(", ", Enum.GetNames<ProofKind>())})";
        return false;
    }
    try
    {
        var now = time.GetUtcNow();
        item = new ProofItem(brandKey, kind, input.Statement, input.Source, input.VerifiedAt, input.ReviewBy,
            DateOnly.FromDateTime(now.UtcDateTime), now);
        return true;
    }
    catch (ArgumentException ex)
    {
        error = ex.Message;
        return false;
    }
}

static bool TryReadBrand(HttpContext ctx, out string? brand)
{
    brand = null;
    var raw = ctx.Request.Query["brand"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(raw)) return true;
    try { brand = BrandKeys.Validate(raw); return true; }
    catch (ArgumentException) { return false; }
}

record OrderDto(string PaymentId, string ProductKey, int Quantity, string Status, string Email,
                string? SupplierOrderId, string? Tracking, decimal Amount, string Currency,
                string ShipName, string ShipCountry, DateTimeOffset CreatedAt, string? FailureReason,
                string BrandKey, bool IsTest)
{
    public static OrderDto From(Order o) => new(
        o.PaymentId, o.ProductKey, o.Quantity, o.Status.ToString(), o.CustomerEmail,
        o.SupplierOrderId, o.TrackingNumber, o.AmountPaid, o.Currency,
        o.ShipTo.Name, o.ShipTo.CountryCode, o.CreatedAt, o.FailureReason, o.BrandKey, o.IsTest);
}

record PushKeys(string P256dh, string Auth);
record PushDeviceInput(string Endpoint, PushKeys Keys);
record PushRemoveInput(string Endpoint);
record WriteScriptInput(string? Instructions);
record MediaDescriptionInput(string? Description, string? Tags);
record RedactionBoxInput(double X, double Y, double W, double H);
record RedactionInput(string? Status, List<RedactionBoxInput>? Boxes);
record MediaFinishedInput(bool Finished);
record VoiceDesignInput(string? Name, string? Gender, string? Age, string? Pitch);
record VoiceChooseInput(int Seed);

sealed class VoiceCloneForm
{
    public IFormFile? File { get; set; }
    public string? Name { get; set; }
    public string? Person { get; set; }
    public bool Consent { get; set; }
}

static class VoiceAudio
{
    public const long MaxBytes = 15 * 1024 * 1024;
    public static readonly Dictionary<string, string> Types = new()
    {
        [".webm"] = "audio/webm", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4", [".mp4"] = "audio/mp4",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav",
    };
}

record NarrationVoiceDto(Guid Id, string Key, string Name, string Kind, string Status, IReadOnlyList<int> Samples, int? ChosenSeed,
    string? Error, string? ConsentBy, DateTimeOffset? ConsentAt, DateTimeOffset CreatedAt, string? Instruct)
{
    public static NarrationVoiceDto From(NarrationVoice v) => new(v.Id, v.Key, v.Name, v.Kind.ToString(), v.Status.ToString(),
        v.Samples.Select(x => x.Seed).ToList(), v.ChosenSeed, v.Error, v.ConsentBy, v.ConsentAt, v.CreatedAt, v.Instruct);
}
record ScreenElementInput(string? Id, string? Role, string? Text);
record ScreenStateInput(string? BrandKey, string Scene, string? Url, List<ScreenElementInput>? Elements, List<string>? History, int Step, string Image);
record ProductInput(string Name, string Kind, string? Url, string? AppUrl, string? Summary, string? Audience,
    List<string>? Features, List<string>? Differentials, string? Offer);
record ChatMessageInput(string? Role, string? Text);
record OnboardingChatInput(List<ChatMessageInput>? Messages);
record ProductDto(string Name, string Kind, string? Url, string? AppUrl, string Summary, string Audience,
    IReadOnlyList<string> Features, IReadOnlyList<string> Differentials, string? Offer, DateTimeOffset UpdatedAt)
{
    public static ProductDto From(BrandProduct p) => new(p.Name, p.Kind, p.Url, p.AppUrl, p.Summary, p.Audience, p.Features,
        p.Differentials, p.Offer, p.UpdatedAt);
}
record CreativeJobInput(string Kind, string Theme, string? VoiceKey);
record ScriptInput(System.Text.Json.JsonElement Roteiro, System.Text.Json.JsonElement Falas);
record JobPublicationsInput(DateTimeOffset ScheduledFor, string? Instagram, string? Facebook, string? LinkedIn);
record WorkerClaimInput(string WorkerId);
record WorkerProgressInput(string WorkerId, int Percent, string? Note);
record WorkerOutputInput(string? Role, string? Url);
record WorkerDeliverInput(string WorkerId, List<WorkerOutputInput>? Outputs, List<string>? Warnings, int? Seed);
record WorkerFailInput(string WorkerId, string? Error, bool Retry);

record CreativeJobDto(Guid Id, string BrandKey, string Kind, string Theme, string? VoiceKey, string Status,
    System.Text.Json.JsonElement? Roteiro, System.Text.Json.JsonElement? Falas, int? Seed, IReadOnlyList<string> Warnings,
    IReadOnlyList<CreativeOutput> Outputs, int Attempts, int Progress, string? ProgressNote, string? Error,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool VoiceApproved, IReadOnlyList<VoiceTakeDto> VoiceTakes)
{
    public static CreativeJobDto From(CreativeJob j) => new(j.Id, j.BrandKey, j.Kind.ToString(), j.Theme, j.VoiceKey,
        j.Status.ToString(), Parse(j.Script), Parse(j.Lines), j.Seed, j.Warnings, j.Outputs, j.Attempts, j.Progress,
        j.ProgressNote, j.Error, j.CreatedAt, j.UpdatedAt, j.VoiceApproved, j.VoiceTakes.Select(t => new VoiceTakeDto(t.Seed, t.Unmatched)).ToList());

    public static System.Text.Json.JsonElement? Parse(string? json) =>
        json is null ? null : System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
}

/// <summary>O que o worker precisa pra renderizar: contratos, voz, semente e formato. Sem dado de outras marcas.</summary>
record WorkerMediaDto(string Id, string Url, bool IsVideo, RedactionBox? Focus = null);

record VoiceTakeDto(int Seed, int Unmatched);

record WorkerJobDto(Guid Id, string BrandKey, string Kind, string Theme, string? VoiceKey, int? Seed, int Attempts,
    System.Text.Json.JsonElement? Roteiro, System.Text.Json.JsonElement? Falas, IReadOnlyList<WorkerMediaDto>? Media = null,
    bool VoiceApproved = false)
{
    public static WorkerJobDto From(CreativeJob j) => new(j.Id, j.BrandKey, j.Kind.ToString(), j.Theme, j.VoiceKey, j.Seed,
        j.Attempts, CreativeJobDto.Parse(j.Script), CreativeJobDto.Parse(j.Lines), VoiceApproved: j.VoiceApproved);
}

record PublicationInput(string Channel, string MediaUrl, string Caption, DateTimeOffset ScheduledFor);
record CaptionInput(string Caption);
record ScheduleInput(DateTimeOffset ScheduledFor);
record MediaInput(string MediaUrl);
record ManualPublishedInput(string? Url);
record ReelOptionsInput(string? CoverUrl, int? ThumbOffsetMs, bool? ShareToFeed);
record PublicationDto(Guid Id, string BrandKey, string Channel, string MediaUrl, string Caption, int CaptionVersion,
    DateTimeOffset ScheduledFor, string Status, string? ApprovedBy, DateTimeOffset? ApprovedAt, int? ApprovedCaptionVersion,
    string? ExternalId, string? PublishedUrl, string? Error, int Attempts, string? CoverUrl, int? ThumbOffsetMs, bool ShareToFeed)
{
    public static PublicationDto From(Publication p) => new(p.Id, p.BrandKey, p.Channel.ToString(), p.MediaUrl, p.Caption,
        p.CaptionVersion, p.ScheduledFor, p.Status.ToString(), p.ApprovedBy, p.ApprovedAt, p.ApprovedCaptionVersion,
        p.ExternalId, p.PublishedUrl, p.Error, p.Attempts, p.CoverUrl, p.ThumbOffsetMs, p.ShareToFeed);
}
record CopyCheckRequest(string Text, string? Channel = null, string? Field = null);
record ProofInput(string Kind, string Statement, string Source, DateOnly VerifiedAt, DateOnly? ReviewBy = null);
record PhraseInput(string Text, string Source, DateOnly CollectedAt);
record ProfileImport(BrandVoice? Voice, List<ProofInput>? Proof, List<PhraseInput>? Phrases);
record CreateOrderRequest(string ProductKey, int Quantity, string? Lang = null);
record CaptureOrderRequest(string OrderId, string? Fbp = null, string? Fbc = null, string? SourceUrl = null);
record TrackViewRequest(string ProductKey, string? Lang = null);

// Exposed for integration tests (WebApplicationFactory needs a public entry point type).
public partial class Program { }
