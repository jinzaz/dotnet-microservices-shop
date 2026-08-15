using MSA.Libraries.Messaging.Events;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging.Handlers
{
    // 示例业务 Saga 处理器：OrderCreationSaga
    public class OrderCreationSagaHandler : ISagaHandler
    {
        public string SagaType => "OrderCreationSaga";

        public IEnumerable<string> SubscribedEventTypes => new[] { "OrderCreated" };

        public async Task HandleAsync(IIntegrationEvent @event, SagaState state, ISagaRepository repository, IEventBus bus)
        {
            // 简单示例：当订单创建时，尝试预留库存（通过发布事件或调用库存服务）。
            // 这里用 PublishAsync 发布一个虚拟事件：InventoryReserveRequested

            // 解析订单信息
            var payload = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(@event.Payload);
            var orderId = payload.GetProperty("OrderId").GetString();

            // 将 SagaState 标记为进行中并保存部分数据
            state.State = "ReservingInventory";
            state.Data = JsonSerializer.Serialize(new { OrderId = orderId });
            state.UpdatedAt = DateTime.UtcNow;

            // 发布库存预留事件（示例）并使用重试策略
            var reserveEvent = new IntegrationEvent("InventoryReserveRequested", JsonSerializer.Serialize(new { OrderId = orderId }), new Dictionary<string, string> { { "CorrelationId", state.CorrelationId } });

            try
            {
                await RetryPolicy.ExecuteAsync(() => bus.PublishAsync(reserveEvent), maxAttempts: 3, delay: TimeSpan.FromSeconds(1), onRetry: (ex, attempt) =>
                {
                    System.Console.WriteLine($"[Saga] Retry publishing reserve event attempt {attempt}: {ex.Message}");
                });

                // 标记为等待库存确认
                state.State = "WaitingInventoryConfirmation";
            }
            catch (Exception ex)
            {
                // 发布补偿事件（示例）：如果预留库存失败，则取消订单
                System.Console.WriteLine($"[Saga] Failed to request inventory reservation: {ex.Message}");
                var cancelEvent = new IntegrationEvent("OrderCancelRequested", JsonSerializer.Serialize(new { OrderId = orderId }), new Dictionary<string, string> { { "CorrelationId", state.CorrelationId } });
                await bus.PublishAsync(cancelEvent);
                state.State = "Cancelled";
                state.Completed = true;
            }

            await repository.UpdateAsync(state);
        }
    }
}
