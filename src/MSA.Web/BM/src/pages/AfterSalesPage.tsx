import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, query } from '../api';
import { useSession } from '../session';
import { ChipFilter, CopyButton, Empty, ErrorNotice, Field, formatDate, formatMoney, formatRelative, Icon, Loading, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, usePage, useToast, useUrlState } from '../ui';

type AfterSalesSummary = {
  id: string;
  requestNumber: string;
  orderId: string;
  orderNumber: string;
  userId: string;
  type: string;
  status: string;
  refundAmount: number;
  currency: string;
  productNames: string[];
  replacementStock: string;
  returnDisposition: string;
  deadlineOnUtc?: string;
  createdOnUtc: string;
  updatedOnUtc: string;
};

type Shipment = { carrierCode?: string; carrierName: string; trackingNumber: string; shippedOnUtc: string };

type AfterSalesDetail = AfterSalesSummary & {
  reasonCode: string;
  description: string;
  lines: Array<{ sku: string; productName: string; variantName: string; quantity: number; refundAmount: number; restockedQuantity?: number }>;
  evidenceUrls: string[];
  returnAddress?: string;
  returnShipment?: Shipment;
  replacementShipment?: Shipment;
  rejectionReason?: string;
  refundFailureReason?: string;
  warehouse?: { replacementStock: string; replacementStockFailure?: string; returnDisposition: string; dispositionNote?: string; disposedBy?: string; disposedOnUtc?: string };
  history: Array<{ status: string; note?: string; actor: string; occurredOnUtc: string }>;
};

type Action = 'approve' | 'reject' | 'receive-return' | 'refund' | 'ship-replacement' | 'complete' | 'dispose-return' | 'reserve-stock';
type Editor = { record: AfterSalesSummary; action: Action; lines: AfterSalesDetail['lines'] };

const PAGE_SIZE = 20;
const RETURN_ADDRESS_KEY = 'bm.afterSales.returnAddress';
const statuses = ['Requested', 'AwaitingReturn', 'ReturnInTransit', 'ReturnReceived', 'Approved', 'Refunding', 'RefundFailed', 'ReplacementShipped', 'Refunded', 'Completed', 'Rejected', 'Cancelled'];
const carriers = [['SF', '顺丰速运'], ['JD', '京东物流'], ['YTO', '圆通速递'], ['ZTO', '中通快递'], ['STO', '申通快递'], ['EMS', 'EMS']] as const;
const reasonLabels: Record<string, string> = {
  NoLongerNeeded: '不想要了（7 天无理由）', QualityIssue: '商品质量问题', DamagedInTransit: '运输途中损坏', WrongItem: '商家发错货',
  NotAsDescribed: '与商品描述不符', MissingParts: '少件 / 配件缺失', NotReceived: '未收到货 / 拒收', Other: '其他原因',
};
const stockLabels: Record<string, string> = { Reserving: '锁定中', Reserved: '已锁定', Insufficient: '库存不足', Committed: '已出库', Released: '已释放' };
const dispositionLabels: Record<string, string> = { Pending: '待入库处理', Restocked: '已全部入库', PartiallyRestocked: '部分入库', NotRestocked: '未入库', ReturnedToCustomer: '退回买家' };
/** What the ordering service does on its own once the deadline passes. */
const autoActions: Record<string, string> = { Requested: '自动同意', AwaitingReturn: '自动关闭', ReturnInTransit: '自动收货并退款', ReturnReceived: '自动退款', ReplacementShipped: '自动完成' };
// Queue chips filter on warehouse state rather than on the request status.
const queues: Record<string, { label: string; params: Record<string, string> }> = {
  restock: { label: '待入库处理', params: { disposition: 'Pending' } },
  stock: { label: '新品缺货', params: { replacementStock: 'Insufficient' } },
};
const isRefundType = (type: string) => type === 'RefundOnly' || type === 'ReturnAndRefund';
const needsReturn = (type: string) => type === 'ReturnAndRefund' || type === 'Exchange';

