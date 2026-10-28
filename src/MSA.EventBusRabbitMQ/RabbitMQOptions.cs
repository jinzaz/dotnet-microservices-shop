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

    /// <summary>
    /// 长期复用的发布 channel 数，也是同时在途的发布上限；默认与 Outbox:PublishConcurrency 一致，调整时两边一起改。
    /// </summary>
    public int PublishChannelCount { get; set; } = 16;

    /// <summary>消费端预取数量（已投递未确认的上限），必须不小于 <see cref="ConsumerDispatchConcurrency"/></summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// 同时处理的消息数，默认 1（串行）。每条消息有独立的 DI scope 和 DbContext，
    /// 调大前要确认本服务的处理器之间靠数据库锁互斥，且不依赖消息之间的先后顺序。
    /// </summary>
    public ushort ConsumerDispatchConcurrency { get; set; } = 1;

    public bool DurableQueue { get; set; } = true;

    /// <summary>
    /// 死信交换机（fanout，各服务共用）：主队列的 x-dead-letter-exchange 指向它，处理失败(Nack且不重入队)的消息进入这里。
    /// 已部署的主队列参数和交换机类型都改不了，所以它只负责把消息转给 <see cref="DeadLetterRoutingExchange"/>，不直接绑死信队列。
    /// </summary>
    public string DeadLetterExchange { get; set; } = "msa.commerce.events.dlx";

    /// <summary>
    /// 死信路由交换机（direct），绑定在 <see cref="DeadLetterExchange"/> 后面；各服务的死信队列按自己的 QueueName 绑定到这里。
    /// 进死信的消息都经过重试队列，RoutingKey 已被改写成 QueueName，因此只会落进所属服务的死信队列。
    /// </summary>
    public string DeadLetterRoutingExchange => $"{DeadLetterExchange}.routed";

    /// <summary>
    /// 消费失败后的最大投递次数（含首次）。用尽后消息进入死信队列。至少为 2：
    /// 首次投递的 RoutingKey 是事件名，只有经过一次重试队列才会变成 QueueName，死信才能按服务路由。
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>两次投递之间的等待时间，由重试队列的 TTL 实现。</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    private string? _retryQueue;

    /// <summary>重试队列，未配置时为 "{QueueName}.retry"。</summary>
    public string RetryQueue
    {
        get => string.IsNullOrWhiteSpace(_retryQueue) ? $"{QueueName}.retry" : _retryQueue;
        set => _retryQueue = value;
    }

    private string? _deadLetterQueue;

    /// <summary>死信队列，未配置时为 "{QueueName}.dlq"，保证每个服务的死信互不混杂。</summary>
    public string DeadLetterQueue
    {
        get => string.IsNullOrWhiteSpace(_deadLetterQueue) ? $"{QueueName}.dlq" : _deadLetterQueue;
        set => _deadLetterQueue = value;
    }
}
