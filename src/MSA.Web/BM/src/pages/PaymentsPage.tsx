import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, query, type Paged } from '../api';
import { paymentsSummaryQuery } from '../queries';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, formatDate, formatMoney, formatRelative, Icon, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, usePage, useUrlState } from '../ui';

type PaymentRow = {
  id: string; orderId: string; userId: string; amount: number; currency: string; channel: string; status: string;
  merchantTradeNo: string; providerTransactionId?: string; createdOnUtc: string; expiresOnUtc: string;
  paidOnUtc?: string; closedOnUtc?: string; refundedAmount: number; failureReason?: string;
};

const PAGE_SIZE = 25;
const statuses = ['Pending', 'Succeeded', 'Failed', 'Refunding', 'Refunded', 'Closed', 'Expired', 'Cancelling'];
const channelLabels: Record<string, string> = { Alipay: '支付宝', WeChatPay: '微信支付' };
const guidPattern = /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i;

export default function PaymentsPage() {
  const { can } = useSession();
  const canQuery = can('payment.transactions.query');
  const [status, setStatus] = useUrlState('status');
  const [channel, setChannel] = useUrlState('channel');
  const [orderId, setOrderId] = useUrlState('orderId');
  const [page, setPage] = usePage();
  const [selected, setSelected] = useState<PaymentRow | null>(null);
  const validOrderId = !orderId || guidPattern.test(orderId);

  const summary = useQuery({ ...paymentsSummaryQuery(), enabled: canQuery });
  const listQuery = useQuery({
    queryKey: ['payments', 'list', { status, channel, orderId, page }],
    queryFn: ({ signal }) => api<Paged<PaymentRow>>(`/api/payment/admin/payments?${query({ status, channel, orderId, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE })}`, { signal }),
    enabled: canQuery && validOrderId,
    placeholderData: keepPreviousData,
  });

  const counts = (summary.data?.statuses ?? []).reduce<Record<string, number>>((result, row) => ({ ...result, [row.status]: (result[row.status] ?? 0) + row.count }), {});
  const rows = listQuery.data?.items ?? [];

  return <div className="page">
    <PageHeader eyebrow="PAYMENT / TRANSACTIONS" title="支付交易追踪" description="按状态、渠道或订单追踪 PaymentService 的真实交易快照；状态计数统计近 7 日新建交易。" />

    {!canQuery ? <Empty title="没有交易查询权限" detail="需要 payment.transactions.query 才能查看支付交易。" /> : <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="交易状态" value={status} onChange={setStatus} chips={[{ value: '', label: '全部', count: summary.data?.totalCount }, ...statuses.map((value) => ({ value, label: statusLabel(value), count: summary.data ? counts[value] ?? 0 : undefined }))]} />
        <div className="list-toolbar__filters">
          <select className="select" value={channel} onChange={(event) => setChannel(event.target.value)} aria-label="支付渠道"><option value="">全部渠道</option><option value="Alipay">支付宝</option><option value="WeChatPay">微信支付</option></select>
          <SearchInput value={orderId} onChange={setOrderId} placeholder="按订单 ID 精确查询" />
        </div>
      </div>
      {!validOrderId ? <p className="notice notice--warning" role="status"><Icon name="alert" />订单 ID 应为 GUID 格式，例如 3f2504e0-4f89-11d3-9a0c-0305e82c3301。</p> : null}
      {listQuery.isError ? <ErrorNotice error={listQuery.error} onRetry={() => void listQuery.refetch()} /> : null}
      {listQuery.isPending && validOrderId ? <SkeletonRows rows={8} /> : rows.length ? <div className={`data-table${listQuery.isPlaceholderData ? ' is-stale' : ''}`}><table>
        <thead><tr><th>交易</th><th>用户</th><th>金额</th><th>渠道</th><th>状态</th><th>创建时间</th><th>操作</th></tr></thead>
        <tbody>{rows.map((payment) => <tr key={payment.id} className="is-clickable" onClick={() => setSelected(payment)}>
          <td><span className="cell-with-copy"><strong className="mono">{payment.merchantTradeNo}</strong><CopyButton value={payment.merchantTradeNo} label="复制商户单号" /></span><small>订单 {payment.orderId.slice(0, 8)}…</small></td>
          <td className="mono">{payment.userId.slice(0, 13)}…</td>
          <td><strong>{formatMoney(payment.amount, payment.currency)}</strong>{payment.refundedAmount > 0 ? <small className="number-bad">已退 {formatMoney(payment.refundedAmount, payment.currency)}</small> : null}</td>
          <td>{channelLabels[payment.channel] ?? payment.channel}</td>
          <td><Status>{payment.status}</Status>{payment.failureReason ? <small title={payment.failureReason}>{payment.failureReason}</small> : null}</td>
          <td title={formatDate(payment.createdOnUtc)}>{formatRelative(payment.createdOnUtc)}<small>{formatDate(payment.createdOnUtc)}</small></td>
          <td onClick={(event) => event.stopPropagation()}><div className="row-actions"><button type="button" onClick={() => setSelected(payment)}>详情</button>{can('order.fulfillment.query') ? <Link className="row-link" to={`/orders?order=${payment.orderId}`}>订单</Link> : null}</div></td>
        </tr>)}</tbody>
      </table></div> : validOrderId && !listQuery.isFetching ? <Empty title="暂无支付交易" detail={status || channel || orderId ? '当前筛选条件下没有交易。' : '还没有支付交易记录。'} action={status || channel || orderId ? <button className="button button--quiet" type="button" onClick={() => { setStatus(''); setChannel(''); setOrderId(''); }}>清空筛选</button> : undefined} /> : null}
      <Pagination page={page} pageSize={PAGE_SIZE} total={listQuery.data?.total} onChange={setPage} busy={listQuery.isFetching} />
    </section>}
    <p className="capability-note"><Icon name="alert" />退款与关单由订单 Saga 协调（订单取消、超时或支付晚到时自动发起），BM 不提供绕过订单状态的手工退款入口。</p>

    <Modal open={selected !== null} title={selected ? formatMoney(selected.amount, selected.currency) : '交易详情'} description={selected ? `${channelLabels[selected.channel] ?? selected.channel} · ${statusLabel(selected.status)}` : undefined} onClose={() => setSelected(null)}>
      {selected ? <div className="order-detail">
        <ol className="payment-steps">
          <li className="is-done"><i /><strong>创建</strong><small>{formatDate(selected.createdOnUtc)}</small></li>
          <li className={selected.paidOnUtc ? 'is-done' : selected.status === 'Pending' ? 'is-current' : ''}><i /><strong>支付</strong><small>{selected.paidOnUtc ? formatDate(selected.paidOnUtc) : selected.status === 'Pending' ? `${formatRelative(selected.expiresOnUtc)}失效` : '—'}</small></li>
          <li className={selected.refundedAmount > 0 || selected.closedOnUtc ? 'is-done' : ''}><i /><strong>{selected.refundedAmount > 0 ? '退款' : '关闭'}</strong><small>{selected.closedOnUtc ? formatDate(selected.closedOnUtc) : selected.refundedAmount > 0 ? formatMoney(selected.refundedAmount, selected.currency) : '—'}</small></li>
        </ol>
        {selected.failureReason ? <p className="notice notice--error"><Icon name="alert" />{selected.failureReason}</p> : null}
        <dl className="detail-list">
          <div><dt>支付 ID</dt><dd className="mono">{selected.id}<CopyButton value={selected.id} /></dd></div>
          <div><dt>商户单号</dt><dd className="mono">{selected.merchantTradeNo}<CopyButton value={selected.merchantTradeNo} /></dd></div>
          <div><dt>渠道交易号</dt><dd className="mono">{selected.providerTransactionId ?? '—'}{selected.providerTransactionId ? <CopyButton value={selected.providerTransactionId} /> : null}</dd></div>
          <div><dt>订单 ID</dt><dd className="mono">{selected.orderId}<CopyButton value={selected.orderId} /></dd></div>
          <div><dt>用户 ID</dt><dd className="mono">{selected.userId}<CopyButton value={selected.userId} /></dd></div>
          <div><dt>已退款</dt><dd>{formatMoney(selected.refundedAmount, selected.currency)}</dd></div>
        </dl>
        <div className="detail-actions">
          {can('order.fulfillment.query') ? <Link className="button button--quiet" to={`/orders?order=${selected.orderId}`}><Icon name="order" />查看订单</Link> : null}
          {can('identity.users.query') ? <Link className="button button--quiet" to={`/customers?user=${encodeURIComponent(selected.userId)}`}><Icon name="user" />查看用户</Link> : null}
        </div>
      </div> : null}
    </Modal>
  </div>;
}
