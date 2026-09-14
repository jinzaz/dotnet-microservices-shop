import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, PageHeader, Status } from '../ui';

type Payment = { id: string; orderId: string; amount: number; currency: string; channel: string; status: string; paymentUrl?: string; expiresOnUtc: string };
type PaymentRow = Payment & { userId: string; merchantTradeNo: string; providerTransactionId?: string; createdOnUtc: string; paidOnUtc?: string; closedOnUtc?: string; refundedAmount: number; failureReason?: string };
type PaymentPage = { items: PaymentRow[]; skip: number; take: number; total: number };

export default function PaymentsPage() {
  const { can } = useSession();
  const [orderId, setOrderId] = useState('');
  const [status, setStatus] = useState('');
  const [channel, setChannel] = useState('');
  const listQuery = useQuery({ queryKey: ['payments', status, channel], queryFn: () => { const params = new URLSearchParams({ skip: '0', take: '100' }); if (status) params.set('status', status); if (channel) params.set('channel', channel); return api<PaymentPage>(`/api/payment/admin/payments?${params}`); }, enabled: can('payment.transactions.query') });
  const query = useQuery({ queryKey: ['payment', orderId], queryFn: () => api<Payment>(`/api/payment/orders/${encodeURIComponent(orderId)}`), enabled: Boolean(orderId) && can('payment.transactions.query'), retry: false });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); setOrderId(String(new FormData(event.currentTarget).get('orderId') ?? '').trim()); };
  const filter = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); setStatus(String(data.get('status') ?? '')); setChannel(String(data.get('channel') ?? '')); };

  return <div className="page">
    <PageHeader eyebrow="PAYMENT / TRANSACTIONS" title="支付交易追踪" description="通过订单 ID 读取 PaymentService 的真实交易快照。" actions={<button className="button button--quiet" type="button" disabled title="等待退款接口">发起退款 · 待接口</button>} />
    <section className="lookup-bar panel"><form onSubmit={filter}><Field label="交易状态"><select name="status"><option value="">全部状态</option>{['Pending', 'Succeeded', 'Failed', 'Refunding', 'Refunded', 'Closed', 'Expired', 'Cancelling'].map((value) => <option key={value}>{value}</option>)}</select></Field><Field label="支付渠道"><select name="channel"><option value="">全部渠道</option><option>Alipay</option><option>WeChatPay</option></select></Field><button className="button button--dark" type="submit" disabled={!can('payment.transactions.query')}>筛选</button></form><span>{listQuery.data?.total ?? 0} 笔交易</span></section>
    {listQuery.isError ? <ErrorNotice error={listQuery.error} /> : null}
    <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">TRANSACTION LEDGER</p><h2>支付交易</h2></div><span>{listQuery.data?.items.length ?? 0} 条</span></div>{listQuery.data?.items.length ? <div className="data-table"><table><thead><tr><th>交易</th><th>用户</th><th>金额</th><th>渠道</th><th>状态</th><th>创建时间</th><th>操作</th></tr></thead><tbody>{listQuery.data.items.map((payment) => <tr key={payment.id}><td><strong>{payment.merchantTradeNo}</strong><small>{payment.orderId}</small></td><td className="mono">{payment.userId}</td><td>{formatMoney(payment.amount, payment.currency)}<small>已退 {formatMoney(payment.refundedAmount, payment.currency)}</small></td><td>{payment.channel}</td><td><Status>{payment.status}</Status>{payment.failureReason ? <small>{payment.failureReason}</small> : null}</td><td>{formatDate(payment.createdOnUtc)}</td><td><button onClick={() => setOrderId(payment.orderId)}>查看</button></td></tr>)}</tbody></table></div> : !listQuery.isFetching ? <Empty title="暂无支付交易" detail="调整状态或渠道筛选后重试。" /> : null}</section>
    <section className="lookup-bar panel"><form onSubmit={submit}><Field label="订单 ID"><input name="orderId" required placeholder="输入订单 GUID" /></Field><button className="button button--dark" type="submit" disabled={!can('payment.transactions.query')}><Icon name="search" />查询交易</button></form><span>READ ONLY · PAYMENT SERVICE</span></section>
    {query.isError ? <ErrorNotice error={query.error} /> : null}
    {query.data ? <section className="payment-detail panel">
      <div className="payment-detail__amount"><p>交易金额</p><strong>{formatMoney(query.data.amount, query.data.currency)}</strong><Status>{query.data.status}</Status></div>
      <div className="payment-detail__track"><span /><span /><span /><i /></div>
      <dl><div><dt>支付 ID</dt><dd>{query.data.id}</dd></div><div><dt>订单 ID</dt><dd>{query.data.orderId}</dd></div><div><dt>支付渠道</dt><dd>{query.data.channel}</dd></div><div><dt>失效时间</dt><dd>{formatDate(query.data.expiresOnUtc)}</dd></div></dl>
      {query.data.paymentUrl ? <a className="text-link" href={query.data.paymentUrl} target="_blank" rel="noreferrer">打开支付地址 <Icon name="arrow" /></a> : null}
    </section> : !query.isFetching && !query.isError ? <Empty title="等待交易查询" detail="输入订单 ID 后，这里会显示金额、渠道、状态与过期时间。" /> : null}
    <p className="capability-note"><Icon name="alert" />退款与人工关单必须由 Ordering 工作流协调，避免订单和支付状态分裂；渠道对账能力仍待支付渠道查询契约。</p>
  </div>;
}
