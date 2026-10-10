import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent } from 'react';
import { api, idempotencyKey, query, type Paged } from '../api';
import { LOW_STOCK_THRESHOLD, type StockRow } from '../queries';
import { useSession } from '../session';
import { asLocalInput, ChipFilter, Empty, ErrorNotice, Field, formatDate, formatRelative, Icon, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, useConfirm, usePage, useToast, useUrlState } from '../ui';

type Movement = { id: string; sku: string; type: string; onHandDelta: number; reservedDelta: number; onHandAfter: number; reservedAfter: number; reason: string; operatorId?: string; occurredOnUtc: string };
type Allocation = { allocationId: string; offerId: string; sku: string; state: string; totalQuantity: number; reservedQuantity: number; soldQuantity: number; releasedQuantity: number; availableQuantity: number; perUserLimit: number; startsOnUtc: string; endsOnUtc: string };
type Variant = { id: string; sku: string; displayName: string; isActive: boolean };
type Product = { id: string; name: string; variants?: Variant[] };
type Promotion = { id: string; code: string; name: string; status: string };
type Operation = { kind: 'add' | 'adjust' | 'allocation'; sku?: string } | null;

const PAGE_SIZE = 25;
const movementLabels: Record<string, string> = {
  StockAdded: '入库', StockAdjusted: '人工调整', InventoryReserved: '预占', InventoryCommitted: '出库扣减',
  InventoryReleased: '预占释放', AllocationCreated: '活动圈定', AllocationReleased: '活动释放', ReturnRestocked: '退货入库',
};

