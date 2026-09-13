import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, PageHeader, Status } from '../ui';

type Payment = { id: string; orderId: string; amount: number; currency: string; channel: string; status: string; paymentUrl?: string; expiresOnUtc: string };

export default function PaymentsPage() {
  const { can } = useSession();
  const [orderId, setOrderId] = useState('');
  const query = useQuery({ queryKey: ['payment', orderId], queryFn: () => api<Payment>(`/api/payment/orders/${encodeURIComponent(orderId)}`), enabled: Boolean(orderId) && can('payment.transactions.query'), retry: false });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); setOrderId(String(new FormData(event.currentTarget).get('orderId') ?? '').trim()); };

  return <div className="page">
    <PageHeader eyebrow="PAYMENT / TRANSACTIONS" title="支付交易追踪" description="通过订单 ID 读取 PaymentService 的真实交易快照。" actions={<button className="button button--quiet" type="button" disabled title="等待退款接口">发起退款 · 待接口</button>} />
    <section className="lookup-bar panel"><form onSubmit={submit}><Field label="订单 ID"><input name="orderId" required placeholder="输入订单 GUID" /></Field><button className="button button--dark" type="submit" disabled={!can('payment.transactions.query')}><Icon name="search" />查询交易</button></form><span>READ ONLY · PAYMENT SERVICE</span></section>
    {query.isError ? <ErrorNotice error={query.error} /> : null}
    {query.data ? <section className="payment-detail panel">
      <div className="payment-detail__amount"><p>交易金额</p><strong>{formatMoney(query.data.amount, query.data.currency)}</strong><Status>{query.data.status}</Status></div>
      <div className="payment-detail__track"><span /><span /><span /><i /></div>
      <dl><div><dt>支付 ID</dt><dd>{query.data.id}</dd></div><div><dt>订单 ID</dt><dd>{query.data.orderId}</dd></div><div><dt>支付渠道</dt><dd>{query.data.channel}</dd></div><div><dt>失效时间</dt><dd>{formatDate(query.data.expiresOnUtc)}</dd></div></dl>
      {query.data.paymentUrl ? <a className="text-link" href={query.data.paymentUrl} target="_blank" rel="noreferrer">打开支付地址 <Icon name="arrow" /></a> : null}
    </section> : !query.isFetching && !query.isError ? <Empty title="等待交易查询" detail="输入订单 ID 后，这里会显示金额、渠道、状态与过期时间。" /> : null}
    <p className="capability-note"><Icon name="alert" />缺少支付交易分页列表、渠道/状态筛选、退款、人工关闭与对账接口。</p>
  </div>;
}
