import { useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, idempotencyKey } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, Icon, Modal, PageHeader, Status, SubmitBar } from '../ui';

type Movement = { id: string; sku: string; type: string; onHandDelta: number; reservedDelta: number; onHandAfter: number; reservedAfter: number; reason: string; operatorId?: string; occurredOnUtc: string };
type Stock = { id: string; sku: string; onHand: number; reserved: number; available: number; version: number };
type Allocation = { allocationId: string; offerId: string; sku: string; state: string; totalQuantity: number; reservedQuantity: number; soldQuantity: number; releasedQuantity: number; availableQuantity: number; perUserLimit: number; startsOnUtc: string; endsOnUtc: string };
type Variant = { id: string; sku: string; displayName: string; isActive: boolean; isDefault: boolean };
type Product = { id: string; name: string; lifecycleStatus: string; variants?: Variant[] };
type Promotion = { id: string; code: string; name: string; status: string; startsOnUtc: string; endsOnUtc: string };
type Page<T> = { items: T[]; skip: number; take: number; total: number };
type Operation = 'add' | 'adjust' | 'allocation' | null;

const localDate = (offsetDays: number) => {
  const date = new Date(Date.now() + offsetDays * 86_400_000);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};

export default function InventoryPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const [tab, setTab] = useState<'stock' | 'allocations'>('stock');
  const [sku, setSku] = useState('');
  const [allocationStatus, setAllocationStatus] = useState('');
  const [selectedAllocation, setSelectedAllocation] = useState<Allocation | null>(null);
  const [operation, setOperation] = useState<Operation>(null);

  const stockListQuery = useQuery({
    queryKey: ['inventory', 'stock'],
    queryFn: () => api<Page<Stock>>('/api/inventory/admin/stock?skip=0&take=100'),
    enabled: can('inventory.stock.query'),
  });
  const allocationListQuery = useQuery({
    queryKey: ['inventory', 'allocations', allocationStatus],
    queryFn: () => {
      const query = new URLSearchParams({ skip: '0', take: '100' });
      if (allocationStatus) query.set('status', allocationStatus);
      return api<Page<Allocation>>(`/api/inventory/admin/allocations?${query}`);
    },
    enabled: can('inventory.allocations.query'),
  });
  const movementsQuery = useQuery({
    queryKey: ['inventory', 'movements', sku],
    queryFn: () => api<Movement[]>(`/api/inventory/admin/stock/${encodeURIComponent(sku)}/movements?take=100`),
    enabled: Boolean(sku) && can('inventory.stock.query'),
  });
  const productsQuery = useQuery({
    queryKey: ['catalog', 'product-options'],
    queryFn: () => api<Page<Product>>('/api/catalog/admin/products?skip=0&take=200'),
    enabled: can('catalog.products.query'),
    staleTime: 60_000,
  });
  const promotionsQuery = useQuery({
    queryKey: ['catalog', 'promotion-options'],
    queryFn: () => api<Page<Promotion>>('/api/catalog/admin/promotions?skip=0&take=200'),
    enabled: can('catalog.promotions.query'),
    staleTime: 60_000,
  });

  const skuOptions = useMemo(() => productsQuery.data?.items.flatMap((product) =>
    (product.variants ?? []).map((variant) => ({ ...variant, productName: product.name }))) ?? [], [productsQuery.data]);
  const promotionNames = useMemo(() => new Map(promotionsQuery.data?.items.map((promotion) => [promotion.id, promotion]) ?? []), [promotionsQuery.data]);

  const mutation = useMutation({
    mutationFn: ({ path, body, headers }: { path: string; body?: unknown; headers?: HeadersInit }) =>
      api<unknown>(path, { method: 'POST', body, headers }),
    onSuccess: () => {
      setOperation(null);
      setSelectedAllocation(null);
      void client.invalidateQueries({ queryKey: ['inventory'] });
    },
  });

  const mutate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (key: string) => String(data.get(key) ?? '').trim();
    if (operation === 'add') {
      mutation.mutate({ path: '/api/inventory/admin/stock', body: { sku: value('sku'), quantity: Number(value('quantity')) } });
    }
    if (operation === 'adjust') {
      mutation.mutate({
        path: '/api/inventory/admin/stock/adjustments',
        body: { sku: value('sku'), quantityDelta: Number(value('quantity')), reason: value('reason') },
        headers: { 'Idempotency-Key': idempotencyKey() },
      });
    }
    if (operation === 'allocation') {
      mutation.mutate({
        path: '/api/inventory/admin/allocations',
        body: {
          offerId: value('offerId'),
          sku: value('sku'),
          totalQuantity: Number(value('quantity')),
          startsOnUtc: new Date(value('startsOnUtc')).toISOString(),
          endsOnUtc: new Date(value('endsOnUtc')).toISOString(),
          perUserLimit: Number(value('perUserLimit')),
        },
      });
    }
  };

  const selectMovementSku = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSku(String(new FormData(event.currentTarget).get('sku') ?? ''));
  };

  const stockOptions = stockListQuery.data?.items ?? [];
  const selectedOffer = selectedAllocation ? promotionNames.get(selectedAllocation.offerId) : undefined;

  return <div className="page">
    <PageHeader
      eyebrow="INVENTORY / CONTROL"
      title="库存与活动配额"
      description="普通订单和活动订单共用库存主链路；活动配额只负责为指定优惠与 SKU 圈定可售数量。"
      actions={<div className="button-group">
        {tab === 'stock' && can('inventory.stock.create') ? <button className="button button--quiet" type="button" onClick={() => setOperation('add')}><Icon name="plus" />商品入库</button> : null}
        {tab === 'stock' && can('inventory.stock.edit') ? <button className="button button--dark" type="button" onClick={() => setOperation('adjust')}>调整库存</button> : null}
        {tab === 'allocations' && can('inventory.allocations.create') ? <button className="button button--dark" type="button" onClick={() => setOperation('allocation')}><Icon name="plus" />创建活动配额</button> : null}
      </div>}
    />

    <div className="catalog-toolbar">
      <div className="tabs">
        <button type="button" aria-selected={tab === 'stock'} onClick={() => setTab('stock')}>SKU 库存</button>
        <button type="button" aria-selected={tab === 'allocations'} onClick={() => setTab('allocations')}>活动配额</button>
      </div>
      {tab === 'allocations' ? <div className="filters"><select value={allocationStatus} onChange={(event) => setAllocationStatus(event.target.value)} aria-label="配额状态"><option value="">全部状态</option><option value="Open">开放</option><option value="Closed">已关闭</option></select></div> : null}
    </div>

    {tab === 'stock' ? <>
      <section className="lookup-bar panel">
        <form onSubmit={selectMovementSku}>
          <Field label="查看库存流水"><select name="sku" required defaultValue={sku}><option value="">选择库存 SKU</option>{stockOptions.map((item) => <option key={item.id} value={item.sku}>{item.sku} · 可用 {item.available}</option>)}</select></Field>
          <button className="button button--dark" type="submit"><Icon name="search" />查看流水</button>
        </form>
        <span>AUDITED MOVEMENTS</span>
      </section>
      {stockListQuery.isError ? <ErrorNotice error={stockListQuery.error} /> : null}
      <section className="panel table-panel">
        <div className="section-heading"><div><p className="eyebrow">STOCK OVERVIEW</p><h2>库存汇总</h2></div><span>{stockListQuery.data?.total ?? 0} 个 SKU</span></div>
        {stockOptions.length ? <div className="data-table"><table><thead><tr><th>SKU</th><th>在库</th><th>预留</th><th>可用</th><th>版本</th><th>操作</th></tr></thead><tbody>{stockOptions.map((row) => <tr key={row.id}><td><strong>{row.sku}</strong></td><td>{row.onHand}</td><td>{row.reserved}</td><td className={row.available === 0 ? 'number-bad' : 'number-good'}><strong>{row.available}</strong></td><td>v{row.version}</td><td><div className="row-actions"><button type="button" onClick={() => setSku(row.sku)}>查看流水</button>{can('inventory.stock.edit') ? <button className="row-action-primary" type="button" onClick={() => { setSku(row.sku); setOperation('adjust'); }}>调整</button> : null}</div></td></tr>)}</tbody></table></div> : <Empty title="暂无库存" detail="从商品目录选择 SKU 完成第一次入库。" />}
      </section>
      {movementsQuery.isError ? <ErrorNotice error={movementsQuery.error} /> : null}
      <section className="panel table-panel inventory-ledger">
        <div className="section-heading"><div><p className="eyebrow">STOCK LEDGER</p><h2>{sku ? `${sku} 的库存流水` : '库存流水'}</h2></div><span>{movementsQuery.data?.length ?? 0} 条</span></div>
        {movementsQuery.data?.length ? <div className="data-table"><table><thead><tr><th>发生时间</th><th>类型</th><th>在库变化</th><th>预留变化</th><th>变化后库存</th><th>原因</th><th>操作人</th></tr></thead><tbody>{movementsQuery.data.map((row) => <tr key={row.id}><td>{formatDate(row.occurredOnUtc)}</td><td><Status>{row.type}</Status></td><td className={row.onHandDelta >= 0 ? 'number-good' : 'number-bad'}>{row.onHandDelta > 0 ? '+' : ''}{row.onHandDelta}</td><td>{row.reservedDelta}</td><td><strong>{row.onHandAfter}</strong><small>可用 {row.onHandAfter - row.reservedAfter}</small></td><td>{row.reason}</td><td>{row.operatorId ?? 'SYSTEM'}</td></tr>)}</tbody></table></div> : <Empty title="选择一个 SKU" detail="从库存汇总或上方下拉框查看该 SKU 的完整审计流水。" />}
      </section>
    </> : <>
      {allocationListQuery.isError ? <ErrorNotice error={allocationListQuery.error} /> : null}
      <section className="panel table-panel">
        <div className="section-heading"><div><p className="eyebrow">INVENTORY ALLOCATIONS</p><h2>活动库存配额</h2></div><span>{allocationListQuery.data?.total ?? 0} 个</span></div>
        {allocationListQuery.data?.items.length ? <div className="data-table"><table><thead><tr><th>优惠活动</th><th>SKU</th><th>状态</th><th>总量 / 可售</th><th>用户限购</th><th>活动周期</th><th>操作</th></tr></thead><tbody>{allocationListQuery.data.items.map((row) => {
          const offer = promotionNames.get(row.offerId);
          return <tr key={row.allocationId}><td><strong>{offer?.name ?? '未读取到活动名称'}</strong><small>{offer?.code ?? row.offerId}</small></td><td className="mono"><strong>{row.sku}</strong></td><td><Status>{row.state}</Status></td><td>{row.totalQuantity} / <strong className={row.availableQuantity ? 'number-good' : 'number-bad'}>{row.availableQuantity}</strong></td><td>{row.perUserLimit}</td><td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)}</small></td><td><div className="row-actions"><button type="button" onClick={() => setSelectedAllocation(row)}>详情</button>{row.state !== 'Closed' && can('inventory.allocations.edit') ? <button className="danger-link" type="button" onClick={() => mutation.mutate({ path: `/api/inventory/admin/allocations/${row.allocationId}/close` })}>关闭</button> : null}</div></td></tr>;
        })}</tbody></table></div> : <Empty title="暂无活动配额" detail="需要活动限量时，为已有促销和具体 SKU 创建配额；普通商品无需创建。" />}
      </section>
      {selectedAllocation ? <section className="allocation-detail panel">
        <div className="allocation-detail__headline"><div><p className="eyebrow">ALLOCATION DETAIL</p><h2>{selectedOffer?.name ?? selectedAllocation.sku}</h2><Status>{selectedAllocation.state}</Status></div><strong>{selectedAllocation.availableQuantity}<small>剩余可售</small></strong></div>
        <div className="quota-bar"><i style={{ width: `${Math.max(0, Math.min(100, selectedAllocation.availableQuantity / selectedAllocation.totalQuantity * 100))}%` }} /></div>
        <div className="quota-metrics"><span><small>配额总量</small><strong>{selectedAllocation.totalQuantity}</strong></span><span><small>已预留</small><strong>{selectedAllocation.reservedQuantity}</strong></span><span><small>已售</small><strong>{selectedAllocation.soldQuantity}</strong></span><span><small>已释放</small><strong>{selectedAllocation.releasedQuantity}</strong></span></div>
        <p>{selectedAllocation.sku} · 每位用户最多 {selectedAllocation.perUserLimit} 件 · {formatDate(selectedAllocation.startsOnUtc)} 至 {formatDate(selectedAllocation.endsOnUtc)}</p>
      </section> : null}
    </>}

    <Modal open={operation !== null} title={operation === 'add' ? '商品入库' : operation === 'adjust' ? '审计库存调整' : '创建活动库存配额'} description={operation === 'allocation' ? '配额绑定已有优惠活动和一个可售 SKU，不创建第二套下单流程。' : undefined} onClose={() => { setOperation(null); mutation.reset(); }}>
      <form className="modal-form" onSubmit={mutate}>
        {operation === 'allocation' ? <Field label="优惠活动" hint={!can('catalog.promotions.query') ? '需要 catalog.promotions.query 才能选择活动' : undefined}><select name="offerId" required disabled={!can('catalog.promotions.query')}><option value="">选择促销活动</option>{promotionsQuery.data?.items.map((promotion) => <option key={promotion.id} value={promotion.id}>{promotion.name} · {promotion.code} · {promotion.status}</option>)}</select></Field> : null}
        <Field label="商品 SKU" hint={!can('catalog.products.query') && operation !== 'adjust' ? '需要 catalog.products.query 才能选择商品 SKU' : undefined}>
          <select name="sku" required defaultValue={sku} disabled={operation !== 'adjust' && !can('catalog.products.query')}>
            <option value="">选择 SKU</option>
            {(operation === 'adjust' ? stockOptions.map((item) => ({ sku: item.sku, displayName: `可用 ${item.available}`, productName: '库存 SKU', id: item.id })) : skuOptions).map((item) => <option key={item.id} value={item.sku}>{item.productName} · {item.displayName} · {item.sku}</option>)}
          </select>
        </Field>
        <Field label={operation === 'adjust' ? '数量变化' : operation === 'allocation' ? '配额总量' : '入库数量'}><input name="quantity" type="number" required min={operation === 'adjust' ? undefined : 1} /></Field>
        {operation === 'adjust' ? <Field label="调整原因"><textarea name="reason" required rows={3} maxLength={500} placeholder="说明盘点、损耗或人工校正原因" /></Field> : null}
        {operation === 'allocation' ? <>
          <div className="form-row"><Field label="开始时间"><input name="startsOnUtc" type="datetime-local" defaultValue={localDate(0)} required /></Field><Field label="结束时间"><input name="endsOnUtc" type="datetime-local" defaultValue={localDate(2)} required /></Field></div>
          <Field label="每位用户限购"><input name="perUserLimit" type="number" min="1" max="999" defaultValue="1" required /></Field>
        </> : null}
        {mutation.isError ? <ErrorNotice error={mutation.error} /> : null}
        <SubmitBar busy={mutation.isPending} label={operation === 'allocation' ? '创建配额' : '提交库存变更'} onCancel={() => setOperation(null)} />
      </form>
    </Modal>
  </div>;
}