/** Mirrors the ordering service's state machine; the server stays authoritative. */
function actionsFor(record: Pick<AfterSalesSummary, 'type' | 'status' | 'replacementStock' | 'returnDisposition'>): Action[] {
  const ship: Action[] = record.replacementStock === 'Reserved' ? ['ship-replacement'] : record.replacementStock === 'Insufficient' ? ['reserve-stock'] : [];
  const reject: Action[] = record.returnDisposition === 'Pending' ? ['reject'] : [];
  const byStatus: Record<string, Action[]> = {
    Requested: ['approve', 'reject'],
    AwaitingReturn: record.replacementStock === 'Insufficient' ? ['receive-return', 'reserve-stock'] : ['receive-return'],
    ReturnInTransit: ['receive-return'],
    ReturnReceived: record.type === 'Exchange' ? [...ship, ...reject] : ['refund', ...reject],
    Approved: ship,
    RefundFailed: ['refund'],
    ReplacementShipped: ['complete'],
  };
  const actions = byStatus[record.status] ?? [];
  // 退货入库独立于退款 / 换新进度：签收后随时可做，但只做一次。
  return record.returnDisposition === 'Pending' ? [...actions, 'dispose-return'] : actions;
}

function actionLabel(action: Action, record: Pick<AfterSalesSummary, 'type' | 'status'>) {
  switch (action) {
    case 'approve': return record.type === 'RefundOnly' ? '同意并退款' : '审核通过';
    case 'reject': return record.status === 'ReturnReceived' ? '验货不通过' : '拒绝申请';
    case 'receive-return': return '确认收到退货';
    case 'refund': return record.status === 'RefundFailed' ? '重新发起退款' : '执行退款';
    case 'ship-replacement': return record.type === 'Exchange' ? '发出换货商品' : '发出补寄商品';
    case 'complete': return '确认完成';
    case 'dispose-return': return '退货入库处理';
    case 'reserve-stock': return '重新锁定新品库存';
  }
}

function rememberedReturnAddress() {
  try { return window.localStorage.getItem(RETURN_ADDRESS_KEY) ?? ''; } catch { return ''; }
}