export default function InventoryPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [tab, setTab] = useUrlState('tab', 'stock');
  const [search, setSearch] = useUrlState('q');
  const [low, setLow] = useUrlState('low');
  const [ledgerSku, setLedgerSku] = useUrlState('sku', '', { resetPage: false });
  const [allocationStatus, setAllocationStatus] = useUrlState('status');
  const [page, setPage] = usePage();
  const [selectedAllocation, setSelectedAllocation] = useState<Allocation | null>(null);
  const [operation, setOperation] = useState<Operation>(null);

  const stockQuery = useQuery({
    queryKey: ['inventory', 'stock', { search, low, page }],
    queryFn: ({ signal }) => api<Paged<StockRow>>(`/api/inventory/admin/stock?${query({ search, lowStockThreshold: low ? LOW_STOCK_THRESHOLD : null, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE })}`, { signal }),
    enabled: tab === 'stock' && can('inventory.stock.query'),
    placeholderData: keepPreviousData,
  });
  const allocationQuery = useQuery({
    queryKey: ['inventory', 'allocations', { allocationStatus, search, page }],
    queryFn: ({ signal }) => api<Paged<Allocation>>(`/api/inventory/admin/allocations?${query({ status: allocationStatus, sku: search, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE })}`, { signal }),
    enabled: tab === 'allocations' && can('inventory.allocations.query'),
    placeholderData: keepPreviousData,
  });
  const movementsQuery = useQuery({
    queryKey: ['inventory', 'movements', ledgerSku],
    queryFn: () => api<Movement[]>(`/api/inventory/admin/stock/${encodeURIComponent(ledgerSku)}/movements?take=100`),
    enabled: Boolean(ledgerSku) && can('inventory.stock.query'),
  });
  const productsQuery = useQuery({
    queryKey: ['catalog', 'product-options'],
    queryFn: () => api<Paged<Product>>('/api/catalog/admin/products?skip=0&take=200'),
    enabled: operation !== null && can('catalog.products.query'),
    staleTime: 60_000,
  });
  const promotionsQuery = useQuery({
    queryKey: ['catalog', 'promotion-options'],
    queryFn: () => api<Paged<Promotion>>('/api/catalog/admin/promotions?skip=0&take=200'),
    enabled: can('catalog.promotions.query'),
    staleTime: 60_000,
  });

  const skuOptions = useMemo(() => productsQuery.data?.items.flatMap((product) => (product.variants ?? []).map((variant) => ({ ...variant, productName: product.name }))) ?? [], [productsQuery.data]);
  const promotionNames = useMemo(() => new Map(promotionsQuery.data?.items.map((promotion) => [promotion.id, promotion]) ?? []), [promotionsQuery.data]);

  const mutation = useMutation({
    mutationFn: ({ path, body, headers }: { path: string; body?: unknown; headers?: HeadersInit; success: string }) => api<unknown>(path, { method: 'POST', body, headers }),
    onSuccess: (_, { success }) => {
      toast.success(success);
      setOperation(null);
      setSelectedAllocation(null);
      void client.invalidateQueries({ queryKey: ['inventory'] });
    },
  });

  const closeAllocation = async (row: Allocation) => {
    const offer = promotionNames.get(row.offerId);
    const result = await confirm({ title: `关闭 ${row.sku} 的活动配额？`, detail: `${offer?.name ?? '该活动'}将不再接受抢购；未售出的 ${row.availableQuantity} 件会退回普通库存。`, confirmLabel: '关闭配额', tone: 'danger' });
    if (!result) return;
    mutation.mutate({ path: `/api/inventory/admin/allocations/${row.allocationId}/close`, success: `已关闭 ${row.sku} 的活动配额` }, { onError: (error) => toast.error(error) });
  };

  const openLedger = (sku: string) => {
    setLedgerSku(sku);
    window.requestAnimationFrame(() => document.getElementById('inventory-ledger')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  };

  const stockRows = stockQuery.data?.items ?? [];
  const allocationRows = allocationQuery.data?.items ?? [];

  return <div className="page">
    <PageHeader eyebrow="INVENTORY / CONTROL" title="库存与活动配额" description="普通订单和活动订单共用库存主链路；活动配额只负责为指定优惠与 SKU 圈定可售数量。"
      actions={<div className="button-group">
        {tab === 'stock' && can('inventory.stock.create') ? <button className="button button--quiet" type="button" onClick={() => setOperation({ kind: 'add' })}><Icon name="plus" />商品入库</button> : null}
        {tab === 'stock' && can('inventory.stock.edit') ? <button className="button button--dark" type="button" onClick={() => setOperation({ kind: 'adjust', sku: ledgerSku || undefined })}>调整库存</button> : null}
        {tab === 'allocations' && can('inventory.allocations.create') ? <button className="button button--dark" type="button" onClick={() => setOperation({ kind: 'allocation' })}><Icon name="plus" />创建活动配额</button> : null}
      </div>} />

    <div className="catalog-toolbar">
      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'stock'} onClick={() => setTab('stock')}>SKU 库存</button>
        <button type="button" role="tab" aria-selected={tab === 'allocations'} onClick={() => setTab('allocations')}>活动配额</button>
      </div>
    </div>

    {tab === 'stock' ? <>
      <section className="panel table-panel">
        <div className="list-toolbar">
          <ChipFilter label="库存筛选" value={low} onChange={setLow} chips={[{ value: '', label: '全部 SKU' }, { value: '1', label: `低库存 ≤ ${LOW_STOCK_THRESHOLD}` }]} />
          <div className="list-toolbar__filters"><SearchInput value={search} onChange={setSearch} placeholder="搜索 SKU" /><span className="list-toolbar__count">{stockQuery.data?.total ?? 0} 个 SKU</span></div>
        </div>
        {stockQuery.isError ? <ErrorNotice error={stockQuery.error} onRetry={() => void stockQuery.refetch()} /> : null}
        {!can('inventory.stock.query') ? <Empty title="没有库存查询权限" detail="需要 inventory.stock.query 才能查看库存。" />
          : stockQuery.isPending ? <SkeletonRows rows={8} />
            : stockRows.length ? <div className={`data-table${stockQuery.isPlaceholderData ? ' is-stale' : ''}`}><table><thead><tr><th>SKU</th><th>在库</th><th>预留</th><th>可用</th><th>占用率</th><th>操作</th></tr></thead><tbody>{stockRows.map((row) => {
              const reservedRatio = row.onHand ? row.reserved / row.onHand : 0;
              return <tr key={row.id} className={ledgerSku === row.sku ? 'is-selected' : undefined}>
                <td><strong className="mono">{row.sku}</strong><small>v{row.version}</small></td>
                <td>{row.onHand.toLocaleString('zh-CN')}</td>
                <td>{row.reserved.toLocaleString('zh-CN')}</td>
                <td className={row.available === 0 ? 'number-bad' : row.available <= LOW_STOCK_THRESHOLD ? 'number-warn' : 'number-good'}><strong>{row.available.toLocaleString('zh-CN')}</strong>{row.available <= LOW_STOCK_THRESHOLD ? <small>{row.available === 0 ? '已售罄' : '低库存'}</small> : null}</td>
                <td><span className="mini-bar" title={`${(reservedRatio * 100).toFixed(0)}% 已预留`}><i style={{ width: `${Math.min(100, reservedRatio * 100)}%` }} /></span></td>
                <td><div className="row-actions"><button type="button" onClick={() => openLedger(row.sku)}>流水</button>{can('inventory.stock.edit') ? <button className="row-action-primary" type="button" onClick={() => setOperation({ kind: 'adjust', sku: row.sku })}>调整</button> : null}</div></td>
              </tr>;
            })}</tbody></table></div>
              : <Empty title={low || search ? '没有匹配的 SKU' : '暂无库存'} detail={low || search ? '调整筛选条件后重试。' : '从商品目录选择 SKU 完成第一次入库。'} />}
        <Pagination page={page} pageSize={PAGE_SIZE} total={stockQuery.data?.total} onChange={setPage} busy={stockQuery.isFetching} />
      </section>

      <section id="inventory-ledger" className="panel table-panel inventory-ledger">
        <div className="section-heading"><div><p className="eyebrow">STOCK LEDGER</p><h2>{ledgerSku ? `${ledgerSku} 的库存流水` : '库存流水'}</h2></div>{ledgerSku ? <button className="button button--quiet" type="button" onClick={() => setLedgerSku('')}><Icon name="close" />关闭</button> : <span>选择 SKU 查看</span>}</div>
        {movementsQuery.isError ? <ErrorNotice error={movementsQuery.error} /> : null}
        {!ledgerSku ? <Empty title="选择一个 SKU" detail="在库存列表点击「流水」查看该 SKU 的完整审计记录。" />
          : movementsQuery.isPending ? <SkeletonRows rows={5} />
            : movementsQuery.data?.length ? <div className="data-table"><table><thead><tr><th>发生时间</th><th>类型</th><th>在库变化</th><th>预留变化</th><th>变化后</th><th>原因</th><th>操作人</th></tr></thead><tbody>{movementsQuery.data.map((row) => <tr key={row.id}>
              <td title={formatDate(row.occurredOnUtc)}>{formatRelative(row.occurredOnUtc)}<small>{formatDate(row.occurredOnUtc)}</small></td>
              <td><span className="tag">{movementLabels[row.type] ?? row.type}</span></td>
              <td className={row.onHandDelta > 0 ? 'number-good' : row.onHandDelta < 0 ? 'number-bad' : undefined}>{row.onHandDelta > 0 ? '+' : ''}{row.onHandDelta}</td>
              <td>{row.reservedDelta > 0 ? '+' : ''}{row.reservedDelta}</td>
              <td><strong>{row.onHandAfter}</strong><small>可用 {row.onHandAfter - row.reservedAfter}</small></td>
              <td className="cell-wrap">{row.reason}</td>
              <td className="mono">{row.operatorId ?? 'SYSTEM'}</td>
            </tr>)}</tbody></table></div> : <Empty title="暂无流水" detail="该 SKU 还没有库存变化记录。" />}
      </section>
    </> : <>
      <section className="panel table-panel">
        <div className="list-toolbar">
          <ChipFilter label="配额状态" value={allocationStatus} onChange={setAllocationStatus} chips={[{ value: '', label: '全部' }, { value: 'Open', label: '开放' }, { value: 'Closed', label: '已关闭' }]} />
          <div className="list-toolbar__filters"><SearchInput value={search} onChange={setSearch} placeholder="搜索 SKU" /><span className="list-toolbar__count">{allocationQuery.data?.total ?? 0} 个</span></div>
        </div>
        {allocationQuery.isError ? <ErrorNotice error={allocationQuery.error} onRetry={() => void allocationQuery.refetch()} /> : null}
        {!can('inventory.allocations.query') ? <Empty title="没有配额查询权限" detail="需要 inventory.allocations.query 才能查看活动配额。" />
          : allocationQuery.isPending ? <SkeletonRows rows={6} />
            : allocationRows.length ? <div className="data-table"><table><thead><tr><th>优惠活动</th><th>SKU</th><th>状态</th><th>售卖进度</th><th>用户限购</th><th>活动周期</th><th>操作</th></tr></thead><tbody>{allocationRows.map((row) => {
              const offer = promotionNames.get(row.offerId);
              const sold = row.totalQuantity - row.availableQuantity - row.releasedQuantity;
              return <tr key={row.allocationId} className="is-clickable" onClick={() => setSelectedAllocation(row)}>
                <td><strong>{offer?.name ?? '未读取到活动名称'}</strong><small>{offer?.code ?? row.offerId}</small></td>
                <td className="mono"><strong>{row.sku}</strong></td>
                <td><Status>{row.state}</Status></td>
                <td><span className="mini-bar mini-bar--wide"><i style={{ width: `${row.totalQuantity ? sold / row.totalQuantity * 100 : 0}%` }} /></span><small>剩余 {row.availableQuantity} / {row.totalQuantity}</small></td>
                <td>{row.perUserLimit} 件</td>
                <td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)}</small></td>
                <td onClick={(event) => event.stopPropagation()}><div className="row-actions"><button type="button" onClick={() => setSelectedAllocation(row)}>详情</button>{row.state !== 'Closed' && can('inventory.allocations.edit') ? <button className="danger-link" type="button" onClick={() => void closeAllocation(row)}>关闭</button> : null}</div></td>
              </tr>;
            })}</tbody></table></div> : <Empty title="暂无活动配额" detail="需要活动限量时，为已有促销和具体 SKU 创建配额；普通商品无需创建。" />}
        <Pagination page={page} pageSize={PAGE_SIZE} total={allocationQuery.data?.total} onChange={setPage} busy={allocationQuery.isFetching} />
      </section>
    </>}

    <Modal open={selectedAllocation !== null} title={selectedAllocation ? promotionNames.get(selectedAllocation.offerId)?.name ?? selectedAllocation.sku : '配额详情'} description={selectedAllocation ? `${selectedAllocation.sku} · ${statusLabel(selectedAllocation.state)}` : undefined} onClose={() => setSelectedAllocation(null)}>
      {selectedAllocation ? <div className="allocation-detail">
        <div className="allocation-detail__headline"><div><p className="eyebrow">ALLOCATION DETAIL</p><Status>{selectedAllocation.state}</Status></div><strong>{selectedAllocation.availableQuantity}<small>剩余可售</small></strong></div>
        <div className="quota-bar quota-bar--stacked" aria-label="配额分布">
          <i className="tone--good" style={{ flex: selectedAllocation.soldQuantity }} title={`已售 ${selectedAllocation.soldQuantity}`} />
          <i className="tone--warn" style={{ flex: selectedAllocation.reservedQuantity }} title={`已预留 ${selectedAllocation.reservedQuantity}`} />
          <i className="tone--neutral" style={{ flex: selectedAllocation.releasedQuantity }} title={`已释放 ${selectedAllocation.releasedQuantity}`} />
          <i className="tone--empty" style={{ flex: selectedAllocation.availableQuantity }} title={`剩余 ${selectedAllocation.availableQuantity}`} />
        </div>
        <div className="quota-metrics"><span><small>配额总量</small><strong>{selectedAllocation.totalQuantity}</strong></span><span><small>已售</small><strong>{selectedAllocation.soldQuantity}</strong></span><span><small>已预留</small><strong>{selectedAllocation.reservedQuantity}</strong></span><span><small>已释放</small><strong>{selectedAllocation.releasedQuantity}</strong></span></div>
        <p>每位用户最多 {selectedAllocation.perUserLimit} 件 · {formatDate(selectedAllocation.startsOnUtc)} 至 {formatDate(selectedAllocation.endsOnUtc)}</p>
        {selectedAllocation.state !== 'Closed' && can('inventory.allocations.edit') ? <div className="submit-bar"><button className="button button--danger" type="button" onClick={() => void closeAllocation(selectedAllocation)}>关闭配额</button></div> : null}
      </div> : null}
    </Modal>

    <Modal open={operation !== null} title={operation?.kind === 'add' ? '商品入库' : operation?.kind === 'adjust' ? '审计库存调整' : '创建活动库存配额'} description={operation?.kind === 'allocation' ? '配额绑定已有优惠活动和一个可售 SKU，会从普通库存中圈定对应数量。' : operation?.kind === 'adjust' ? '调整会写入审计流水，重复提交由幂等键去重。' : undefined} onClose={() => { setOperation(null); mutation.reset(); }}>
      {operation ? <OperationForm operation={operation} stockRows={stockRows} skuOptions={skuOptions} promotions={promotionsQuery.data?.items ?? []} canReadProducts={can('catalog.products.query')} canReadPromotions={can('catalog.promotions.query')} busy={mutation.isPending} error={mutation.error}
        onCancel={() => setOperation(null)} onSubmit={(request) => mutation.mutate(request)} /> : null}
    </Modal>
  </div>;
}

