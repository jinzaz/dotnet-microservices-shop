namespace MSA.Certificates;

public enum CertificateStage
{
    /// <summary>已加载、已对外发布（签名密钥会出现在 JWKS 里），但还不用来签名 / 加密。</summary>
    Staged,
    /// <summary>当前证书：签名、加密、对外提供都用它。</summary>
    Active,
    /// <summary>已被替换，只用来验签 / 解密此前签发的内容，保留期过后退役。</summary>
    Retiring,
    Retired,
    Expired,
    Revoked
}

public readonly record struct CertificateTimelineEntry(Guid Id, DateTimeOffset ActivateAt, DateTimeOffset NotAfter, DateTimeOffset? RevokedAt);

public readonly record struct CertificateState(CertificateStage Stage, DateTimeOffset? RetireAt)
{
    public bool IsUsable => Stage is CertificateStage.Staged or CertificateStage.Active or CertificateStage.Retiring;
}

/// <summary>
/// 证书阶段完全由时间推出来，不存状态字段：当前证书 = 激活时间已到的证书里最晚激活的那张；
/// 更早的证书从被下一张接替时起算保留期。这样吊销当前证书时上一张自动回到当前，排期也不需要后台任务去"切状态"。
/// 证书服务（列表、轮换）和各使用方（加载密钥）用的是同一套判定。
/// </summary>
public static class CertificateTimeline
{
    /// <param name="group">同一个（使用方、用途、槽位）里的全部证书。</param>
    public static IReadOnlyDictionary<Guid, CertificateState> Classify(
        IEnumerable<CertificateTimelineEntry> group,
        TimeSpan retention,
        DateTimeOffset now)
    {
        var states = new Dictionary<Guid, CertificateState>();
        var live = new List<CertificateTimelineEntry>();
        foreach (var entry in group)
        {
            if (entry.RevokedAt is not null)
                states[entry.Id] = new(CertificateStage.Revoked, null);
            else if (entry.NotAfter <= now)
                states[entry.Id] = new(CertificateStage.Expired, null);
            else if (entry.ActivateAt > now)
                states[entry.Id] = new(CertificateStage.Staged, null);
            else
                live.Add(entry);
        }

        // 激活时间相同按到期时间、再按 Id 排，结果确定，各个实例算出来的当前证书一致。
        live.Sort(static (left, right) =>
        {
            var order = left.ActivateAt.CompareTo(right.ActivateAt);
            if (order == 0) order = left.NotAfter.CompareTo(right.NotAfter);
            return order != 0 ? order : left.Id.CompareTo(right.Id);
        });

        for (var index = 0; index < live.Count; index++)
        {
            if (index == live.Count - 1)
            {
                states[live[index].Id] = new(CertificateStage.Active, null);
                continue;
            }

            var retireAt = live[index + 1].ActivateAt + retention;
            states[live[index].Id] = new(retireAt > now ? CertificateStage.Retiring : CertificateStage.Retired, retireAt);
        }

        return states;
    }
}
