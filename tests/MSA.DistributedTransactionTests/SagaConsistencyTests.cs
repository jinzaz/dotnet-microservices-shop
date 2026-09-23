using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSA.Saga;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：Saga 的并发推进（丢失更新）、超时租约（重复补偿）、迟到与乱序事件、补偿幂等与终态保护。
/// </summary>
[TestClass]
public sealed class SagaConsistencyTests
{
    private const string SagaType = "LedgerTransfer";
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>
    /// 丢失更新：两条消息（例如库存回执与支付回执）同时推进同一个 Saga，乐观并发让后写者失败重试，
    /// 而不是用旧状态覆盖新状态。
    /// </summary>
    [TestMethod]
    public async Task ConcurrentHandlersCannotOverwriteEachOthersSagaState()
    {
        var correlationId = await StartSagaAsync("AwaitingInventory");

        await using var inventoryHandler = _harness.NewContext();
        await using var paymentHandler = _harness.NewContext();
        var fromInventory = await ((ISagaStore)inventoryHandler).FindAsync(correlationId);
        var fromPayment = await ((ISagaStore)paymentHandler).FindAsync(correlationId);

        fromInventory!.TransitionTo("AwaitingPayment", """{"reservationId":"r-1"}""", Now);
        await ((ISagaStore)inventoryHandler).SaveChangesAsync();

        fromPayment!.TransitionTo("Confirmed", """{"paymentId":"p-1"}""", Now);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(
            () => ((ISagaStore)paymentHandler).SaveChangesAsync());

        await using var verify = _harness.NewContext();
        var saga = await ((ISagaStore)verify).FindAsync(correlationId);
        Assert.AreEqual("AwaitingPayment", saga!.State, "后写者必须重试，而不是覆盖。");
    }

    /// <summary>超时租约：两个实例同时扫描到期的 Saga，只有一个能拿到，避免重复补偿。</summary>
    [TestMethod]
    public async Task OnlyOneWorkerClaimsADueTimeout()
    {
        var correlationId = await StartSagaAsync("AwaitingPayment", timeoutOn: Now.AddMinutes(-1));
        var lease = TimeSpan.FromMinutes(1);

        await using var workerA = _harness.NewContext();
        await using var workerB = _harness.NewContext();
        var claimedByA = await ((ISagaStore)workerA).ClaimDueTimeoutsAsync(Guid.NewGuid(), 10, Now, lease, CancellationToken.None);
        var claimedByB = await ((ISagaStore)workerB).ClaimDueTimeoutsAsync(Guid.NewGuid(), 10, Now, lease, CancellationToken.None);

        Assert.HasCount(1, claimedByA);
        Assert.AreEqual(correlationId, claimedByA[0].CorrelationId);
        Assert.IsEmpty(claimedByB, "同一个 Saga 不能被两个实例同时补偿。");

        var afterLeaseExpiry = await ((ISagaStore)workerB).ClaimDueTimeoutsAsync(
            Guid.NewGuid(), 10, Now.Add(lease).AddSeconds(1), lease, CancellationToken.None);
        Assert.HasCount(1, afterLeaseExpiry, "实例崩溃后超时任务必须能被接管。");
    }

    /// <summary>超时处理失败：事务回滚后 Saga 状态不变，租约到期后会重新处理。</summary>
    [TestMethod]
    public async Task AFailedTimeoutHandlerLeavesTheSagaUntouched()
    {
        var correlationId = await StartSagaAsync("AwaitingPayment", timeoutOn: Now.AddMinutes(-1));
        var lockId = Guid.NewGuid();

        await using var worker = _harness.NewContext();
        ISagaStore store = worker;
        await store.ClaimDueTimeoutsAsync(lockId, 10, Now, TimeSpan.FromMinutes(1), CancellationToken.None);

        await using (var transaction = await store.BeginTransactionAsync())
        {
            var saga = await store.FindAsync(correlationId);
            saga!.BeginCompensation("CompensatingInventory", """{"requiresInventoryCompensation":true}""", Now);
            // 补偿指令还没发出去就失败了：直接放弃事务。
            await transaction.DisposeAsync();
        }

        await using var verify = _harness.NewContext();
        var reloaded = await ((ISagaStore)verify).FindAsync(correlationId);
        Assert.AreEqual(SagaStatus.Running, reloaded!.Status, "失败的补偿不能留下半个状态机。");
        Assert.AreEqual("AwaitingPayment", reloaded.State);
    }

