using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RootBoost.Infrastructure.Persistence;

/// <summary>
/// Escolha do banco por configuração (`Database__Provider`): `Sqlite` (padrão, dev e testes) ou
/// `Postgres` (padrão da casa em produção, com migrações do EF). O SQLite segue funcionando até a
/// virada, então ligar o Postgres é só trocar variável no Railway.
/// </summary>
public static class DatabaseSetup
{
    public const string Postgres = "Postgres";

    public static bool IsPostgres(IConfiguration config) =>
        string.Equals(config["Database:Provider"], Postgres, StringComparison.OrdinalIgnoreCase);

    public static void Configure(DbContextOptionsBuilder options, IConfiguration config)
    {
        if (IsPostgres(config))
            options.UseNpgsql(PostgresConnectionString(config),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"));
        else
            options.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=rootboost.db");
    }

    /// <summary>
    /// Aceita `ConnectionStrings__Postgres` no formato do Npgsql ou a `DATABASE_URL` que o Railway
    /// injeta (`postgresql://usuario:senha@host:porta/banco`).
    /// </summary>
    public static string PostgresConnectionString(IConfiguration config)
    {
        var raw = config.GetConnectionString("Postgres") ?? config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Database:Provider=Postgres mas não há ConnectionStrings:Postgres nem DATABASE_URL.");

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
