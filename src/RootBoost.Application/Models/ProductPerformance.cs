namespace RootBoost.Application.Models;

/// <summary>
/// Desempenho de um produto pra decidir qual escalar e qual cortar. É o "qual converte mais":
/// junta visitas (topo) com pedidos pagos (fundo) e deriva conversão e receita por visitante.
/// </summary>
/// <param name="ConversionRate">Pedidos / visitas. 0 quando ainda não houve visita.</param>
/// <param name="RevenuePerVisitor">Receita / visitas. Melhor métrica única de "quanto rende por tráfego".</param>
/// <param name="Fulfillable">Falso enquanto o VID da CJ for TODO: testa orgânico, mas não fatura ainda.</param>
/// <param name="EnoughData">Amostra pequena engana. Vira true a partir de um mínimo de visitas.</param>
public sealed record ProductPerformance(
    string ProductKey,
    string? DisplayName,
    long Views,
    int Orders,
    decimal Revenue,
    string Currency,
    double ConversionRate,
    decimal RevenuePerVisitor,
    bool Fulfillable,
    bool EnoughData);

/// <summary>Relatório completo: produtos rankeados do que mais converte pro que menos, + nota de leitura.</summary>
public sealed record PerformanceReport(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? Since,
    IReadOnlyList<ProductPerformance> Products,
    string Note);