    /// <summary>终态保护：已完成的 Saga 拒绝迟到事件，避免重复扣减或重复发货。</summary>
    [TestMethod]
    public async Task ACompletedSagaRejectsLateEvents()
    {
        var correlationId = await StartSagaAsync("AwaitingInventoryCommit");
        await using var db = _harness.NewContext();
        ISagaStore store = db;
        var saga = await store.FindAsync(correlationId);
        saga!.Complete(Now);
        await store.SaveChangesAsync();

        Assert.ThrowsExactly<InvalidOperationException>(() => saga.TransitionTo("AwaitingPayment", "{}", Now));
        Assert.ThrowsExactly<InvalidOperationException>(() => saga.Fail("late failure", Now));
        Assert.ThrowsExactly<InvalidOperationException>(() => saga.BeginCompensation("CompensatingRefund", "{}", Now));
    }

    /// <summary>
    /// 迟到且不可逆的事件（典型例子：订单超时取消后支付回调才到达）：已补偿的 Saga 可以重新打开补偿，
    /// 把新出现的副作用（这笔钱）退回去。
    /// </summary>
    [TestMethod]
    public async Task ACompensatedSagaReopensForALateIrreversibleEffect()
    {
        var correlationId = await StartSagaAsync("AwaitingPayment");
        await using var db = _harness.NewContext();
        ISagaStore store = db;
        var saga = await store.FindAsync(correlationId);

        saga!.BeginCompensation("CompensatingInventory", """{"requiresInventoryCompensation":true}""", Now);
        saga.Compensate(Now);
        await store.SaveChangesAsync();

        // 支付回调迟到：必须重新进入补偿去退款。
        saga.BeginCompensation("CompensatingRefund", """{"requiresPaymentCompensation":true}""", Now.AddMinutes(1));
        await store.SaveChangesAsync();

        Assert.AreEqual(SagaStatus.Compensating, saga.Status);
        Assert.AreEqual("CompensatingRefund", saga.State);
    }

    /// <summary>
    /// 补偿幂等：库存释放回执重复到达、且与退款回执乱序时，只有两路补偿都完成才收敛为 Compensated。
    /// </summary>
    [TestMethod]
    public async Task DuplicateAndOutOfOrderCompensationAcksConvergeOnce()
    {
        var correlationId = await StartSagaAsync("AwaitingPayment");
        await using var db = _harness.NewContext();
        ISagaStore store = db;
        var saga = await store.FindAsync(correlationId);
        saga!.BeginCompensation(
            "Compensating",
            JsonSerializer.Serialize(new CompensationData(true, false, true, false)),
            Now);
        await store.SaveChangesAsync();

        MarkCompensated(saga, inventory: true, payment: false);
        MarkCompensated(saga, inventory: true, payment: false); // 重复回执
        Assert.AreEqual(SagaStatus.Compensating, saga.Status, "退款还没回来，不能提前收敛。");

        MarkCompensated(saga, inventory: false, payment: true);
        Assert.AreEqual(SagaStatus.Compensated, saga.Status);

        // 补偿完成后再来一条重复回执：不能再次改变终态。
        Assert.ThrowsExactly<InvalidOperationException>(() => saga.Compensate(Now));
        await store.SaveChangesAsync();
    }

    /// <summary>Mirrors OrderCompensation.MarkAsync: idempotent flags, and only the last ack completes the saga.</summary>
    private static void MarkCompensated(SagaState saga, bool inventory, bool payment)
    {
        if (saga.Status != SagaStatus.Compensating)
            return;
        var current = JsonSerializer.Deserialize<CompensationData>(saga.Data)!;
        var updated = current with
        {
            InventoryCompensated = inventory || current.InventoryCompensated,
            PaymentCompensated = payment || current.PaymentCompensated
        };
        if (updated.IsComplete)
        {
            saga.Compensate(Now);
            return;
        }
        saga.TransitionTo(saga.State, JsonSerializer.Serialize(updated), Now);
    }

    private async Task<Guid> StartSagaAsync(string state, DateTimeOffset? timeoutOn = null)
    {
        var correlationId = Guid.NewGuid();
        await using var db = _harness.NewContext();
        ISagaStore store = db;
        var saga = SagaState.Start(correlationId, SagaType, state, "{}", Now.AddMinutes(-5));
        store.Add(saga);
        if (timeoutOn.HasValue)
            saga.ScheduleTimeout(timeoutOn.Value, Now.AddMinutes(-5));
        await store.SaveChangesAsync();
        return correlationId;
    }

    private sealed record CompensationData(
        bool RequiresInventoryCompensation,
        bool InventoryCompensated,
        bool RequiresPaymentCompensation,
        bool PaymentCompensated)
    {
        public bool IsComplete =>
            (!RequiresInventoryCompensation || InventoryCompensated)
            && (!RequiresPaymentCompensation || PaymentCompensated);
    }
}
