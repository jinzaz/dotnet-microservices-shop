using Microsoft.Extensions.DependencyInjection;
using MSA.EventBus.Abstractions;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：AddIntegrationEventHandlersFromAssembly 用反射把 IIntegrationEventHandler&lt;TEvent&gt; 的实现
/// 逐一注册，等价于手写 AddIntegrationEventHandler&lt;TEvent, THandler&gt;()。这里用假的事件/处理器验证扫描本身
/// 没找错、没漏、也没把同一事件的多个处理器相互覆盖掉。
/// </summary>
[TestClass]
public sealed class IntegrationEventHandlerScanningTests
{
    private sealed class FirstEvent : IntegrationEvent;
    private sealed class SecondEvent : IntegrationEvent;

    private sealed class FirstHandler : IIntegrationEventHandler<FirstEvent>
    {
        public Task HandleAsync(FirstEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // 同一个事件挂两个处理器：keyed services 要能把两个都取回来，不能互相覆盖。
    private sealed class SecondHandlerA : IIntegrationEventHandler<SecondEvent>
    {
        public Task HandleAsync(SecondEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SecondHandlerB : IIntegrationEventHandler<SecondEvent>
    {
        public Task HandleAsync(SecondEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [TestMethod]
    public void ScanningRegistersEveryHandlerInTheAssembly()
    {
        var services = new ServiceCollection();
        services.AddIntegrationEventHandlersFromAssembly(typeof(IntegrationEventHandlerScanningTests).Assembly);
        var provider = services.BuildServiceProvider();

        var firstEventHandlers = provider.GetKeyedServices<IIntegrationEventHandler>(typeof(FirstEvent)).ToList();
        Assert.AreEqual(1, firstEventHandlers.Count);
        Assert.IsInstanceOfType<FirstHandler>(firstEventHandlers[0]);

        var secondEventHandlers = provider.GetKeyedServices<IIntegrationEventHandler>(typeof(SecondEvent)).ToList();
        Assert.AreEqual(2, secondEventHandlers.Count, "同一事件的两个处理器都应该被扫描到，互不覆盖。");
        CollectionAssert.AreEquivalent(
            new[] { typeof(SecondHandlerA), typeof(SecondHandlerB) },
            secondEventHandlers.Select(handler => handler.GetType()).ToArray());
    }

    [TestMethod]
    public void ScanningMatchesWhatManualRegistrationWouldProduce()
    {
        var scanned = new ServiceCollection();
        scanned.AddIntegrationEventHandlersFromAssembly(typeof(IntegrationEventHandlerScanningTests).Assembly);

        var manual = new ServiceCollection();
        manual.AddIntegrationEventHandler<FirstEvent, FirstHandler>();
        manual.AddIntegrationEventHandler<SecondEvent, SecondHandlerA>();
        manual.AddIntegrationEventHandler<SecondEvent, SecondHandlerB>();

        var scannedProvider = scanned.BuildServiceProvider();
        var manualProvider = manual.BuildServiceProvider();

        Assert.AreEqual(
            manualProvider.GetKeyedServices<IIntegrationEventHandler>(typeof(FirstEvent)).Count(),
            scannedProvider.GetKeyedServices<IIntegrationEventHandler>(typeof(FirstEvent)).Count());
        Assert.AreEqual(
            manualProvider.GetKeyedServices<IIntegrationEventHandler>(typeof(SecondEvent)).Count(),
            scannedProvider.GetKeyedServices<IIntegrationEventHandler>(typeof(SecondEvent)).Count());
    }
}
