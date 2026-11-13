namespace RootBoost.Domain;

/// <summary>
/// Uma mensagem de conversa com cliente num canal da marca (Onda 5c, caixa de entrada única). Uma linha por (canal, id da
/// mensagem no canal): reler não duplica. Mensagem de cliente é dado pessoal: guarda só o necessário pra ler e responder
/// (quem, quando, texto), com acesso pela marca. <see cref="FromBusiness"/> marca o que a própria marca mandou.
/// </summary>
public sealed class InboxMessage
{
    public const int MaxText = 2000;

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public SocialChannel Channel { get; private set; }
    public string ConversationId { get; private set; } = default!;
    public string ExternalId { get; private set; } = default!;
    /// <summary>Id da pessoa no canal (no Instagram, o IGSID). É o destinatário de uma resposta.</summary>
    public string ContactId { get; private set; } = default!;
    /// <summary>Nome de usuário da pessoa no canal, quando o canal devolve.</summary>
    public string? ContactName { get; private set; }
    public bool FromBusiness { get; private set; }
    public string Text { get; private set; } = default!;
    /// <summary>Mensagem sem texto (foto, áudio, figurinha, compartilhamento): o Console mostra um aviso.</summary>
    public bool HasAttachment { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }

    private InboxMessage() { }

    public InboxMessage(string brandKey, SocialChannel channel, string conversationId, string externalId, string contactId,
        string? contactName, bool fromBusiness, string? text, bool hasAttachment, DateTimeOffset sentAt, DateTimeOffset fetchedAt)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) throw new ArgumentException("conversa obrigatória", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("id da mensagem obrigatório", nameof(externalId));
        if (string.IsNullOrWhiteSpace(contactId)) throw new ArgumentException("contato obrigatório", nameof(contactId));
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Channel = channel;
        ConversationId = conversationId.Trim();
        ExternalId = externalId.Trim();
        ContactId = contactId.Trim();
        ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim();
        FromBusiness = fromBusiness;
        var t = text?.Trim() ?? string.Empty;
        Text = t.Length > MaxText ? t[..MaxText] : t;
        HasAttachment = hasAttachment;
        SentAt = sentAt;
        FetchedAt = fetchedAt;
    }
}
