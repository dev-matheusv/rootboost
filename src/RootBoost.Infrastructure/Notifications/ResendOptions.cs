namespace RootBoost.Infrastructure.Notifications;

public sealed class ResendOptions
{
    public const string Section = "Resend";

    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "orders@example.com";
    public string FromName { get; set; } = "RootBoost";
    /// <summary>Where fulfillment-failure alerts go (the operator's inbox).</summary>
    public string OperatorEmail { get; set; } = "";
}
