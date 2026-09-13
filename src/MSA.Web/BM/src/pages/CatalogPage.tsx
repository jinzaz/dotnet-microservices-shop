import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, Modal, PageHeader, Status, SubmitBar } from '../ui';

type Tab = 'categories' | 'products' | 'promotions' | 'coupons';
type Paged<T> = { items: T[]; skip: number; take: number; total: number };
type Category = { id: string; code: string; name: string; status: string; version: number; updatedOnUtc: string };
type Product = { id: string; categoryId: string; sku: string; name: string; description: string; price: number; currency: string; lifecycleStatus: string; version: number; updatedOnUtc: string };
type Promotion = { id: string; code: string; name: string; triggerType: string; status: string; startsOnUtc: string; endsOnUtc: string; priority: number; version: number };
type Coupon = { id: string; promotionId: string; maskedCode: string; status: string; validFromUtc: string; validUntilUtc: string; totalLimit?: number; perUserLimit: number; version: number };
type CatalogRow = Category | Product | Promotion | Coupon;

const tabs: Array<{ id: Tab; label: string; module: string }> = [
  { id: 'products', label: '商品', module: 'catalog.products' },
  { id: 'categories', label: '分类', module: 'catalog.categories' },
  { id: 'promotions', label: '促销', module: 'catalog.promotions' },
  { id: 'coupons', label: '优惠券', module: 'catalog.coupons' },
];

const asLocalInput = (date = new Date(Date.now() + 3_600_000)) => new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);

