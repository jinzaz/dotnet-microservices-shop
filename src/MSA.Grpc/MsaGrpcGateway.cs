using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace MSA.Grpc;

/// <summary>
/// 所有「实时问一下对端」的网关都从这里派生：gRPC 细节只出现在这一层，
/// 传输故障被翻译成「问不到」（null），应用层因此不需要认识 RpcException，
/// 也不会把一次网络抖动误当成业务结论。
/// </summary>
public abstract class MsaGrpcGateway<TGateway>(ILogger<TGateway> logger)
{
    protected async Task<TResult?> CallAsync<TResult>(
        string operation,
        Func<Task<TResult?>> call)
        where TResult : class
    {
        try
        {
            return await call();
        }
        catch (RpcException exception)
        {
            // Unavailable / DeadlineExceeded 是常态化的瞬时故障，业务拒绝（FailedPrecondition 等）也一并降级处理，
            // 由调用方按自己的容忍度决定后续动作。
            logger.LogWarning(
                "Realtime call {Operation} failed with {StatusCode}: {Detail}",
                operation, exception.StatusCode, exception.Status.Detail);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Realtime call {Operation} failed.", operation);
            return null;
        }
    }

    /// <summary>金额在契约里是字符串（protobuf 没有十进制类型），两端都按不变文化解析与格式化。</summary>
    protected static decimal ParseAmount(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;

    protected static DateTimeOffset? ParseInstant(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var instant) ? instant : null;
}
