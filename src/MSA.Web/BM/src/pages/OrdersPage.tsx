import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, idempotencyKey } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, Loading, Modal, PageHeader, Status, SubmitBar } from '../ui';

type Order = {
  id: string;
  orderNumber: string;
  userId: string;
  amount: number;
  currency: string;
  orderStatus: string;
  fulfillmentStatus: string;
  recipientName: string;
  phoneNumber: string;
  carrierName?: string;
  trackingNumber?: string;
  createdOnUtc: string;
  updatedOnUtc: string;
};

type OrderDetail = {
  id: string;
  orderNumber: string;
  userId: string;
  amount: number;
  currency: string;
  status: string;
  fulfillmentStatus: string;
  subtotalAmount: number;
  discountAmount: number;
  paymentMethod: string;
  shippingAddress: {
    recipientName: string;
    phoneNumber: string;
    province: string;
    city: string;
    district: string;
    streetLine1: string;
    streetLine2?: string;
  };
  lines: Array<{
    variantId?: string;
    offerId?: string;
    sku: string;
    productName: string;
    variantName: string;
    quantity: number;
    unitPrice: number;
    lineAmount: number;
  }>;
  fulfillmentHistory: Array<{
    fromStatus?: string;
    toStatus: string;
    note?: string;
    actor: string;
    occurredOnUtc: string;
  }>;
};

type FulfillmentResult = {
  orderId: string;
  status: string;
  changed: boolean;
  carrierName?: string;
  trackingNumber?: string;
  updatedOnUtc: string;
};

type Action = 'ship' | 'in-transit' | 'delivered';
type FulfillmentEditor = { order: Order; action: Action };

const actionLabels: Record<Action, string> = {
  ship: '确认发货',
  'in-transit': '确认进入运输',
  delivered: '确认送达',
};

function nextAction(status: string): Action | null {
  if (status === 'AwaitingShipment') return 'ship';
  if (status === 'Shipped') return 'in-transit';
  if (status === 'InTransit') return 'delivered';
  return null;
}

