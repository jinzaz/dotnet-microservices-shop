import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, idempotencyKey } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, Icon, PageHeader, Status } from '../ui';

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
  const [action, setAction] = useState<Action>('ship');
  const mutation = useMutation({
    mutationFn: ({ orderId, body }: { orderId: string; body: unknown }) => api<FulfillmentResult>(`/api/order/admin/orders/${encodeURIComponent(orderId)}/fulfillment/${action}`, {
      method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey() },
    }),
  });

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const orderId = String(values.get('orderId') ?? '').trim();
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
      <div className="section-heading"><div><p className="eyebrow">FULFILLMENT QUEUE</p><h2>待履约订单队列</h2></div><span className="pending-label">待 BFF 查询路由</span></div>
      <div className="pending-filters"><input placeholder="订单号 / 用户 / 收件人" disabled /><select disabled><option>全部履约状态</option></select><button className="button button--quiet" disabled>导出队列</button></div>
      <div className="pending-table-head"><span>订单</span><span>用户</span><span>金额</span><span>履约状态</span><span>下单时间</span><span>操作</span></div>
      <Empty title="订单查询尚未接入 BM BFF" detail="OrderService 已有列表与详情接口，但 PBAC 路由表还没有 query 映射；映射后可直接启用本队列。" />
    </section>
    {!can('order.fulfillment.edit') ? <ErrorNotice error={new Error('当前 BM 身份没有订单履约编辑权限。')} /> : <div className="split-workspace">
      <section className="panel operation-form-panel">
        <div className="section-heading"><div><p className="eyebrow">STATE TRANSITION</p><h2>推进履约状态</h2></div><Icon name="order" /></div>
        <div className="segmented" role="tablist" aria-label="履约动作">
          {([['ship', '确认发货'], ['in-transit', '运输中'], ['delivered', '确认送达']] as const).map(([value, label]) => <button key={value} type="button" role="tab" aria-selected={action === value} onClick={() => { setAction(value); mutation.reset(); }}>{label}</button>)}
        </div>
        <form className="form-grid" onSubmit={submit}>
          <Field label="订单 ID" hint="OrderService 使用的 GUID"><input name="orderId" required placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" /></Field>
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
        {mutation.data ? <div className="result-card"><div className="result-card__icon"><Icon name="check" /></div><p>订单状态已处理</p><h3><Status>{mutation.data.status}</Status></h3><dl><div><dt>订单 ID</dt><dd>{mutation.data.orderId}</dd></div><div><dt>发生变更</dt><dd>{mutation.data.changed ? '是' : '幂等重放'}</dd></div><div><dt>承运商</dt><dd>{mutation.data.carrierName ?? '—'}</dd></div><div><dt>物流单号</dt><dd>{mutation.data.trackingNumber ?? '—'}</dd></div></dl></div> : <div className="workflow-rail"><p>允许的履约路径</p><ol><li className={action === 'ship' ? 'active' : ''}><span>1</span><div><strong>Shipped</strong><small>登记承运商与单号</small></div></li><li className={action === 'in-transit' ? 'active' : ''}><span>2</span><div><strong>In transit</strong><small>确认包裹进入运输</small></div></li><li className={action === 'delivered' ? 'active' : ''}><span>3</span><div><strong>Delivered</strong><small>确认用户已经签收</small></div></li></ol><small>当前后端未提供 BM 订单列表查询路由，本页仅呈现真实可用的履约动作。</small></div>}
      </section>
    </div>}
  </div>;
}
