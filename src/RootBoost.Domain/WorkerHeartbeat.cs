namespace RootBoost.Domain;

/// <summary>
/// Sinal de vida do computador que renderiza (worker). Cada rodada e cada progresso renovam; sem sinal há alguns minutos,
/// o Console avisa "nenhum computador ligado" em vez de deixar o job parado na fila sem explicação.
/// </summary>
public sealed class WorkerHeartbeat
{
    public string WorkerId { get; private set; } = default!;
    public DateTimeOffset LastSeenAt { get; private set; }
    /// <summary>O que estava fazendo ("livre", "gravando a tela: ...", "renderizando").</summary>
    public string? Activity { get; private set; }

    private WorkerHeartbeat() { }

    public WorkerHeartbeat(string workerId, DateTimeOffset now, string? activity)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Trim().Length > 100) throw new ArgumentException("worker sem identificação", nameof(workerId));
        WorkerId = workerId.Trim();
        Beat(now, activity);
    }

    public void Beat(DateTimeOffset now, string? activity)
    {
        LastSeenAt = now;
        Activity = string.IsNullOrWhiteSpace(activity) ? null : activity.Trim()[..Math.Min(activity.Trim().Length, 300)];
    }
}
