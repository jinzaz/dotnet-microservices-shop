import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, idempotencyKey } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, PageHeader, Status } from '../ui';

type Order = {
  id: string;
  orderNumber: string;
  userId: string;
  amount: number;
  currency: string;
  orderStatus: string;
  fulfillmentStatus: string;
  recipientName: string;
  createdOnUtc: string;
};

type FulfillmentResult = {
  orderId: string;
  status: string;
  changed: boolean;
  carrierCode?: string;
  carrierName?: string;
  trackingNumber?: string;
  updatedOnUtc: string;
};

type Action = 'ship' | 'in-transit' | 'delivered';

export default function OrdersPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [action, setAction] = useState<Action>('ship');
  const [userId, setUserId] = useState('');
  const [fulfillmentStatus, setFulfillmentStatus] = useState('');
  const [selectedOrderId, setSelectedOrderId] = useState('');
  const ordersQuery = useQuery({
    queryKey: ['orders', userId, fulfillmentStatus],
    queryFn: () => {
      const query = new URLSearchParams({ skip: '0', take: '100' });
      if (userId) query.set('userId', userId);
      if (fulfillmentStatus) query.set('fulfillmentStatus', fulfillmentStatus);
      return api<Order[]>(`/api/order/orders?${query}`);
    },
    enabled: can('order.fulfillment.query'),
  });
  const mutation = useMutation({
    mutationFn: ({ orderId, body }: { orderId: string; body: unknown }) => api<FulfillmentResult>(`/api/order/admin/orders/${encodeURIComponent(orderId)}/fulfillment/${action}`, {
      method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey() },
    }),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['orders'] }),
  });

  const filter = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    setUserId(String(values.get('userId') ?? '').trim());
    setFulfillmentStatus(String(values.get('fulfillmentStatus') ?? ''));
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const orderId = selectedOrderId.trim();
    const note = String(values.get('note') ?? '').trim() || null;
    const body = action === 'ship' ? {
      carrierCode: String(values.get('carrierCode') ?? '').trim(),
      carrierName: String(values.get('carrierName') ?? '').trim(),
      trackingNumber: String(values.get('trackingNumber') ?? '').trim(),
      note,
    } : { note };
    mutation.mutate({ orderId, body });
  };

  return <div className="page">
    <PageHeader eyebrow="ORDER / FULFILLMENT" title="订单履约工作台" description="按订单标识推进发货、运输与签收状态；每次提交自动携带独立幂等键。" />
    <section className="panel pending-surface">
      <div className="section-heading"><div><p className="eyebrow">FULFILLMENT QUEUE</p><h2>待履约订单队列</h2></div><span>{ordersQuery.data?.length ?? 0} 条</span></div>
      <form className="pending-filters" onSubmit={filter}><input name="userId" placeholder="Identity User ID" /><select name="fulfillmentStatus"><option value="">全部履约状态</option><option value="PendingPayment">PendingPayment</option><option value="AwaitingShipment">AwaitingShipment</option><option value="Shipped">Shipped</option><option value="InTransit">InTransit</option><option value="Delivered">Delivered</option><option value="Cancelled">Cancelled</option></select><button className="button button--quiet" disabled={!can('order.fulfillment.query')}>筛选队列</button></form>
      {ordersQuery.isError ? <ErrorNotice error={ordersQuery.error} /> : null}
      {ordersQuery.data?.length ? <div className="data-table"><table><thead><tr><th>订单</th><th>用户</th><th>金额</th><th>履约状态</th><th>下单时间</th><th>操作</th></tr></thead><tbody>{ordersQuery.data.map((order) => <tr key={order.id}><td><strong>{order.orderNumber}</strong><small>{order.recipientName}</small></td><td className="mono">{order.userId}</td><td>{formatMoney(order.amount, order.currency)}</td><td><Status>{order.fulfillmentStatus}</Status><small>{order.orderStatus}</small></td><td>{formatDate(order.createdOnUtc)}</td><td><button onClick={() => setSelectedOrderId(order.id)}>处理</button></td></tr>)}</tbody></table></div> : !ordersQuery.isFetching ? <Empty title="暂无匹配订单" detail={can('order.fulfillment.query') ? '调整筛选条件后重试。' : '当前 BM 身份没有订单查询权限。'} /> : null}
    </section>
    {!can('order.fulfillment.edit') ? <ErrorNotice error={new Error('当前 BM 身份没有订单履约编辑权限。')} /> : <div className="split-workspace">
      <section className="panel operation-form-panel">
        <div className="section-heading"><div><p className="eyebrow">STATE TRANSITION</p><h2>推进履约状态</h2></div><Icon name="order" /></div>
        <div className="segmented" role="tablist" aria-label="履约动作">
          {([['ship', '确认发货'], ['in-transit', '运输中'], ['delivered', '确认送达']] as const).map(([value, label]) => <button key={value} type="button" role="tab" aria-selected={action === value} onClick={() => { setAction(value); mutation.reset(); }}>{label}</button>)}
        </div>
        <form className="form-grid" onSubmit={submit}>
          <Field label="订单 ID" hint="可从上方队列选择"><input name="orderId" required value={selectedOrderId} onChange={(event) => setSelectedOrderId(event.target.value)} placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" /></Field>
          {action === 'ship' ? <>
            <Field label="承运商代码"><input name="carrierCode" required maxLength={50} placeholder="SF" /></Field>
            <Field label="承运商名称"><input name="carrierName" required maxLength={120} placeholder="顺丰速运" /></Field>
            <Field label="物流单号"><input name="trackingNumber" required maxLength={120} placeholder="输入运单号" /></Field>
          </> : null}
          <Field label="操作备注"><textarea name="note" maxLength={1000} rows={4} placeholder="可选，记录本次状态推进原因" /></Field>
          {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
          <button className="button button--dark" type="submit" disabled={mutation.isPending}>{mutation.isPending ? '正在推进…' : '确认并提交'}</button>
        </form>
      </section>
      <section className="panel result-panel">
        <div className="section-heading"><div><p className="eyebrow">LATEST RESULT</p><h2>本次操作结果</h2></div><span>LIVE</span></div>
        {mutation.data ? <div className="result-card"><div className="result-card__icon"><Icon name="check" /></div><p>订单状态已处理</p><h3><Status>{mutation.data.status}</Status></h3><dl><div><dt>订单 ID</dt><dd>{mutation.data.orderId}</dd></div><div><dt>发生变更</dt><dd>{mutation.data.changed ? '是' : '幂等重放'}</dd></div><div><dt>承运商</dt><dd>{mutation.data.carrierName ?? '—'}</dd></div><div><dt>物流单号</dt><dd>{mutation.data.trackingNumber ?? '—'}</dd></div></dl></div> : <div className="workflow-rail"><p>允许的履约路径</p><ol><li className={action === 'ship' ? 'active' : ''}><span>1</span><div><strong>Shipped</strong><small>登记承运商与单号</small></div></li><li className={action === 'in-transit' ? 'active' : ''}><span>2</span><div><strong>In transit</strong><small>确认包裹进入运输</small></div></li><li className={action === 'delivered' ? 'active' : ''}><span>3</span><div><strong>Delivered</strong><small>确认用户已经签收</small></div></li></ol><small>从待履约队列选择订单后执行状态推进。</small></div>}
      </section>
    </div>}
  </div>;
}