function OperationForm({ operation, stockRows, skuOptions, promotions, canReadProducts, canReadPromotions, busy, error, onSubmit, onCancel }: {
  operation: NonNullable<Operation>;
  stockRows: StockRow[];
  skuOptions: Array<Variant & { productName: string }>;
  promotions: Promotion[];
  canReadProducts: boolean;
  canReadPromotions: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (request: { path: string; body: unknown; headers?: HeadersInit; success: string }) => void;
  onCancel: () => void;
}) {
  const [sku, setSku] = useState(operation.sku ?? '');
  const [quantity, setQuantity] = useState('');
  // A stable key per dialog lets a double-click resubmit the same adjustment instead of applying it twice.
  const [requestKey] = useState(idempotencyKey);
  const stock = useQuery({
    queryKey: ['inventory', 'availability', sku],
    queryFn: async () => (await api<Paged<StockRow>>(`/api/inventory/admin/stock?${query({ search: sku, take: 5 })}`)).items.find((item) => item.sku === sku) ?? null,
    enabled: Boolean(sku) && operation.kind !== 'allocation',
  });
  const current = stock.data ?? null;
  const delta = Number(quantity) || 0;

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (key: string) => String(data.get(key) ?? '').trim();
    if (operation.kind === 'add') onSubmit({ path: '/api/inventory/admin/stock', body: { sku, quantity: delta }, success: `${sku} 已入库 ${delta} 件` });
    if (operation.kind === 'adjust') onSubmit({ path: '/api/inventory/admin/stock/adjustments', body: { sku, quantityDelta: delta, reason: value('reason') }, headers: { 'Idempotency-Key': requestKey }, success: `${sku} 库存已${delta > 0 ? '增加' : '减少'} ${Math.abs(delta)} 件` });
    if (operation.kind === 'allocation') onSubmit({
      path: '/api/inventory/admin/allocations',
      body: { offerId: value('offerId'), sku, totalQuantity: delta, startsOnUtc: new Date(value('startsOnUtc')).toISOString(), endsOnUtc: new Date(value('endsOnUtc')).toISOString(), perUserLimit: Number(value('perUserLimit')) },
      success: `已为 ${sku} 创建 ${delta} 件活动配额`,
    });
  };

  const options = operation.kind === 'adjust'
    ? stockRows.map((item) => ({ value: item.sku, label: `${item.sku} · 可用 ${item.available}` }))
    : skuOptions.map((item) => ({ value: item.sku, label: `${item.productName} · ${item.displayName} · ${item.sku}${item.isActive ? '' : '（未启用）'}` }));
  if (sku && !options.some((option) => option.value === sku)) options.unshift({ value: sku, label: sku });
  const after = current ? { onHand: current.onHand + (operation.kind === 'allocation' ? 0 : delta), available: current.available + delta } : null;
  const invalidAdjust = operation.kind === 'adjust' && after !== null && after.onHand < current!.reserved;

  return <form className="modal-form" onSubmit={submit}>
    {operation.kind === 'allocation' ? <Field label="优惠活动" hint={!canReadPromotions ? '需要 catalog.promotions.query 才能选择活动' : undefined}><select name="offerId" required disabled={!canReadPromotions}><option value="">选择促销活动</option>{promotions.filter((promotion) => !['Ended', 'Archived'].includes(promotion.status)).map((promotion) => <option key={promotion.id} value={promotion.id}>{promotion.name} · {promotion.code} · {statusLabel(promotion.status)}</option>)}</select></Field> : null}
    <Field label="商品 SKU" hint={!canReadProducts && operation.kind !== 'adjust' ? '需要 catalog.products.query 才能从商品目录选择 SKU' : undefined}>
      <select required value={sku} onChange={(event) => setSku(event.target.value)} disabled={operation.kind !== 'adjust' && !canReadProducts}><option value="">选择 SKU</option>{options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select>
    </Field>
    <Field label={operation.kind === 'adjust' ? '数量变化' : operation.kind === 'allocation' ? '配额总量' : '入库数量'} hint={operation.kind === 'adjust' ? '正数增加，负数减少；不能低于已预留数量' : undefined}>
      <input type="number" required value={quantity} onChange={(event) => setQuantity(event.target.value)} min={operation.kind === 'adjust' ? undefined : 1} step="1" autoFocus={Boolean(operation.sku)} />
    </Field>
    {current && delta !== 0 && operation.kind !== 'allocation' ? <div className={`balance-preview${invalidAdjust ? ' is-invalid' : ''}`}>
      <span><small>当前在库</small><strong>{current.onHand}</strong></span><Icon name="arrow" /><span><small>调整后在库</small><strong>{after!.onHand}</strong></span>
      <span><small>调整后可用</small><strong>{after!.available}</strong></span>
      {invalidAdjust ? <p role="alert">调整后在库将低于已预留的 {current.reserved} 件，提交会被拒绝。</p> : null}
    </div> : null}
    {operation.kind === 'adjust' ? <Field label="调整原因"><textarea name="reason" required rows={3} maxLength={500} placeholder="说明盘点、损耗或人工校正原因" /></Field> : null}
    {operation.kind === 'allocation' ? <>
      <div className="form-row"><Field label="开始时间"><input name="startsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date())} required /></Field><Field label="结束时间"><input name="endsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date(Date.now() + 2 * 86_400_000))} required /></Field></div>
      <Field label="每位用户限购"><input name="perUserLimit" type="number" min="1" max={delta || 999} defaultValue="1" required /></Field>
    </> : null}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label={operation.kind === 'allocation' ? '创建配额' : '提交库存变更'} onCancel={onCancel} />
  </form>;
}
