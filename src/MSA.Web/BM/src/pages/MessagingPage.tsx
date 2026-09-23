import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, query } from '../api';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, formatDate, formatRelative, Icon, PageHeader, SkeletonRows, Status, useConfirm, useToast, useUrlState } from '../ui';

/** Every service exposes the same /admin/messaging surface; the id is also its gateway route prefix. */
const services = [
  { id: 'order', label: '订单' },
  { id: 'catalog', label: '商品目录' },
  { id: 'inventory', label: '库存' },
  { id: 'payment', label: '支付' },
  { id: 'basket', label: '购物车' },
  { id: 'notification', label: '通知' },
] as const;

type FailedOutboxMessage = { id: string; eventType: string; retryCount: number; occurredOnUtc: string; lastError?: string | null };
type StalledSaga = { correlationId: string; sagaType: string; state: string; status: string; updatedOnUtc: string; failureReason?: string | null };
type DeadLetter = { eventName: string; attempts: number; payload: string };

const stalledOptions = [
  { value: '15', label: '15 分钟' },
  { value: '30', label: '30 分钟' },
  { value: '60', label: '1 小时' },
  { value: '1440', label: '1 天' },
];

/** Outbox rows store the assembly-qualified name; only the type name is useful on screen. */
function shortEventType(eventType: string) {
  const [typeName] = eventType.split(',');
  return typeName?.split('.').pop() ?? eventType;
}

