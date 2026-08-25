using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.EventBus.Abstractions;

public abstract class IntergrationEventHandler<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    private static readonly ResiliencePipeline Pipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,

                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutException>()
                    .Handle<HttpRequestException>()
            })
            .Build();

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        await Pipeline.ExecuteAsync(
            async ct =>
            {
                await HandleCoreAsync(@event, ct);
            },
            cancellationToken);
    }

    /// <summary>
    /// 消息处理核心逻辑，内部需要具有幂等性实现，该方法会被重试策略包装，若抛出异常则会触发重试
    /// </summary>
    /// <param name="event"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected abstract Task HandleCoreAsync(TEvent @event, CancellationToken cancellationToken = default);
}
