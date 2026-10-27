using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RootBoost.Infrastructure.Persistence;

/// <summary>
/// Banco único: Postgres em todo lugar (produção, dev local e testes), sempre com migrações do EF.
/// O SQLite saiu em 27/10/2026 (RB-29): o EnsureCreated dele não ganhava tabela nova e travava o dev.
/// </summary>
public static class DatabaseSetup
{
    public static void Configure(DbContextOptionsBuilder options, IConfiguration config) =>
        options.UseNpgsql(PostgresConnectionString(config),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"));

    /// <summary>
    /// Aceita `ConnectionStrings__Postgres` no formato do Npgsql ou a `DATABASE_URL` que o Railway
    /// injeta (`postgresql://usuario:senha@host:porta/banco`).
    /// </summary>
    public static string PostgresConnectionString(IConfiguration config)
    {
        var raw = config.GetConnectionString("Postgres") ?? config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Banco não configurado: defina ConnectionStrings:Postgres ou DATABASE_URL (dev local: docker compose up -d).");

        return raw.StartsWith("postgres", StringComparison.OrdinalIgnoreCase) && raw.Contains("://")
            ? FromUrl(raw)
            : raw;
    }

    public static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        // Repassa parâmetros da URL (ex.: sslmode=require) sem inventar nenhum.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2) builder[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
        }

        return builder.ConnectionString;
    }
}

/// <summary>
/// Usada só pelo `dotnet ef` pra gerar migrações. As migrações são do Postgres; o SQLite continua
/// com `EnsureCreated` enquanto existir.
/// </summary>
public sealed class RootBoostDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RootBoostDbContext>
{
    public RootBoostDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RootBoostDbContext>()
            .UseNpgsql("Host=localhost;Database=rootboost_design;Username=design;Password=design")
            .Options;
        return new RootBoostDbContext(options);
    }
}
