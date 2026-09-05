namespace MSA.Repositories.Persistence;

public sealed class InboxMessage
{
    private InboxMessage() { }
    public InboxMessage(Guid messageId, string consumer, DateTimeOffset processedOnUtc) { MessageId = messageId; Consumer = consumer; ProcessedOnUtc = processedOnUtc; }
    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedOnUtc { get; private set; }
}