export default function OrdersPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [filters, setFilters] = useState({ userId: '', fulfillmentStatus: '' });
  const [fulfillment, setFulfillment] = useState<FulfillmentEditor | null>(null);
  const [detailOrderId, setDetailOrderId] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<FulfillmentResult | null>(null);

  const ordersQuery = useQuery({
    queryKey: ['orders', filters],
    queryFn: () => {
      const query = new URLSearchParams({ skip: '0', take: '100' });
      if (filters.userId) query.set('userId', filters.userId);
      if (filters.fulfillmentStatus) query.set('fulfillmentStatus', filters.fulfillmentStatus);
      return api<Order[]>(`/api/order/orders?${query}`);
    },
    enabled: can('order.fulfillment.query'),
  });
  const detailQuery = useQuery({
    queryKey: ['orders', 'detail', detailOrderId],
    queryFn: () => api<OrderDetail>(`/api/order/orders/${detailOrderId}`),
    enabled: Boolean(detailOrderId) && can('order.fulfillment.query'),
  });
  const mutation = useMutation({
    mutationFn: ({ orderId, action, body }: { orderId: string; action: Action; body: unknown }) =>
      api<FulfillmentResult>(
        `/api/order/admin/orders/${encodeURIComponent(orderId)}/fulfillment/${action}`,
        { method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey() } },
      ),
    onSuccess: (result) => {
      setLastResult(result);
      setFulfillment(null);
      void client.invalidateQueries({ queryKey: ['orders'] });
    },
  });

  const filter = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    setFilters({
      userId: String(values.get('userId') ?? '').trim(),
      fulfillmentStatus: String(values.get('fulfillmentStatus') ?? ''),
    });
  };

  const submitFulfillment = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!fulfillment) return;
    const values = new FormData(event.currentTarget);
    const note = String(values.get('note') ?? '').trim() || null;
    const [carrierCode, carrierName] = String(values.get('carrier') ?? '').split('|');
    const body = fulfillment.action === 'ship'
      ? {
          carrierCode,
          carrierName,
          trackingNumber: String(values.get('trackingNumber') ?? '').trim(),
          note,
        }
      : { note };
    mutation.mutate({ orderId: fulfillment.order.id, action: fulfillment.action, body });
  };

  return <div className="page">
    <PageHeader
      eyebrow="ORDER / FULFILLMENT"
      title="订单列表与履约"
      description="先在订单列表确认客户、金额和当前状态，再从订单行发起唯一允许的下一步履约操作。"
    />

    <section className="panel pending-surface">
      <div className="section-heading">
        <div><p className="eyebrow">ORDER DIRECTORY</p><h2>全部订单</h2></div>
        <span>{ordersQuery.data?.length ?? 0} 条</span>
      </div>
      <form className="pending-filters" onSubmit={filter}>
        <input name="userId" defaultValue={filters.userId} placeholder="按 Identity User ID 筛选" />
        <select name="fulfillmentStatus" defaultValue={filters.fulfillmentStatus} aria-label="履约状态">
          <option value="">全部履约状态</option>
          {['PendingPayment', 'AwaitingShipment', 'Shipped', 'InTransit', 'Delivered', 'Cancelled'].map((value) => <option key={value}>{value}</option>)}
        </select>
        <button className="button button--quiet" disabled={!can('order.fulfillment.query')}><Icon name="search" />筛选订单</button>
      </form>
      {lastResult ? <p className="notice notice--success" role="status"><Icon name="check" />订单履约已更新为 {lastResult.status}{lastResult.changed ? '' : '（幂等重放）'}。</p> : null}
      {ordersQuery.isError ? <ErrorNotice error={ordersQuery.error} /> : null}
      {ordersQuery.data?.length ? <div className="data-table"><table>
        <thead><tr><th>订单</th><th>客户</th><th>金额</th><th>订单状态</th><th>履约状态</th><th>物流</th><th>下单时间</th><th>操作</th></tr></thead>
        <tbody>{ordersQuery.data.map((order) => {
          const action = nextAction(order.fulfillmentStatus);
          return <tr key={order.id}>
            <td><button className="table-link" type="button" onClick={() => setDetailOrderId(order.id)}>{order.orderNumber}</button><small>{order.id.slice(0, 8)}</small></td>
            <td><strong>{order.recipientName}</strong><small className="mono">{order.userId}</small></td>
            <td><strong>{formatMoney(order.amount, order.currency)}</strong></td>
            <td><Status>{order.orderStatus}</Status></td>
            <td><Status>{order.fulfillmentStatus}</Status></td>
            <td>{order.carrierName ?? '—'}<small>{order.trackingNumber ?? '尚无运单'}</small></td>
            <td>{formatDate(order.createdOnUtc)}</td>
            <td><div className="row-actions">
              <button type="button" onClick={() => setDetailOrderId(order.id)}>详情</button>
              {action && can('order.fulfillment.edit') ? <button className="row-action-primary" type="button" onClick={() => { mutation.reset(); setFulfillment({ order, action }); }}>{actionLabels[action]}</button> : null}
            </div></td>
          </tr>;
        })}</tbody>
      </table></div> : !ordersQuery.isFetching ? <Empty title="暂无匹配订单" detail={can('order.fulfillment.query') ? '调整筛选条件后重试。' : '当前 BM 身份没有订单查询权限。'} /> : null}
    </section>

    <Modal
      open={fulfillment !== null}
      title={fulfillment ? actionLabels[fulfillment.action] : '订单履约'}
      description={fulfillment ? `${fulfillment.order.orderNumber} · 当前 ${fulfillment.order.fulfillmentStatus}` : undefined}
      onClose={() => { setFulfillment(null); mutation.reset(); }}
    >
      {fulfillment ? <form className="modal-form" onSubmit={submitFulfillment}>
        <div className="order-context">
          <div><span>收件人</span><strong>{fulfillment.order.recipientName}</strong></div>
          <div><span>订单金额</span><strong>{formatMoney(fulfillment.order.amount, fulfillment.order.currency)}</strong></div>
          <div><span>下一状态</span><Status>{fulfillment.action === 'ship' ? 'Shipped' : fulfillment.action === 'in-transit' ? 'InTransit' : 'Delivered'}</Status></div>
        </div>
        {fulfillment.action === 'ship' ? <>
          <Field label="承运商"><select name="carrier" required defaultValue="SF|顺丰速运"><option value="SF|顺丰速运">顺丰速运</option><option value="JD|京东物流">京东物流</option><option value="YTO|圆通速递">圆通速递</option><option value="ZTO|中通快递">中通快递</option><option value="STO|申通快递">申通快递</option><option value="EMS|EMS">EMS</option></select></Field>
          <Field label="物流单号"><input name="trackingNumber" required maxLength={120} autoFocus placeholder="扫描或输入运单号" /></Field>
        </> : null}
        <Field label="操作备注"><textarea name="note" maxLength={1000} rows={4} placeholder="可选，记录本次状态推进说明" /></Field>
        {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
        <SubmitBar busy={mutation.isPending} label={actionLabels[fulfillment.action]} onCancel={() => setFulfillment(null)} />
      </form> : null}
    </Modal>

    <Modal
      open={detailOrderId !== null}
      title={detailQuery.data?.orderNumber ?? '订单详情'}
      description="订单商品、配送地址与履约时间线"
      onClose={() => setDetailOrderId(null)}
    >
      {detailQuery.isPending ? <Loading label="正在读取订单详情" /> : null}
      {detailQuery.isError ? <div className="modal-form"><ErrorNotice error={detailQuery.error} /></div> : null}
      {detailQuery.data ? <OrderDetailPanel order={detailQuery.data} /> : null}
    </Modal>
  </div>;
}

function OrderDetailPanel({ order }: { order: OrderDetail }) {
  const address = order.shippingAddress;
  return <div className="order-detail">
    <div className="order-detail__summary">
      <div><span>应付金额</span><strong>{formatMoney(order.amount, order.currency)}</strong><small>优惠 {formatMoney(order.discountAmount, order.currency)}</small></div>
      <div><span>订单 / 履约</span><p><Status>{order.status}</Status> <Status>{order.fulfillmentStatus}</Status></p><small>{order.paymentMethod}</small></div>
    </div>
    <section><h3>商品明细</h3><div className="compact-list">{order.lines.map((line) => <article key={`${line.sku}-${line.offerId ?? ''}`}>
      <div><strong>{line.productName}</strong><small>{line.variantName} · {line.sku}</small></div>
      <span>× {line.quantity}</span><strong>{formatMoney(line.lineAmount, order.currency)}</strong>
    </article>)}</div></section>
    <section><h3>配送地址</h3><p>{address.recipientName} · {address.phoneNumber}</p><small>{address.province}{address.city}{address.district}{address.streetLine1}{address.streetLine2 ?? ''}</small></section>
    <section><h3>履约记录</h3>{order.fulfillmentHistory.length ? <ol className="timeline">{order.fulfillmentHistory.map((item, index) => <li key={`${item.occurredOnUtc}-${index}`}><i /><div><strong>{item.toStatus}</strong><small>{formatDate(item.occurredOnUtc)} · {item.actor}</small>{item.note ? <p>{item.note}</p> : null}</div></li>)}</ol> : <p className="muted-copy">尚未产生履约操作。</p>}</section>
  </div>;
}