export default function CatalogPage() {
  const { can } = useSession();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<Tab>('products');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const module = `catalog.${tab}`;
  const params = new URLSearchParams({ skip: '0', take: '50' });
  if (status) params.set('status', status);
  if (search && tab === 'products') params.set('search', search);
  const listQuery = useQuery({
    queryKey: ['catalog', tab, status, search],
    queryFn: () => api<Paged<CatalogRow>>(`/api/catalog/admin/${tab}?${params}`),
    enabled: can(`${module}.query`),
  });

  const createMutation = useMutation({
    mutationFn: (body: unknown) => api<{ id: string }>(`/api/catalog/admin/${tab}`, { method: 'POST', body }),
    onSuccess: () => { setCreateOpen(false); void queryClient.invalidateQueries({ queryKey: ['catalog', tab] }); },
  });
  const actionMutation = useMutation({
    mutationFn: ({ path, method = 'POST', body }: { path: string; method?: string; body: unknown }) => api<void>(path, { method, body }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['catalog', tab] }),
  });

  const create = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    let body: unknown;
    if (tab === 'categories') body = { code: value('code'), name: value('name') };
    else if (tab === 'products') body = { categoryId: value('categoryId'), sku: value('sku'), name: value('name'), description: value('description'), price: Number(value('price')), currency: value('currency') || 'CNY' };
    else if (tab === 'coupons') body = { promotionId: value('promotionId'), code: value('code'), validFromUtc: new Date(value('startsOnUtc')).toISOString(), validUntilUtc: new Date(value('endsOnUtc')).toISOString(), assignedUserId: value('assignedUserId') || null, totalLimit: value('totalLimit') ? Number(value('totalLimit')) : null, perUserLimit: Number(value('perUserLimit') || 1), quotaStrategy: 'SingleRow', bucketCount: null };
    else {
      const ruleAmount = Number(value('minimumAmount'));
      const benefitType = value('benefitType');
      const benefitValue = Number(value('benefitValue'));
      body = { code: value('code'), name: value('name'), triggerType: value('triggerType'), startsOnUtc: new Date(value('startsOnUtc')).toISOString(), endsOnUtc: new Date(value('endsOnUtc')).toISOString(), priority: Number(value('priority') || 0), stackPolicy: value('stackPolicy'), exclusiveGroup: value('exclusiveGroup') || null, rules: ruleAmount > 0 ? [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: JSON.stringify({ amount: ruleAmount }), sequence: 0 }] : [], benefits: [{ benefitType, schemaVersion: 1, configurationJson: JSON.stringify(benefitType === 'PercentageOff' ? { percentage: benefitValue } : { amount: benefitValue }), sequence: 0 }], targets: [{ targetType: 'All', targetKey: null, isExcluded: false }] };
    }
    createMutation.mutate(body);
  };

  const stateAction = (row: CatalogRow, action: string, reason?: boolean) => {
    const note = reason ? window.prompt('请输入状态变更原因（可留空）') : null;
    actionMutation.mutate({ path: `/api/catalog/admin/${tab}/${row.id}/${action}`, body: { expectedVersion: row.version, ...(reason ? { reason: note } : {}) } });
  };

  const editCategory = (row: Category) => {
    const name = window.prompt('新的分类名称', row.name)?.trim();
    if (name) actionMutation.mutate({ path: `/api/catalog/admin/categories/${row.id}/name`, method: 'PUT', body: { name, expectedVersion: row.version } });
  };

  const editProduct = (row: Product, mode: 'details' | 'price') => {
    if (mode === 'price') {
      const price = Number(window.prompt('新的商品价格', String(row.price)));
      if (price > 0) actionMutation.mutate({ path: `/api/catalog/admin/products/${row.id}/price`, body: { price, currency: row.currency, expectedVersion: row.version } });
      return;
    }
    const name = window.prompt('新的商品名称', row.name)?.trim();
    if (name) actionMutation.mutate({ path: `/api/catalog/admin/products/${row.id}/details`, method: 'PUT', body: { name, description: row.description, expectedVersion: row.version } });
  };

  return <div className="page">
    <PageHeader eyebrow="CATALOG / MERCHANDISING" title="商品与营销目录" description="管理商品生命周期、分类结构、促销规则和优惠券配额。" actions={can(`${module}.create`) ? <button className="button button--dark" type="button" onClick={() => setCreateOpen(true)}><Icon name="plus" />新建{tabs.find((item) => item.id === tab)?.label}</button> : undefined} />
    <div className="catalog-toolbar"><div className="tabs">{tabs.filter((item) => can(`${item.module}.query`) || can(`${item.module}.create`) || can(`${item.module}.edit`)).map((item) => <button type="button" key={item.id} aria-selected={tab === item.id} onClick={() => { setTab(item.id); setStatus(''); setSearch(''); }}>{item.label}</button>)}</div><div className="filters">{tab === 'products' ? <label><Icon name="search" /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="名称或 SKU" /></label> : null}<select value={status} onChange={(event) => setStatus(event.target.value)} aria-label="状态筛选"><option value="">全部状态</option><option value="Draft">Draft</option><option value="Active">Active</option><option value="Published">Published</option><option value="Paused">Paused</option><option value="Archived">Archived</option><option value="Disabled">Disabled</option></select></div></div>
    {listQuery.isError ? <ErrorNotice error={listQuery.error} /> : null}
    {actionMutation.isError ? <ErrorNotice error={actionMutation.error} /> : null}
    <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">LIVE CATALOG</p><h2>{tabs.find((item) => item.id === tab)?.label}列表</h2></div><span>{listQuery.data?.total ?? 0} 条</span></div>{listQuery.data?.items.length ? <CatalogTable tab={tab} rows={listQuery.data.items} canEdit={can(`${module}.edit`)} onState={stateAction} onEditCategory={editCategory} onEditProduct={editProduct} /> : !listQuery.isFetching ? <Empty title="没有可展示的数据" detail={can(`${module}.query`) ? '调整筛选条件，或创建第一条记录。' : '当前身份没有该模块的查询权限。'} /> : null}</section>
    <p className="capability-note"><Icon name="alert" />后端尚未提供 Catalog 删除端点，因此页面不伪造删除操作；生命周期归档仍使用现有接口。</p>

    <Modal open={createOpen} title={`新建${tabs.find((item) => item.id === tab)?.label}`} description="表单字段直接对应当前 CatalogService 契约。" onClose={() => { setCreateOpen(false); createMutation.reset(); }}><form className="modal-form" onSubmit={create}>
      {tab === 'products' ? <><div className="form-row"><Field label="分类 ID"><input name="categoryId" required /></Field><Field label="SKU"><input name="sku" required maxLength={80} /></Field></div><Field label="商品名称"><input name="name" required /></Field><Field label="商品描述"><textarea name="description" required rows={4} /></Field><div className="form-row"><Field label="价格"><input name="price" required min="0.01" step="0.01" type="number" /></Field><Field label="币种"><input name="currency" defaultValue="CNY" required /></Field></div></> : null}
      {tab === 'categories' ? <div className="form-row"><Field label="分类代码"><input name="code" required /></Field><Field label="分类名称"><input name="name" required /></Field></div> : null}
      {tab === 'promotions' ? <><div className="form-row"><Field label="活动代码"><input name="code" required /></Field><Field label="活动名称"><input name="name" required /></Field></div><div className="form-row"><Field label="触发方式"><select name="triggerType"><option>Automatic</option><option>Coupon</option></select></Field><Field label="叠加策略"><select name="stackPolicy"><option>Stackable</option><option>Exclusive</option></select></Field></div><div className="form-row"><Field label="开始时间"><input name="startsOnUtc" type="datetime-local" defaultValue={asLocalInput()} required /></Field><Field label="结束时间"><input name="endsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date(Date.now() + 7 * 86_400_000))} required /></Field></div><div className="form-row"><Field label="最低订单金额"><input name="minimumAmount" type="number" min="0" step="0.01" placeholder="可选" /></Field><Field label="优先级"><input name="priority" type="number" defaultValue="0" /></Field></div><div className="form-row"><Field label="优惠类型"><select name="benefitType"><option>PercentageOff</option><option>FixedAmountOff</option></select></Field><Field label="优惠值"><input name="benefitValue" type="number" min="0.01" step="0.01" required /></Field></div><Field label="互斥组"><input name="exclusiveGroup" placeholder="可选" /></Field></> : null}
      {tab === 'coupons' ? <><div className="form-row"><Field label="促销 ID"><input name="promotionId" required /></Field><Field label="券码"><input name="code" required /></Field></div><div className="form-row"><Field label="生效时间"><input name="startsOnUtc" type="datetime-local" defaultValue={asLocalInput()} required /></Field><Field label="失效时间"><input name="endsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date(Date.now() + 7 * 86_400_000))} required /></Field></div><div className="form-row"><Field label="总额度"><input name="totalLimit" min="1" type="number" /></Field><Field label="每用户额度"><input name="perUserLimit" min="1" type="number" defaultValue="1" required /></Field></div><Field label="指定用户 ID"><input name="assignedUserId" placeholder="留空表示不定向" /></Field></> : null}
      {createMutation.isError ? <ErrorNotice error={createMutation.error} /> : null}<SubmitBar busy={createMutation.isPending} label="创建记录" onCancel={() => setCreateOpen(false)} />
    </form></Modal>
  </div>;
}

function CatalogTable({ tab, rows, canEdit, onState, onEditCategory, onEditProduct }: { tab: Tab; rows: CatalogRow[]; canEdit: boolean; onState: (row: CatalogRow, action: string, reason?: boolean) => void; onEditCategory: (row: Category) => void; onEditProduct: (row: Product, mode: 'details' | 'price') => void }) {
  if (tab === 'products') return <div className="data-table"><table><thead><tr><th>商品</th><th>SKU</th><th>价格</th><th>状态</th><th>更新时间</th><th>操作</th></tr></thead><tbody>{(rows as Product[]).map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.id.slice(0, 8)}</small></td><td className="mono">{row.sku}</td><td>{formatMoney(row.price, row.currency)}</td><td><Status>{row.lifecycleStatus}</Status></td><td>{formatDate(row.updatedOnUtc)}</td><td><div className="row-actions">{canEdit ? <><button onClick={() => onEditProduct(row, 'details')}>编辑</button><button onClick={() => onEditProduct(row, 'price')}>改价</button>{row.lifecycleStatus === 'Draft' ? <button onClick={() => onState(row, 'publish')}>发布</button> : <button onClick={() => onState(row, 'unpublish', true)}>下架</button>}<button onClick={() => onState(row, 'archive', true)}>归档</button></> : '—'}</div></td></tr>)}</tbody></table></div>;
  if (tab === 'categories') return <div className="data-table"><table><thead><tr><th>分类</th><th>代码</th><th>状态</th><th>版本</th><th>操作</th></tr></thead><tbody>{(rows as Category[]).map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.id.slice(0, 8)}</small></td><td className="mono">{row.code}</td><td><Status>{row.status}</Status></td><td>v{row.version}</td><td><div className="row-actions">{canEdit ? <><button onClick={() => onEditCategory(row)}>重命名</button>{row.status === 'Draft' ? <button onClick={() => onState(row, 'activate')}>启用</button> : null}<button onClick={() => onState(row, 'archive')}>归档</button></> : '—'}</div></td></tr>)}</tbody></table></div>;
  if (tab === 'promotions') return <div className="data-table"><table><thead><tr><th>促销</th><th>触发</th><th>周期</th><th>优先级</th><th>状态</th><th>操作</th></tr></thead><tbody>{(rows as Promotion[]).map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.code}</small></td><td>{row.triggerType}</td><td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)}</small></td><td>{row.priority}</td><td><Status>{row.status}</Status></td><td><div className="row-actions">{canEdit ? <>{row.status === 'Draft' ? <button onClick={() => onState(row, 'publish')}>发布</button> : null}{row.status === 'Published' ? <button onClick={() => onState(row, 'pause')}>暂停</button> : null}{row.status === 'Paused' ? <button onClick={() => onState(row, 'resume')}>恢复</button> : null}<button onClick={() => onState(row, 'end')}>结束</button></> : '—'}</div></td></tr>)}</tbody></table></div>;
  return <div className="data-table"><table><thead><tr><th>优惠券</th><th>促销 ID</th><th>有效期</th><th>配额</th><th>状态</th><th>操作</th></tr></thead><tbody>{(rows as Coupon[]).map((row) => <tr key={row.id}><td><strong>{row.maskedCode}</strong><small>{row.id.slice(0, 8)}</small></td><td className="mono">{row.promotionId.slice(0, 8)}</td><td>{formatDate(row.validFromUtc)}<small>至 {formatDate(row.validUntilUtc)}</small></td><td>{row.totalLimit ?? '不限'} / 每人 {row.perUserLimit}</td><td><Status>{row.status}</Status></td><td><div className="row-actions">{canEdit ? <>{row.status !== 'Active' ? <button onClick={() => onState(row, 'activate')}>启用</button> : <button onClick={() => onState(row, 'disable', true)}>停用</button>}</> : '—'}</div></td></tr>)}</tbody></table></div>;
}
