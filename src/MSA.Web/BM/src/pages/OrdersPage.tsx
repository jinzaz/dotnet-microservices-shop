import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, idempotencyKey, query } from '../api';
import { countOf, ordersSummaryQuery } from '../queries';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, Field, formatDate, formatMoney, formatRelative, Icon, Loading, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, usePage, useToast, useUrlState } from '../ui';

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
  carrierName?: string;
  trackingNumber?: string;
  cancellationReason?: string;
  paymentDueOnUtc?: string;
  shippingAddress: {
    recipientName: string;
    phoneNumber: string;
    province: string;
    city: string;
    district: string;
    streetLine1: string;
    streetLine2?: string;
  };
  lines: Array<{ variantId?: string; offerId?: string; sku: string; productName: string; variantName: string; quantity: number; unitPrice: number; lineAmount: number }>;
  fulfillmentHistory: Array<{ fromStatus?: string; toStatus: string; note?: string; actor: string; occurredOnUtc: string }>;
};

type FulfillmentResult = { orderId: string; status: string; changed: boolean; carrierName?: string; trackingNumber?: string; updatedOnUtc: string };
type Action = 'ship' | 'in-transit' | 'delivered';
type FulfillmentEditor = { id: string; orderNumber: string; recipientName: string; amount: number; currency: string; fulfillmentStatus: string; action: Action };

const PAGE_SIZE = 20;
const actionLabels: Record<Action, string> = { ship: '确认发货', 'in-transit': '确认进入运输', delivered: '确认送达' };
const nextStatus: Record<Action, string> = { ship: 'Shipped', 'in-transit': 'InTransit', delivered: 'Delivered' };
const statuses = ['PendingPayment', 'AwaitingShipment', 'Shipped', 'InTransit', 'Delivered', 'Cancelled'];
const carriers = [['SF', '顺丰速运'], ['JD', '京东物流'], ['YTO', '圆通速递'], ['ZTO', '中通快递'], ['STO', '申通快递'], ['EMS', 'EMS']] as const;
const LAST_CARRIER_KEY = 'bm.orders.lastCarrier';

function nextAction(status: string): Action | null {
  if (status === 'AwaitingShipment') return 'ship';
  if (status === 'Shipped') return 'in-transit';
  if (status === 'InTransit') return 'delivered';
  return null;
}

function rememberedCarrier() {
  try { return window.localStorage.getItem(LAST_CARRIER_KEY) ?? 'SF|顺丰速运'; } catch { return 'SF|顺丰速运'; }
}

