using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;

namespace MSA.CommerceTests;

/// <summary>
/// 余额支付的记账规则：扣款、余额不足、重复扣款、退款回余额、后台调整不能扣成负数。
/// 事务、Inbox 和行锁是 PostgreSQL 语义，这里只验证记账本身；幂等靠 (用户, 业务键) 流水查重。
/// </summary>
[TestClass]
public sealed class BalancePaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Buyer = "buyer-1";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Debit_DeductsTheOrderAmountAndRecordsAPaymentEntry()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 100m);
        var payment = NewPayment(db, 59.9m);

        var entry = await ledger.TryDebitAsync(payment, Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        Assert.IsNotNull(entry);
        Assert.AreEqual(-59.9m, entry.Amount);
        Assert.AreEqual(40.1m, entry.BalanceAfter);
        Assert.AreEqual(payment.OrderId, entry.OrderId);
        Assert.AreEqual(40.1m, (await db.BalanceAccounts.SingleAsync(TestContext.CancellationToken)).Balance);
    }

    [TestMethod]
    public async Task Debit_WithInsufficientBalance_ChangesNothing()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 50m);

        var entry = await ledger.TryDebitAsync(NewPayment(db, 50.01m), Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        Assert.IsNull(entry, "余额差一分也不能扣。");
        Assert.AreEqual(50m, (await db.BalanceAccounts.SingleAsync(TestContext.CancellationToken)).Balance);
        Assert.AreEqual(1, await db.BalanceEntries.CountAsync(TestContext.CancellationToken), "只有充值那一条。");
    }

    [TestMethod]
    public async Task Debit_WithoutAnAccount_IsInsufficient()
    {
        await using var db = NewDb();

        var entry = await new BalanceLedger(db).TryDebitAsync(NewPayment(db, 1m), Now, TestContext.CancellationToken);

        Assert.IsNull(entry);
    }

    [TestMethod]
    public async Task Debit_ReplayedForTheSamePayment_DeductsOnlyOnce()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 100m);
        var payment = NewPayment(db, 30m);

        var first = await ledger.TryDebitAsync(payment, Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var replay = await ledger.TryDebitAsync(payment, Now.AddSeconds(5), TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        Assert.AreEqual(first!.Id, replay!.Id, "重投返回原来那条流水。");
        Assert.AreEqual(70m, (await db.BalanceAccounts.SingleAsync(TestContext.CancellationToken)).Balance);
        Assert.AreEqual(1, await db.BalanceEntries.CountAsync(entry => entry.Type == BalanceEntryType.Payment, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Refund_CreditsTheBalanceOnceAndCompletesThePayment()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 100m);
        var payment = NewPayment(db, 80m);
        var debit = await ledger.TryDebitAsync(payment, Now, TestContext.CancellationToken);
        payment.MarkPaid(debit!.Id.ToString("N"), Now);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        var refund = PaymentRefund.Request(payment.Id, payment.OrderId, Guid.NewGuid(), Guid.NewGuid(), 30m, "CNY", "After-sales AS001", Now);
        db.PaymentRefunds.Add(refund);
        payment.RequestRefund(refund.Amount);
        var credit = await ledger.CreditRefundAsync(payment, refund, "AS001", Now, TestContext.CancellationToken);
        refund.MarkSucceeded(credit.Id.ToString("N"), Now);
        payment.CompleteRefund(refund.Amount);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var replay = await ledger.CreditRefundAsync(payment, refund, "AS001", Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        Assert.AreEqual(credit.Id, replay.Id, "同一退款单不会重复入账。");
        Assert.AreEqual(30m, credit.Amount);
        Assert.AreEqual("AS001", credit.AfterSalesRequestNumber);
        Assert.AreEqual(50m, (await db.BalanceAccounts.SingleAsync(TestContext.CancellationToken)).Balance);
        Assert.AreEqual(PaymentStatus.Succeeded, payment.Status, "部分退款后支付单仍是成功。");
        Assert.AreEqual(30m, payment.RefundedAmount);
    }

    [TestMethod]
    public async Task Adjustment_CannotDriveTheBalanceNegative()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 10m);

        var error = await Assert.ThrowsExactlyAsync<BalanceRuleException>(() =>
            ledger.AdjustAsync(Buyer, -10.01m, "deduct-1", "扣减", "operator", Now, TestContext.CancellationToken));

        Assert.Contains("余额不足", error.Message);
    }

    [TestMethod]
    public async Task Adjustment_ReusingTheKeyForADifferentAmount_IsAConflict()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await TopUp(ledger, db, 10m, key: "grant-1");

        var replay = await ledger.AdjustAsync(Buyer, 10m, "grant-1", "充值", "operator", Now, TestContext.CancellationToken);
        Assert.IsTrue(replay.IsReplay);
        await Assert.ThrowsExactlyAsync<BalanceIdempotencyConflictException>(() =>
            ledger.AdjustAsync(Buyer, 20m, "grant-1", "充值", "operator", Now, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task WelcomeGrant_OpensTheAccountAndIsPaidOnlyOnce()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);

        var first = await ledger.GrantWelcomeAsync(Buyer, 9999m, Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var replay = await ledger.GrantWelcomeAsync(Buyer, 9999m, Now.AddMinutes(1), TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        Assert.AreEqual(first.Id, replay.Id, "注册事件重投不会再发一次。");
        Assert.AreEqual(9999m, (await db.BalanceAccounts.SingleAsync(TestContext.CancellationToken)).Balance);
        Assert.AreEqual(BalanceEntry.WelcomeKey, first.BusinessKey);
    }

    [TestMethod]
    public async Task WelcomeGrant_CanBeSpentOnTheFirstOrder()
    {
        await using var db = NewDb();
        var ledger = new BalanceLedger(db);
        await ledger.GrantWelcomeAsync(Buyer, 9999m, Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);

        var debit = await ledger.TryDebitAsync(NewPayment(db, 299m), Now, TestContext.CancellationToken);

        Assert.AreEqual(9700m, debit!.BalanceAfter);
    }

    [TestMethod]
    public void WelcomeOptions_RejectNonPositiveOrFractionalCentAmounts()
    {
        Assert.IsTrue(new BalanceWelcomeOptions().IsValid(), "默认 9999 有效。");
        Assert.IsFalse(new BalanceWelcomeOptions { Amount = 0m }.IsValid());
        Assert.IsFalse(new BalanceWelcomeOptions { Amount = 1.005m }.IsValid());
    }

    private async Task TopUp(BalanceLedger ledger, PaymentDbContext db, decimal amount, string key = "top-up")
    {
        await ledger.AdjustAsync(Buyer, amount, key, "充值", "operator", Now, TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);
    }

    private static Payment NewPayment(PaymentDbContext db, decimal amount)
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), Buyer, amount, "CNY", PaymentChannel.Balance, Now, Now.AddMinutes(15));
        db.Payments.Add(payment);
        return payment;
    }

    private static PaymentDbContext NewDb() => new(
        new DbContextOptionsBuilder<PaymentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Mock.Of<IMediator>());
}
