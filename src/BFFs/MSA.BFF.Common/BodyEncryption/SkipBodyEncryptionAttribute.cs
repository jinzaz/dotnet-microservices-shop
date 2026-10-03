namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// 标记一个端点不参与 body 加密：SSE 长连接、文件上传/下载、OIDC 回调重定向、
/// 公钥自身这几类端点天然不适合"整段加密"的模型（流式、无 body、或者就是密钥交换的起点）。
/// 作为 Minimal API 端点元数据使用：app.MapGet(...).WithMetadata(new SkipBodyEncryptionAttribute())，
/// 或者直接 [SkipBodyEncryption] 标在 Controller action 上，两种路由方式都认。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SkipBodyEncryptionAttribute : Attribute;
