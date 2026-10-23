using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RootBoost.Infrastructure.Persistence;

/// <summary>
/// Copia pedidos e visitas do SQLite antigo pro Postgres na virada. Roda no boot quando
/// `Database__ImportFromSqlite` aponta pro arquivo, e só se o Postgres estiver vazio, então rodar
/// de novo não duplica nada. Confere as contagens depois de copiar e falha alto se não baterem:
/// pedido pago é dinheiro, não pode sumir na migração.
/// </summary>
public static class SqliteImporter
{
    public sealed record ImportResult(bool Imported, int Orders, int Views, string Reason);

    public static async Task<ImportResult> ImportIfEmptyAsync(
        RootBoostDbContext target, string sqlitePath, ILogger logger, CancellationToken ct = default)
    {
        if (!File.Exists(sqlitePath))
            return new ImportResult(false, 0, 0, $"arquivo {sqlitePath} não existe");

        if (await target.Orders.AnyAsync(ct) || await target.LandingViews.AnyAsync(ct))
            return new ImportResult(false, 0, 0, "Postgres já tem dados; nada importado");

        var sourceOptions = new DbContextOptionsBuilder<RootBoostDbContext>()
            .UseSqlite($"Data Source={sqlitePath};Mode=ReadOnly")
            .Options;
        await using var source = new RootBoostDbContext(sourceOptions);

        var orders = await source.Orders.AsNoTracking().ToListAsync(ct);
        var views = await source.LandingViews.AsNoTracking().ToListAsync(ct);

        await using var tx = await target.Database.BeginTransactionAsync(ct);
        target.Orders.AddRange(orders);
        target.LandingViews.AddRange(views);
        await target.SaveChangesAsync(ct);

        var copiedOrders = await target.Orders.CountAsync(ct);
        var copiedViews = await target.LandingViews.CountAsync(ct);
        if (copiedOrders != orders.Count || copiedViews != views.Count)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Importação inconsistente: pedidos {orders.Count}->{copiedOrders}, visitas {views.Count}->{copiedViews}. Nada foi gravado.");
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("SQLite importado pro Postgres: {Orders} pedidos e {Views} linhas de visita.", orders.Count, views.Count);
        return new ImportResult(true, orders.Count, views.Count, "importado");
    }
}