export default function AfterSalesPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const [status, setStatus] = useUrlState('status', 'Requested');
  const [search, setSearch] = useUrlState('q');
  const [orderId, setOrderId] = useUrlState('orderId');
  const [detailId, setDetailId] = useUrlState('id', '', { resetPage: false });
  const [page, setPage] = usePage();
  const [editor, setEditor] = useState<Editor | null>(null);
  const canQuery = can('order.after-sales.query');
  const canEdit = can('order.after-sales.edit');

  const listQuery = useQuery({
    queryKey: ['after-sales', 'list', { status, search, orderId, page }],
    // One extra row tells us whether a next page exists without a total count.
    queryFn: ({ signal }) => api<AfterSalesSummary[]>(`/api/order/admin/after-sales?${query({ status: status === 'all' || queues[status] ? '' : status, ...queues[status]?.params, search, orderId, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE + 1 })}`, { signal }),
    enabled: canQuery,
    placeholderData: keepPreviousData,
  });
  const detailQuery = useQuery({
    queryKey: ['after-sales', 'detail', detailId],
    queryFn: () => api<AfterSalesDetail>(`/api/order/admin/after-sales/${detailId}`),
    enabled: Boolean(detailId) && canQuery,
  });
  const mutation = useMutation({
    mutationFn: ({ editor: target, body }: { editor: Editor; body: unknown }) =>
      api<AfterSalesDetail>(`/api/order/admin/after-sales/${encodeURIComponent(target.record.id)}/${target.action}`, { method: 'POST', body }),
    onSuccess: (result, { editor: target }) => {
      toast.success(`${target.record.requestNumber} 已更新为「${statusLabel(result.status)}」`);
      setEditor(null);
      client.setQueryData(['after-sales', 'detail', result.id], result);
      void client.invalidateQueries({ queryKey: ['after-sales', 'list'] });
    },
  });

  const rows = listQuery.data?.slice(0, PAGE_SIZE) ?? [];
  const chips = [{ value: 'all', label: '全部' }, ...statuses.map((value) => ({ value, label: statusLabel(value) })), ...Object.entries(queues).map(([value, queue]) => ({ value, label: queue.label }))];

  const openEditor = (record: AfterSalesSummary | AfterSalesDetail, action: Action) => {
    mutation.reset();
    setEditor({ record, action, lines: 'lines' in record ? record.lines : [] });
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editor) return;
    const values = new FormData(event.currentTarget);
    const text = (name: string) => String(values.get(name) ?? '').trim() || null;
    let body: unknown = { note: text('note') };
    if (editor.action === 'approve') {
      const returnAddress = text('returnAddress');
      if (returnAddress) try { window.localStorage.setItem(RETURN_ADDRESS_KEY, returnAddress); } catch { /* preference only */ }
      body = { returnAddress, note: text('note') };
    } else if (editor.action === 'reject') {
      body = { reply: text('reply') };
    } else if (editor.action === 'dispose-return') {
      const lines = editor.lines.map((line) => ({ sku: line.sku, quantity: Number(values.get(`restock:${line.sku}`) ?? 0) }));
      body = { lines, note: text('note') };
    } else if (editor.action === 'reserve-stock') {
      body = undefined;
    } else if (editor.action === 'ship-replacement') {
      const [carrierCode, carrierName] = String(values.get('carrier') ?? '').split('|');
      body = { carrierCode, carrierName, trackingNumber: text('trackingNumber')?.toUpperCase(), note: text('note') };
    }
    mutation.mutate({ editor, body });
  };

  const filtered = Boolean(search || orderId || status !== 'Requested');
  const record = editor?.record;

  return <div className="page">
    <PageHeader eyebrow="ORDER / AFTER-SALES" title="售后处理" description="审核退款、退货、换货与补寄申请；拒绝时的回复会直接展示给买家。默认显示待审核队列。"
      actions={<button className="button button--quiet" type="button" onClick={() => void client.invalidateQueries({ queryKey: ['after-sales'] })} disabled={!canQuery || listQuery.isFetching}><Icon name="refresh" className={listQuery.isFetching ? 'is-spinning' : undefined} />刷新</button>} />

    {!canQuery ? <Empty title="没有售后查询权限" detail="需要 order.after-sales.query 才能查看售后单。" /> : <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="售后状态" chips={chips} value={status} onChange={setStatus} />
        <div className="list-toolbar__filters">
          <SearchInput value={search} onChange={setSearch} placeholder="售后单号 / 订单号 / 退货运单号 / 用户 ID" />
          {orderId ? <button className="filter-pill" type="button" onClick={() => setOrderId('')}>订单 {orderId.slice(0, 8)}… <Icon name="close" /></button> : null}
        </div>
      </div>
      {listQuery.isError ? <ErrorNotice error={listQuery.error} onRetry={() => void listQuery.refetch()} /> : null}
      {listQuery.isPending ? <SkeletonRows rows={8} /> : rows.length ? <div className={`data-table${listQuery.isPlaceholderData ? ' is-stale' : ''}`}><table>
        <thead><tr><th>售后单</th><th>类型</th><th>商品</th><th>退款金额</th><th>状态</th><th>申请时间</th><th>操作</th></tr></thead>
        <tbody>{rows.map((row) => {
          const [primary] = actionsFor(row);
          return <tr key={row.id} className="is-clickable" onClick={() => setDetailId(row.id)}>
            <td><span className="cell-with-copy"><button className="table-link" type="button" onClick={(event) => { event.stopPropagation(); setDetailId(row.id); }}>{row.requestNumber}</button><CopyButton value={row.requestNumber} label="复制售后单号" /></span><small>订单 {row.orderNumber}</small></td>
            <td>{statusLabel(row.type)}</td>
            <td><strong>{row.productNames.join('、')}</strong></td>
            <td>{isRefundType(row.type) ? <strong>{formatMoney(row.refundAmount, row.currency)}</strong> : '—'}</td>
            <td><Status>{row.status}</Status>{row.returnDisposition === 'Pending' ? <small>待入库处理</small> : row.replacementStock === 'Insufficient' ? <small>新品库存不足</small> : row.deadlineOnUtc ? <small title={formatDate(row.deadlineOnUtc)}>{autoActions[row.status]} · {formatRelative(row.deadlineOnUtc)}</small> : null}</td>
            <td title={formatDate(row.createdOnUtc)}>{formatRelative(row.createdOnUtc)}<small>{formatDate(row.createdOnUtc)}</small></td>
            <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
              {primary && primary !== 'dispose-return' && canEdit ? <button className="row-action-primary" type="button" onClick={() => openEditor(row, primary)}>{actionLabel(primary, row)}</button> : <button type="button" onClick={() => setDetailId(row.id)}>详情</button>}
            </div></td>
          </tr>;
        })}</tbody>
      </table></div> : <Empty title={status === 'Requested' && !search && !orderId ? '没有待审核的售后申请' : '暂无匹配售后单'} detail={filtered ? '试试切换状态或清空搜索。' : '新的售后申请会出现在这里。'} action={filtered ? <button className="button button--quiet" type="button" onClick={() => { setStatus('all'); setSearch(''); setOrderId(''); }}>查看全部</button> : undefined} />}
      <Pagination page={page} pageSize={PAGE_SIZE} hasMore={(listQuery.data?.length ?? 0) > PAGE_SIZE} onChange={setPage} busy={listQuery.isFetching} />
    </section>}

    <Modal open={editor !== null} title={editor && record ? actionLabel(editor.action, record) : '售后处理'} description={record ? `${record.requestNumber} · ${statusLabel(record.type)} · 当前「${statusLabel(record.status)}」` : undefined} onClose={() => setEditor(null)}>
      {editor && record ? <form className="modal-form" onSubmit={submit}>
        <div className="order-context">
          <div><span>关联订单</span><strong>{record.orderNumber}</strong></div>
          <div><span>商品</span><strong>{record.productNames.join('、')}</strong></div>
          <div><span>{isRefundType(record.type) ? '退款金额（原路退回）' : '退款金额'}</span><strong>{isRefundType(record.type) ? formatMoney(record.refundAmount, record.currency) : '不退款'}</strong></div>
        </div>
        {editor.action === 'approve' && needsReturn(record.type)
          ? <Field label="退货地址" hint="买家按此地址寄回，默认填入上次使用的地址"><textarea name="returnAddress" required maxLength={500} rows={3} defaultValue={rememberedReturnAddress()} placeholder="收件人、电话、详细地址" /></Field>
          : null}
        {editor.action === 'reject'
          ? <Field label="回复买家" hint="买家会在售后详情中看到这段回复"><textarea name="reply" required maxLength={500} rows={4} autoFocus placeholder={record.status === 'ReturnReceived' ? '例如：退回商品有明显使用痕迹，不符合退货条件，商品将原路寄回。' : '例如：商品已超过无理由退货期限。'} /></Field>
          : null}
        {editor.action === 'ship-replacement' ? <div className="form-row">
          <Field label="承运商"><select name="carrier" required defaultValue="SF|顺丰速运">{carriers.map(([code, name]) => <option key={code} value={`${code}|${name}`}>{name}</option>)}</select></Field>
          <Field label="物流单号"><input name="trackingNumber" required maxLength={120} autoFocus autoComplete="off" placeholder="扫描或输入运单号" pattern="[A-Za-z0-9\-]{6,120}" title="6–120 位字母、数字或连字符" /></Field>
        </div> : null}
        {editor.action === 'dispose-return' ? <div className="compact-list">{editor.lines.map((line) => <article key={line.sku}>
          <div><strong>{line.productName}</strong><small>{line.variantName} · {line.sku} · 退回 {line.quantity} 件</small></div>
          <Field label="入库件数"><input name={`restock:${line.sku}`} type="number" min={0} max={line.quantity} step={1} defaultValue={line.quantity} required /></Field>
        </article>)}</div> : null}
        {editor.action === 'dispose-return'
          ? <Field label="处理说明" hint="有商品不入库时必填，例如：外壳破损、缺少配件、影响二次销售"><textarea name="note" maxLength={500} rows={2} /></Field>
          : editor.action !== 'reject' && editor.action !== 'reserve-stock' ? <Field label="处理说明" hint="可选，会显示在买家的售后进度中"><textarea name="note" maxLength={1000} rows={2} /></Field> : null}
        {editor.action === 'reserve-stock' ? <p className="muted-copy">向库存服务重新申请锁定换货 / 补寄所需的新品，请在补货后执行。</p> : null}
        {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
        <SubmitBar busy={mutation.isPending} label={actionLabel(editor.action, record)} onCancel={() => setEditor(null)} />
      </form> : null}
    </Modal>

    <Modal open={Boolean(detailId)} wide title={detailQuery.data?.requestNumber ?? '售后详情'} description="申请内容、凭证、物流与处理记录" onClose={() => setDetailId('')}>
      {detailQuery.isPending ? <Loading label="正在读取售后单" /> : null}
      {detailQuery.isError ? <div className="modal-form"><ErrorNotice error={detailQuery.error} onRetry={() => void detailQuery.refetch()} /></div> : null}
      {detailQuery.data ? <AfterSalesDetailPanel record={detailQuery.data} canEdit={canEdit} canViewOrder={can('order.fulfillment.query')} onAction={(action) => openEditor(detailQuery.data!, action)} /> : null}
    </Modal>
  </div>;
}

