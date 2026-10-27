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
    var stored = await storage.GetAsync(asset.StorageKey, ct);
    if (stored is null) return Results.NotFound();
    ctx.Response.ContentLength = stored.Length;
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Stream(stored.Content, asset.ContentType);
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
    if (channel == SocialChannel.Facebook && Uri.TryCreate(input.MediaUrl, UriKind.Absolute, out _) &&
        RootBoost.Infrastructure.Publishing.InstagramPublisher.IsVideo(input.MediaUrl))
        return Results.BadRequest(new { error = "vídeo na Página do Facebook ainda não é suportado; use imagem ou publique à mão" });
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
    if (publication.Channel == SocialChannel.Facebook && RootBoost.Infrastructure.Publishing.InstagramPublisher.IsVideo(input.MediaUrl ?? ""))
        return Results.BadRequest(new { error = "vídeo na Página do Facebook ainda não é suportado; use imagem ou publique à mão" });
    try { publication.ChangeMedia(input.MediaUrl ?? "", time.GetUtcNow()); }
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
    isVideo = a.IsVideo, createdAt = a.CreatedAt, url = MediaPublicUrl(options, req, a)
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

record PublicationInput(string Channel, string MediaUrl, string Caption, DateTimeOffset ScheduledFor);
record CaptionInput(string Caption);
record ScheduleInput(DateTimeOffset ScheduledFor);
record MediaInput(string MediaUrl);
record PublicationDto(Guid Id, string BrandKey, string Channel, string MediaUrl, string Caption, int CaptionVersion,
    DateTimeOffset ScheduledFor, string Status, string? ApprovedBy, DateTimeOffset? ApprovedAt, int? ApprovedCaptionVersion,
    string? ExternalId, string? PublishedUrl, string? Error, int Attempts)
{
    public static PublicationDto From(Publication p) => new(p.Id, p.BrandKey, p.Channel.ToString(), p.MediaUrl, p.Caption,
        p.CaptionVersion, p.ScheduledFor, p.Status.ToString(), p.ApprovedBy, p.ApprovedAt, p.ApprovedCaptionVersion,
        p.ExternalId, p.PublishedUrl, p.Error, p.Attempts);
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
