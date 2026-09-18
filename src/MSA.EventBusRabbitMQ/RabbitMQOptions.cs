namespace MSA.EventBusRabbitMQ;

/// <summary>Bound from the "RabbitMQ" configuration section; property names are the configuration keys.</summary>
public class RabbitMQOptions
{
    public const string SectionName = "RabbitMQ";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>交换机名称，同一业务域内的服务应共用</summary>
    public string ExchangeName { get; set; } = "msa.commerce.events";

    /// <summary>交换机类型：direct（按事件名精确路由）或 topic（支持通配符）</summary>
    public string ExchangeType { get; set; } = "direct";

    /// <summary>
    /// 本服务的队列名，必须服务间唯一（同一服务多实例共用同一队列名以实现负载均衡消费）。
    /// 没有默认值：共享默认队列会让不同服务互相抢消息，因此启动时强制校验必须配置。
    /// </summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>发布失败时的重试次数</summary>
    public int RetryCount { get; set; } = 5;

    /// <summary>消费端预取数量，控制并发处理消息数</summary>
    public ushort PrefetchCount { get; set; } = 10;

    public bool DurableQueue { get; set; } = true;

    /// <summary>死信交换机：处理失败(Nack且不重入队)的消息最终进入这里，供人工排查或重放</summary>
    public string DeadLetterExchange { get; set; } = "msa.commerce.events.dlx";

    private string? _deadLetterQueue;

    /// <summary>死信队列，未配置时为 "{QueueName}.dlq"，保证每个服务的死信互不混杂。</summary>
    public string DeadLetterQueue
    {
        get => string.IsNullOrWhiteSpace(_deadLetterQueue) ? $"{QueueName}.dlq" : _deadLetterQueue;
        set => _deadLetterQueue = value;
    }
}