export default function MessagingPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [service, setService] = useUrlState('service', services[0].id);
  const [stalledMinutes, setStalledMinutes] = useUrlState('stalled', '30');
  const [payload, setPayload] = useState<DeadLetter | null>(null);
  const canQuery = can('system.messaging.query');
  const canEdit = can('system.messaging.edit');
  const base = `/api/${service}/admin/messaging`;

  const failed = useQuery({
    queryKey: ['messaging', service, 'outbox'],
    queryFn: ({ signal }) => api<FailedOutboxMessage[]>(`${base}/outbox/failed?take=50`, { signal }),
    enabled: canQuery,
    refetchInterval: 30_000,
  });
  const stalled = useQuery({
    queryKey: ['messaging', service, 'sagas', stalledMinutes],
    queryFn: ({ signal }) => api<StalledSaga[]>(`${base}/sagas/stalled?${query({ stalledMinutes, take: 50 })}`, { signal }),
    enabled: canQuery,
    refetchInterval: 30_000,
  });
  const deadLetters = useQuery({
    queryKey: ['messaging', service, 'dead-letters'],
    queryFn: ({ signal }) => api<DeadLetter[]>(`${base}/dead-letters?take=20`, { signal }),
    enabled: canQuery,
    retry: false,
  });

  // The overview counters make it obvious which service needs attention before you switch tabs.
  const overview = useQueries({
    queries: services.map((item) => ({
      queryKey: ['messaging', item.id, 'overview'],
      queryFn: ({ signal }: { signal: AbortSignal }) => api<FailedOutboxMessage[]>(`/api/${item.id}/admin/messaging/outbox/failed?take=50`, { signal }),
      enabled: canQuery,
      refetchInterval: 60_000,
    })),
  });

  const invalidate = () => client.invalidateQueries({ queryKey: ['messaging'] });

  const requeue = useMutation({
    mutationFn: (messageId?: string) => api<{ requeued: number }>(`${base}/outbox/failed/requeue?${query({ messageId, max: 100 })}`, { method: 'POST' }),
    onSuccess: async (result) => { toast.success(`已重新排队 ${result.requeued} 条消息`); await invalidate(); },
    onError: (error) => toast.error(error),
  });

  const replay = useMutation({
    mutationFn: (max: number) => api<{ replayed: number }>(`${base}/dead-letters/replay?${query({ max })}`, { method: 'POST' }),
    onSuccess: async (result) => { toast.success(`已重放 ${result.replayed} 条死信`); await invalidate(); },
    onError: (error) => toast.error(error),
  });

  const confirmRequeue = async (row?: FailedOutboxMessage) => {
    const result = await confirm({
      title: row ? `重投「${shortEventType(row.eventType)}」？` : `重投全部 ${failed.data?.length ?? 0} 条失败消息？`,
      detail: '消息会回到待发布队列并重置重试次数。请先确认下游问题已经修复，否则会再次失败。',
      confirmLabel: '重新排队',
    });
    if (result) requeue.mutate(row?.id);
  };

  const confirmReplay = async () => {
    const result = await confirm({
      title: `重放 ${deadLetters.data?.length ?? 0} 条死信？`,
      detail: '死信会按原事件重新投递给本服务的消费者。消费端有 Inbox 去重，重复重放是安全的。',
      confirmLabel: '重放',
    });
    if (result) replay.mutate(50);
  };

  if (!canQuery) return <div className="page"><Empty title="没有查询权限" detail="需要 system.messaging.query 才能查看消息运维数据。" /></div>;

  const deadLetterUnavailable = deadLetters.isError;
  return <div className="page">
    <PageHeader
      eyebrow="RELIABILITY / MESSAGING"
      title="消息与一致性运维"
      description="发布失败的消息、长时间没有推进的流程和死信都会停在这里。修复根因后重投，业务会自行收敛。"
      actions={<button className="button button--quiet" type="button" onClick={() => void invalidate()}><Icon name="refresh" />刷新</button>} />

    <div className="kpi-strip">
      {services.map((item, index) => {
        const count = overview[index]?.data?.length ?? 0;
        return <button key={item.id} type="button" className={item.id === service ? 'kpi-strip__action' : undefined} onClick={() => setService(item.id)}>
          <span>{item.label}</span>
          <strong>{overview[index]?.isPending ? '—' : count}</strong>
          <small>{count ? '条消息发布失败' : '发布正常'}</small>
        </button>;
      })}
    </div>

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">OUTBOX</p><h2>发布失败的消息</h2></div>
        {canEdit && failed.data?.length ? <button className="button button--dark" type="button" disabled={requeue.isPending} onClick={() => void confirmRequeue()}>全部重新排队</button> : null}
      </div>
      {failed.isError ? <ErrorNotice error={failed.error} onRetry={() => void failed.refetch()} /> : null}
      {failed.isPending ? <SkeletonRows rows={3} />
        : failed.data?.length ? <div className="data-table"><table>
          <thead><tr><th>事件</th><th>重试次数</th><th>产生时间</th><th>最近错误</th><th>操作</th></tr></thead>
          <tbody>{failed.data.map((row) => <tr key={row.id}>
            <td><strong>{shortEventType(row.eventType)}</strong><small className="mono">{row.id.slice(0, 8)}<CopyButton value={row.id} label="复制消息 ID" /></small></td>
            <td>{row.retryCount} 次</td>
            <td title={formatDate(row.occurredOnUtc)}>{formatRelative(row.occurredOnUtc)}</td>
            <td><small className="error-cell" title={row.lastError ?? ''}>{(row.lastError ?? '').split('\n')[0] || '—'}</small></td>
            <td><div className="row-actions">{canEdit ? <button type="button" className="row-action-primary" disabled={requeue.isPending} onClick={() => void confirmRequeue(row)}>重新排队</button> : '—'}</div></td>
          </tr>)}</tbody>
        </table></div>
          : <Empty title="没有发布失败的消息" detail="Outbox 中的消息都已成功发布。" />}
    </section>

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">SAGA</p><h2>卡住的流程</h2></div>
        <ChipFilter label="停滞时长" value={stalledMinutes} onChange={setStalledMinutes} chips={stalledOptions} />
      </div>
      {stalled.isError ? <ErrorNotice error={stalled.error} onRetry={() => void stalled.refetch()} /> : null}
      {stalled.isPending ? <SkeletonRows rows={3} />
        : stalled.data?.length ? <div className="data-table"><table>
          <thead><tr><th>关联单据</th><th>流程</th><th>当前步骤</th><th>状态</th><th>最后推进</th><th>失败原因</th></tr></thead>
          <tbody>{stalled.data.map((row) => <tr key={row.correlationId}>
            <td><span className="mono">{row.correlationId.slice(0, 8)}</span><CopyButton value={row.correlationId} label="复制 ID" /></td>
            <td>{row.sagaType}</td>
            <td>{row.state}</td>
            <td><Status>{row.status}</Status></td>
            <td title={formatDate(row.updatedOnUtc)}>{formatRelative(row.updatedOnUtc)}</td>
            <td><small className="error-cell" title={row.failureReason ?? ''}>{row.failureReason ?? '—'}</small></td>
          </tr>)}</tbody>
        </table></div>
          : <Empty title="没有卡住的流程" detail={`${stalledOptions.find((item) => item.value === stalledMinutes)?.label ?? ''}内所有流程都在正常推进。`} />}
      <p className="capability-note"><Icon name="alert" />Failed 状态的流程需要人工处理：退款失败、补偿失败都会停在这里，处理完成后请在对应业务页面补记。</p>
    </section>

    <section className="panel table-panel">
      <div className="section-heading">
        <div><p className="eyebrow">DEAD LETTER</p><h2>死信队列</h2></div>
        {canEdit && deadLetters.data?.length ? <button className="button button--dark" type="button" disabled={replay.isPending} onClick={() => void confirmReplay()}>重放</button> : null}
      </div>
      {deadLetterUnavailable ? <ErrorNotice error={deadLetters.error} onRetry={() => void deadLetters.refetch()} /> : null}
      {!deadLetterUnavailable && deadLetters.isPending ? <SkeletonRows rows={3} />
        : deadLetters.data?.length ? <div className="data-table"><table>
          <thead><tr><th>事件</th><th>投递次数</th><th>消息内容</th></tr></thead>
          <tbody>{deadLetters.data.map((row, index) => <tr key={`${row.eventName}-${index}`}>
            <td><strong>{row.eventName}</strong></td>
            <td>{row.attempts || '—'}</td>
            <td><button type="button" className="link-button" onClick={() => setPayload(row)}>查看载荷</button></td>
          </tr>)}</tbody>
        </table></div>
          : !deadLetterUnavailable ? <Empty title="死信队列是空的" detail="没有消息在重试用尽后被丢到死信队列。" /> : null}
    </section>

    {payload ? <section className="panel">
      <div className="section-heading"><div><p className="eyebrow">PAYLOAD</p><h2>{payload.eventName}</h2></div>
        <div className="row-actions"><CopyButton value={payload.payload} label="复制载荷" /><button type="button" onClick={() => setPayload(null)}>关闭</button></div></div>
      <pre className="payload-view">{payload.payload}</pre>
    </section> : null}
  </div>;
}