export default function OrdersPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const [status, setStatus] = useUrlState('status');
  const [search, setSearch] = useUrlState('q');
  const [userId, setUserId] = useUrlState('user');
  const [detailOrderId, setDetailOrderId] = useUrlState('order', '', { resetPage: false });
  const [page, setPage] = usePage();
  const [fulfillment, setFulfillment] = useState<FulfillmentEditor | null>(null);
  const canQuery = can('order.fulfillment.query');
  const canEdit = can('order.fulfillment.edit');

  const summary = useQuery({ ...ordersSummaryQuery(), enabled: canQuery });
  const ordersQuery = useQuery({
    queryKey: ['orders', 'list', { status, search, userId, page }],
    // One extra row tells us whether a next page exists without a total count.
    queryFn: ({ signal }) => api<Order[]>(`/api/order/admin/orders?${query({ fulfillmentStatus: status, search, userId, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE + 1 })}`, { signal }),
    enabled: canQuery,
    placeholderData: keepPreviousData,
  });
  const detailQuery = useQuery({
    queryKey: ['orders', 'detail', detailOrderId],
    queryFn: () => api<OrderDetail>(`/api/order/admin/orders/${detailOrderId}`),
    enabled: Boolean(detailOrderId) && canQuery,
  });
  const mutation = useMutation({
    mutationFn: ({ editor, body }: { editor: FulfillmentEditor; body: unknown }) =>
      api<FulfillmentResult>(`/api/order/admin/orders/${encodeURIComponent(editor.id)}/fulfillment/${editor.action}`, { method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey() } }),
    onSuccess: (result, { editor }) => {
      toast.success(`${editor.orderNumber} 已更新为「${statusLabel(result.status)}」${result.changed ? '' : '（重复提交已忽略）'}`);
      setFulfillment(null);
      void client.invalidateQueries({ queryKey: ['orders'] });
    },
  });

  const rows = ordersQuery.data?.slice(0, PAGE_SIZE) ?? [];
  const chips = [{ value: '', label: '全部', count: summary.data?.totalOrders }, ...statuses.map((value) => ({ value, label: statusLabel(value), count: summary.data ? countOf(summary.data.fulfillmentStatuses, value) : undefined }))];

  const openFulfillment = (order: Pick<FulfillmentEditor, 'id' | 'orderNumber' | 'recipientName' | 'amount' | 'currency' | 'fulfillmentStatus'>) => {
    const action = nextAction(order.fulfillmentStatus);
    if (!action) return;
    mutation.reset();
    setFulfillment({ ...order, action });
  };

  const submitFulfillment = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!fulfillment) return;
    const values = new FormData(event.currentTarget);
    const note = String(values.get('note') ?? '').trim() || null;
    let body: unknown = { note };
    if (fulfillment.action === 'ship') {
      const carrier = String(values.get('carrier') ?? '');
      const [carrierCode, carrierName] = carrier.split('|');
      try { window.localStorage.setItem(LAST_CARRIER_KEY, carrier); } catch { /* preference only */ }
      body = { carrierCode, carrierName, trackingNumber: String(values.get('trackingNumber') ?? '').trim().toUpperCase(), note };
    }
    mutation.mutate({ editor: fulfillment, body });
  };

  return <div className="page">
    <PageHeader eyebrow="ORDER / FULFILLMENT" title="订单列表与履约" description="按状态快速切换工作队列，从订单行或详情中推进唯一允许的下一步履约操作。"
      actions={<button className="button button--quiet" type="button" onClick={() => void client.invalidateQueries({ queryKey: ['orders'] })} disabled={!canQuery || ordersQuery.isFetching}><Icon name="refresh" className={ordersQuery.isFetching ? 'is-spinning' : undefined} />刷新</button>} />

    {!canQuery ? <Empty title="没有订单查询权限" detail="需要 order.fulfillment.query 才能查看订单。" /> : <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="履约状态" chips={chips} value={status} onChange={setStatus} />
        <div className="list-toolbar__filters">
          <SearchInput value={search} onChange={setSearch} placeholder="订单号 / 订单 ID / 手机号 / 运单号" />
          {userId ? <button className="filter-pill" type="button" onClick={() => setUserId('')}>用户 {userId.slice(0, 8)}… <Icon name="close" /></button> : null}
        </div>
      </div>
      {ordersQuery.isError ? <ErrorNotice error={ordersQuery.error} onRetry={() => void ordersQuery.refetch()} /> : null}
      {ordersQuery.isPending ? <SkeletonRows rows={8} /> : rows.length ? <div className={`data-table${ordersQuery.isPlaceholderData ? ' is-stale' : ''}`}><table>
        <thead><tr><th>订单</th><th>客户</th><th>金额</th><th>订单状态</th><th>履约状态</th><th>物流</th><th>下单时间</th><th>操作</th></tr></thead>
        <tbody>{rows.map((order) => {
          const action = nextAction(order.fulfillmentStatus);
          return <tr key={order.id} className="is-clickable" onClick={() => setDetailOrderId(order.id)}>
            <td><span className="cell-with-copy"><button className="table-link" type="button" onClick={(event) => { event.stopPropagation(); setDetailOrderId(order.id); }}>{order.orderNumber}</button><CopyButton value={order.orderNumber} label="复制订单号" /></span><small>{order.id.slice(0, 8)}</small></td>
            <td><strong>{order.recipientName}</strong><small>{order.phoneNumber}</small></td>
            <td><strong>{formatMoney(order.amount, order.currency)}</strong></td>
            <td><Status>{order.orderStatus}</Status></td>
            <td><Status>{order.fulfillmentStatus}</Status></td>
            <td>{order.carrierName ?? '—'}<small>{order.trackingNumber ?? '尚无运单'}</small></td>
            <td title={formatDate(order.createdOnUtc)}>{formatRelative(order.createdOnUtc)}<small>{formatDate(order.createdOnUtc)}</small></td>
            <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
              {action && canEdit ? <button className="row-action-primary" type="button" onClick={() => openFulfillment(order)}>{actionLabels[action]}</button> : <button type="button" onClick={() => setDetailOrderId(order.id)}>详情</button>}
            </div></td>
          </tr>;
        })}</tbody>
      </table></div> : <Empty title="暂无匹配订单" detail={status || search || userId ? '当前筛选条件下没有订单，试试切换状态或清空搜索。' : '还没有任何订单。'} action={status || search || userId ? <button className="button button--quiet" type="button" onClick={() => { setStatus(''); setSearch(''); setUserId(''); }}>清空筛选</button> : undefined} />}
      <Pagination page={page} pageSize={PAGE_SIZE} hasMore={(ordersQuery.data?.length ?? 0) > PAGE_SIZE} onChange={setPage} busy={ordersQuery.isFetching} />
    </section>}

    <Modal open={fulfillment !== null} title={fulfillment ? actionLabels[fulfillment.action] : '订单履约'} description={fulfillment ? `${fulfillment.orderNumber} · 当前「${statusLabel(fulfillment.fulfillmentStatus)}」` : undefined} onClose={() => setFulfillment(null)}>
      {fulfillment ? <form className="modal-form" onSubmit={submitFulfillment}>
        <div className="order-context">
          <div><span>收件人</span><strong>{fulfillment.recipientName}</strong></div>
          <div><span>订单金额</span><strong>{formatMoney(fulfillment.amount, fulfillment.currency)}</strong></div>
          <div><span>下一状态</span><Status>{nextStatus[fulfillment.action]}</Status></div>
        </div>
        {fulfillment.action === 'ship' ? <div className="form-row">
          <Field label="承运商" hint="默认使用上次选择的承运商"><select name="carrier" required defaultValue={rememberedCarrier()}>{carriers.map(([code, name]) => <option key={code} value={`${code}|${name}`}>{name}</option>)}</select></Field>
          <Field label="物流单号"><input name="trackingNumber" required maxLength={120} autoFocus autoComplete="off" placeholder="扫描或输入运单号" pattern="[A-Za-z0-9\-]{6,120}" title="6–120 位字母、数字或连字符" /></Field>
        </div> : null}
        <Field label="操作备注"><textarea name="note" maxLength={1000} rows={3} placeholder="可选，记录本次状态推进说明" /></Field>
        {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
        <SubmitBar busy={mutation.isPending} label={actionLabels[fulfillment.action]} onCancel={() => setFulfillment(null)} />
      </form> : null}
    </Modal>

    <Modal open={Boolean(detailOrderId)} wide title={detailQuery.data?.orderNumber ?? '订单详情'} description="订单商品、配送地址与履约时间线" onClose={() => setDetailOrderId('')}>
      {detailQuery.isPending ? <Loading label="正在读取订单详情" /> : null}
      {detailQuery.isError ? <div className="modal-form"><ErrorNotice error={detailQuery.error} onRetry={() => void detailQuery.refetch()} /></div> : null}
      {detailQuery.data ? <OrderDetailPanel order={detailQuery.data} canEdit={canEdit} canViewPayment={can('payment.transactions.query')} onAdvance={() => openFulfillment({ ...detailQuery.data!, recipientName: detailQuery.data!.shippingAddress.recipientName })} /> : null}
    </Modal>
  </div>;
}

