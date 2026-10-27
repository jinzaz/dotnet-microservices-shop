using MSA.EventBus.Abstractions;
namespace MSA.Contracts;

/// <summary>
/// 新账户注册成功（密码注册或短信登录即注册），由 Identity 经 Outbox 发布，任意服务都可以订阅。
/// 不带手机号、邮箱等个人信息，订阅方需要时按 UserId 自行查询。
/// </summary>
public sealed class UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required string UserId { get; init; }
    /// <summary>Password 或 Sms。</summary>
    public required string Method { get; init; }
    public required DateTimeOffset RegisteredOnUtc { get; init; }
}