function AfterSalesDetailPanel({ record, canEdit, canViewOrder, onAction }: { record: AfterSalesDetail; canEdit: boolean; canViewOrder: boolean; onAction: (action: Action) => void }) {
  const actions = canEdit ? actionsFor(record) : [];
  return <div className="order-detail">
    <div className="order-detail__summary">
      <div><span>{isRefundType(record.type) ? '退款金额' : '售后方式'}</span><strong>{isRefundType(record.type) ? formatMoney(record.refundAmount, record.currency) : statusLabel(record.type)}</strong><small>{statusLabel(record.type)} · {reasonLabels[record.reasonCode] ?? record.reasonCode}</small></div>
      <div><span>状态</span><p><Status>{record.status}</Status></p><small>申请于 {formatDate(record.createdOnUtc)} · 用户 {record.userId.slice(0, 8)}…{record.deadlineOnUtc ? ` · ${formatDate(record.deadlineOnUtc)} 起${autoActions[record.status]}` : ''}</small></div>
    </div>
    <div className="detail-actions">
      {actions.map((action, index) => <button key={action} className={index === 0 ? 'button button--dark' : 'button button--quiet'} type="button" onClick={() => onAction(action)}>{actionLabel(action, record)}</button>)}
      {canViewOrder ? <Link className="button button--quiet" to={`/orders?order=${record.orderId}`}><Icon name="order" />查看订单</Link> : null}
      <span className="mono">{record.orderNumber}<CopyButton value={record.orderNumber} label="复制订单号" /></span>
    </div>
    {record.warehouse && record.warehouse.replacementStock !== 'NotRequired' ? <section><h3>新品库存</h3><p>{stockLabels[record.warehouse.replacementStock] ?? record.warehouse.replacementStock}{record.warehouse.replacementStockFailure ? ` · ${record.warehouse.replacementStockFailure}` : ''}</p><small>换货 / 补寄在审核通过时锁定新品，发出时出库，售后关闭时释放。</small></section> : null}
    {record.warehouse && record.warehouse.returnDisposition !== 'NotApplicable' ? <section><h3>退货入库</h3><p>{dispositionLabels[record.warehouse.returnDisposition] ?? record.warehouse.returnDisposition}{record.warehouse.dispositionNote ? ` · ${record.warehouse.dispositionNote}` : ''}</p>{record.warehouse.disposedOnUtc ? <small>{formatDate(record.warehouse.disposedOnUtc)} · {record.warehouse.disposedBy}</small> : null}</section> : null}
    {record.refundFailureReason ? <section><h3>退款失败原因</h3><p>{record.refundFailureReason}</p><small>处理后可点击「重新发起退款」。</small></section> : null}
    {record.rejectionReason ? <section><h3>给买家的回复</h3><p>{record.rejectionReason}</p></section> : null}
    <section><h3>买家描述</h3><p>{record.description || '买家未填写补充说明。'}</p>
      {record.evidenceUrls.length ? <div className="evidence-grid">{record.evidenceUrls.map((url, index) => <a key={url} href={url} target="_blank" rel="noreferrer"><img src={url} alt={`凭证 ${index + 1}`} loading="lazy" /></a>)}</div> : <small>未上传凭证图片。</small>}
    </section>
    <section><h3>售后商品</h3><div className="compact-list">{record.lines.map((line) => <article key={line.sku}>
      <div><strong>{line.productName}</strong><small>{line.variantName} · {line.sku}</small></div>
      <span>× {line.quantity}{line.restockedQuantity != null ? ` · 入库 ${line.restockedQuantity}` : ''}</span><strong>{isRefundType(record.type) ? formatMoney(line.refundAmount, record.currency) : '—'}</strong>
    </article>)}</div></section>
    {record.returnAddress ? <section><h3>退货地址</h3><p>{record.returnAddress}</p></section> : null}
    {record.returnShipment ? <section><h3>买家寄回</h3><p className="cell-with-copy">{record.returnShipment.carrierName} · <span className="mono">{record.returnShipment.trackingNumber}</span><CopyButton value={record.returnShipment.trackingNumber} label="复制运单号" /></p><small>{formatDate(record.returnShipment.shippedOnUtc)}</small></section> : null}
    {record.replacementShipment ? <section><h3>商家发出</h3><p className="cell-with-copy">{record.replacementShipment.carrierName} · <span className="mono">{record.replacementShipment.trackingNumber}</span><CopyButton value={record.replacementShipment.trackingNumber} label="复制运单号" /></p><small>{formatDate(record.replacementShipment.shippedOnUtc)}</small></section> : null}
    <section><h3>处理记录</h3><ol className="timeline">{[...record.history].reverse().map((item, index) => <li key={`${item.occurredOnUtc}-${index}`}><i /><div><strong>{statusLabel(item.status)}</strong><small>{formatDate(item.occurredOnUtc)} · {item.actor}</small>{item.note ? <p>{item.note}</p> : null}</div></li>)}</ol></section>
  </div>;
}