function OrderDetailPanel({ order, canEdit, canViewPayment, onAdvance }: { order: OrderDetail; canEdit: boolean; canViewPayment: boolean; onAdvance: () => void }) {
  const address = order.shippingAddress;
  const action = nextAction(order.fulfillmentStatus);
  const fullAddress = `${address.province}${address.city}${address.district}${address.streetLine1}${address.streetLine2 ?? ''}`;
  return <div className="order-detail">
    <div className="order-detail__summary">
      <div><span>应付金额</span><strong>{formatMoney(order.amount, order.currency)}</strong><small>商品 {formatMoney(order.subtotalAmount, order.currency)} · 优惠 {formatMoney(order.discountAmount, order.currency)}</small></div>
      <div><span>订单 / 履约</span><p><Status>{order.status}</Status> <Status>{order.fulfillmentStatus}</Status></p><small>{order.paymentMethod}{order.cancellationReason ? ` · 取消原因：${order.cancellationReason}` : ''}</small></div>
    </div>
    <div className="detail-actions">
      {action && canEdit ? <button className="button button--dark" type="button" onClick={onAdvance}><Icon name="truck" />{actionLabels[action]}</button> : null}
      {canViewPayment ? <Link className="button button--quiet" to={`/payments?orderId=${order.id}`}><Icon name="payment" />查看支付交易</Link> : null}
      <span className="mono">{order.id}<CopyButton value={order.id} label="复制订单 ID" /></span>
    </div>
    {order.trackingNumber ? <section><h3>物流信息</h3><p className="cell-with-copy">{order.carrierName} · <span className="mono">{order.trackingNumber}</span><CopyButton value={order.trackingNumber} label="复制运单号" /></p></section> : null}
    <section><h3>商品明细</h3><div className="compact-list">{order.lines.map((line) => <article key={`${line.sku}-${line.offerId ?? ''}`}>
      <div><strong>{line.productName}</strong><small>{line.variantName} · {line.sku}{line.offerId ? ' · 活动价' : ''}</small></div>
      <span>{formatMoney(line.unitPrice, order.currency)} × {line.quantity}</span><strong>{formatMoney(line.lineAmount, order.currency)}</strong>
    </article>)}</div></section>
    <section><h3>配送地址</h3><p className="cell-with-copy">{address.recipientName} · {address.phoneNumber}<CopyButton value={`${address.recipientName} ${address.phoneNumber} ${fullAddress}`} label="复制收货信息" /></p><small>{fullAddress}</small></section>
    <section><h3>履约记录</h3>{order.fulfillmentHistory.length ? <ol className="timeline">{order.fulfillmentHistory.map((item, index) => <li key={`${item.occurredOnUtc}-${index}`}><i /><div><strong>{statusLabel(item.toStatus)}</strong><small>{formatDate(item.occurredOnUtc)} · {item.actor}</small>{item.note ? <p>{item.note}</p> : null}</div></li>)}</ol> : <p className="muted-copy">尚未产生履约操作。</p>}</section>
  </div>;
}
