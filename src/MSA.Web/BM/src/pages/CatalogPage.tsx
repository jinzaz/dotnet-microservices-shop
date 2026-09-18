import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api, catalogMediaPolicy, query, type Paged } from '../api';
import { useSession } from '../session';
import { asLocalInput, ChipFilter, CopyButton, Dropzone, Empty, ErrorNotice, Field, formatDate, formatMoney, formatRelative, Icon, Loading, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, UploadList, useConfirm, usePage, useToast, useUrlState, type UploadItem } from '../ui';
import { attachMedia, newUploadItems, ProductStudio, type Product } from './catalog/ProductStudio';
import { CouponBatchForm, IssuedCouponsView, useCouponUsage, type CouponUsage, type IssuedCoupon } from './catalog/CouponTools';
import { PromotionForm, type CategoryOption, type PromotionDetail } from './catalog/PromotionForm';

type Tab = 'products' | 'categories' | 'promotions' | 'coupons';
type Category = CategoryOption & { version: number; updatedOnUtc: string; updatedBy?: string };
type Promotion = PromotionDetail & { updatedOnUtc: string };
type Coupon = { id: string; promotionId: string; maskedCode: string; status: string; validFromUtc: string; validUntilUtc: string; assignedUserId?: string; totalLimit?: number; perUserLimit: number; quotaStrategy: string; version: number };
type CatalogRow = Category | Product | Promotion | Coupon;
type StateAction = { path: string; body: unknown; success: string };

const PAGE_SIZE = 20;
const tabs: Array<{ id: Tab; label: string; module: string; statuses: string[] }> = [
  { id: 'products', label: '商品', module: 'catalog.products', statuses: ['Draft', 'Published', 'Unpublished', 'Archived'] },
  { id: 'categories', label: '分类', module: 'catalog.categories', statuses: ['Draft', 'Active', 'Archived'] },
  { id: 'promotions', label: '促销', module: 'catalog.promotions', statuses: ['Draft', 'Active', 'Paused', 'Ended', 'Archived'] },
  { id: 'coupons', label: '优惠券', module: 'catalog.coupons', statuses: ['Draft', 'Active', 'Disabled', 'Expired'] },
];

export default function CatalogPage() {
  const { can } = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const visibleTabs = tabs.filter((item) => ['query', 'create', 'edit'].some((action) => can(`${item.module}.${action}`)));
  const [tabValue, setTab] = useUrlState('tab', visibleTabs[0]?.id ?? 'products');
  const tab = (visibleTabs.find((item) => item.id === tabValue) ?? visibleTabs[0] ?? tabs[0]!).id;
  const tabMeta = tabs.find((item) => item.id === tab)!;
  const [status, setStatus] = useUrlState('status');
  const [search, setSearch] = useUrlState('q');
  const [page, setPage] = usePage();
  const [productId, setProductId] = useUrlState('product', '', { resetPage: false });
  const [promotionId, setPromotionId] = useUrlState('promotion', '', { resetPage: false });
  const [promotionFilter, setPromotionFilter] = useUrlState('forPromotion');
  const [createOpen, setCreateOpen] = useState(false);
  const [batchOpen, setBatchOpen] = useState(false);
  const [issued, setIssued] = useState<IssuedCoupon[] | null>(null);
  const module = tabMeta.module;
  const canQuery = can(`${module}.query`);
  const canEdit = can(`${module}.edit`);

  const listQuery = useQuery({
    queryKey: ['catalog', tab, { status, search, page, promotionFilter }],
    queryFn: ({ signal }) => api<Paged<CatalogRow>>(`/api/catalog/admin/${tab}?${query({
      status,
      search: tab === 'products' ? search : '',
      promotionId: tab === 'coupons' ? promotionFilter : '',
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    })}`, { signal }),
    enabled: canQuery,
    placeholderData: keepPreviousData,
  });
  const categoriesQuery = useQuery({
    queryKey: ['catalog', 'category-options'],
    queryFn: () => api<Paged<Category>>('/api/catalog/admin/categories?skip=0&take=200'),
    enabled: can('catalog.categories.query'),
    staleTime: 60_000,
  });
  const promotionsQuery = useQuery({
    queryKey: ['catalog', 'promotion-options'],
    queryFn: () => api<Paged<Promotion>>('/api/catalog/admin/promotions?skip=0&take=200'),
    enabled: can('catalog.promotions.query'),
    staleTime: 60_000,
  });
  const productQuery = useQuery({
    queryKey: ['catalog', 'product', productId],
    queryFn: () => api<Product>(`/api/catalog/admin/products/${productId}`),
    enabled: Boolean(productId) && can('catalog.products.query'),
  });
  const promotionQuery = useQuery({
    queryKey: ['catalog', 'promotion', promotionId],
    queryFn: () => api<Promotion>(`/api/catalog/admin/promotions/${promotionId}`),
    enabled: Boolean(promotionId) && can('catalog.promotions.query'),
  });

  const categoryNames = useMemo(() => new Map(categoriesQuery.data?.items.map((item) => [item.id, item.name]) ?? []), [categoriesQuery.data]);
  const promotionById = useMemo(() => new Map(promotionsQuery.data?.items.map((item) => [item.id, item]) ?? []), [promotionsQuery.data]);
  const invalidateCatalog = () => client.invalidateQueries({ queryKey: ['catalog'] });

  const actionMutation = useMutation({
    mutationFn: ({ path, body }: StateAction) => api<void>(path, { method: 'POST', body }),
    onSuccess: (_, { success }) => { toast.success(success); return invalidateCatalog(); },
    onError: (error) => toast.error(error),
  });

  const runAction = async (row: CatalogRow & { version: number }, action: string, label: string, options: { name: string; reason?: boolean; danger?: boolean; detail?: string }) => {
    const result = await confirm({
      title: `${label}「${options.name}」？`,
      detail: options.detail,
      confirmLabel: label,
      tone: options.danger ? 'danger' : 'default',
      input: options.reason ? { label: '原因（可选）', placeholder: '记录到审计日志，便于追溯', maxLength: 500, multiline: true } : undefined,
    });
    if (!result) return;
    actionMutation.mutate({
      path: `/api/catalog/admin/${tab}/${row.id}/${action}`,
      body: { expectedVersion: row.version, ...(options.reason ? { reason: result.value || null } : {}) },
      success: `已${label}「${options.name}」`,
    });
  };

  const renameCategory = async (row: Category) => {
    const result = await confirm({ title: '重命名分类', confirmLabel: '保存', input: { label: '分类名称', defaultValue: row.name, required: true, maxLength: 120 } });
    if (!result || result.value === row.name) return;
    try {
      await api(`/api/catalog/admin/categories/${row.id}/name`, { method: 'PUT', body: { name: result.value, expectedVersion: row.version } });
      toast.success(`分类已重命名为「${result.value}」`);
      await invalidateCatalog();
    } catch (error) {
      toast.error(error);
    }
  };

  const createMutation = useMutation({
    mutationFn: async ({ body, uploads, onUpload }: { body: unknown; uploads?: UploadItem[]; onUpload?: (item: UploadItem) => void }) => {
      const created = await api<{ id: string; code?: string }>(`/api/catalog/admin/${tab}`, { method: 'POST', body });
      if (tab === 'coupons' && created.code) setIssued([{ id: created.id, code: created.code, assignedUserId: (body as { assignedUserId?: string | null }).assignedUserId }]);
      const failures = tab === 'products' && uploads?.length && onUpload
        ? await attachMedia(created.id, uploads, (body as { name: string }).name, null, 0, onUpload)
        : [];
      return { id: created.id, failures };
    },
    onSuccess: async ({ id, failures }) => {
      await invalidateCatalog();
      setCreateOpen(false);
      if (failures.length) toast.error(`已创建，但 ${failures.length} 个媒体上传失败，可在商品工作台重试。`); else toast.success(`${tabMeta.label}已创建`);
      if (tab === 'products') setProductId(id);
      if (tab === 'promotions') setPromotionId(id);
    },
  });

  const batchMutation = useMutation({
    mutationFn: (body: unknown) => api<IssuedCoupon[]>('/api/catalog/admin/coupons/batch', { method: 'POST', body }),
    onSuccess: async (coupons) => {
      setBatchOpen(false);
      setIssued(coupons);
      toast.success(`已发放 ${coupons.length} 张优惠券`);
      await invalidateCatalog();
    },
  });

  const promotionMutation = useMutation({
    mutationFn: ({ id, body }: { id: string; body: unknown }) => api<void>(`/api/catalog/admin/promotions/${id}`, { method: 'PUT', body }),
    onSuccess: async () => { toast.success('促销草稿已保存'); await invalidateCatalog(); },
  });

  const rows = listQuery.data?.items ?? [];
  const usageQuery = useCouponUsage(tab === 'coupons' ? rows.map((row) => row.id) : [], tab === 'coupons' && canQuery);
  const couponPromotions = (promotionsQuery.data?.items ?? []).filter((item) => item.triggerType === 'Coupon');
  const createLabel = tabMeta.label;

  return <div className="page">
    <PageHeader eyebrow="CATALOG / MERCHANDISING" title="商品与营销目录" description="以 SPU 维护商品内容，以 SKU 维护规格、价格和可售状态；媒体文件直传 RustFS 并在写入前校验。"
      actions={can(`${module}.create`) ? <>
        {tab === 'coupons' ? <button className="button button--quiet" type="button" onClick={() => { batchMutation.reset(); setBatchOpen(true); }}><Icon name="ticket" />批量发券</button> : null}
        <button className="button button--dark" type="button" onClick={() => { createMutation.reset(); setCreateOpen(true); }}><Icon name="plus" />新建{createLabel}</button>
      </> : undefined} />

    <div className="catalog-toolbar"><div className="tabs" role="tablist">{visibleTabs.map((item) => <button type="button" role="tab" key={item.id} aria-selected={tab === item.id} onClick={() => { setTab(item.id); setStatus(''); setSearch(''); }}>{item.label}</button>)}</div></div>

    <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="状态筛选" value={status} onChange={setStatus} chips={[{ value: '', label: '全部' }, ...tabMeta.statuses.map((value) => ({ value, label: statusLabel(value) }))]} />
        <div className="list-toolbar__filters">
          {tab === 'products' ? <SearchInput value={search} onChange={setSearch} placeholder="商品名称或 SKU" /> : null}
          {tab === 'coupons' && couponPromotions.length ? <select className="catalog-filter-select" aria-label="按促销活动筛选" value={promotionFilter} onChange={(event) => setPromotionFilter(event.target.value)}>
            <option value="">全部活动</option>
            {couponPromotions.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select> : null}
          <span className="list-toolbar__count">{listQuery.data?.total ?? 0} 条</span>
        </div>
      </div>
      {listQuery.isError ? <ErrorNotice error={listQuery.error} onRetry={() => void listQuery.refetch()} /> : null}
      {!canQuery ? <Empty title="没有查询权限" detail={`需要 ${module}.query 才能查看${tabMeta.label}列表。`} />
        : listQuery.isPending ? <SkeletonRows rows={8} />
          : rows.length ? <div className={`data-table${listQuery.isPlaceholderData ? ' is-stale' : ''}`}>
            {tab === 'products' ? <ProductTable rows={rows as Product[]} canEdit={canEdit} categoryNames={categoryNames} onOpen={setProductId} onAction={runAction} /> : null}
            {tab === 'categories' ? <CategoryTable rows={rows as Category[]} canEdit={canEdit} onRename={(row) => void renameCategory(row)} onAction={runAction} /> : null}
            {tab === 'promotions' ? <PromotionTable rows={rows as Promotion[]} canEdit={canEdit} onOpen={setPromotionId} onAction={runAction} /> : null}
            {tab === 'coupons' ? <CouponTable rows={rows as Coupon[]} canEdit={canEdit} promotions={promotionById} usage={usageQuery.data} usageError={usageQuery.isError} onAction={runAction} /> : null}
          </div>
            : <Empty title={`没有${tabMeta.label}`} detail={status || search ? '当前筛选条件下没有记录。' : `创建第一条${tabMeta.label}记录开始运营。`} action={status || search ? <button className="button button--quiet" type="button" onClick={() => { setStatus(''); setSearch(''); }}>清空筛选</button> : undefined} />}
      <Pagination page={page} pageSize={PAGE_SIZE} total={listQuery.data?.total} onChange={setPage} busy={listQuery.isFetching} />
    </section>
    <p className="capability-note"><Icon name="alert" />商品采用下架 / 归档而不是物理删除；已发布商品需先下架再归档。SKU 与媒体在商品工作台内管理。</p>

    <Modal open={createOpen} wide={tab === 'products' || tab === 'promotions'} title={`新建${createLabel}`} description={tab === 'products' ? '先创建 SPU 和默认 SKU，保存后自动进入商品工作台。' : undefined} onClose={() => setCreateOpen(false)}>
      {tab === 'products' ? <ProductCreateForm categories={categoriesQuery.data?.items ?? []} canReadCategories={can('catalog.categories.query')} canUploadMedia={can('catalog.products.edit')} busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(body, uploads, onUpload) => createMutation.mutate({ body, uploads, onUpload })} /> : null}
      {tab === 'categories' ? <SimpleCreateForm busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(data) => createMutation.mutate({ body: { code: data.get('code'), name: data.get('name') } })}>
        <div className="form-row"><Field label="分类代码" hint="创建后不可修改，建议使用英文大写"><input name="code" required maxLength={60} autoFocus placeholder="例如 PHONES" /></Field><Field label="分类名称"><input name="name" required maxLength={120} /></Field></div>
        <p className="muted-copy">新分类为草稿状态，启用后才能挂载商品。</p>
      </SimpleCreateForm> : null}
      {tab === 'promotions' ? <PromotionForm categories={categoriesQuery.data?.items ?? []} busy={createMutation.isPending} error={createMutation.isError ? <ErrorNotice error={createMutation.error} /> : null} onCancel={() => setCreateOpen(false)} onSubmit={(body) => createMutation.mutate({ body })} /> : null}
      {tab === 'coupons' ? <CouponCreateForm promotions={promotionsQuery.data?.items ?? []} canReadPromotions={can('catalog.promotions.query')} busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(body) => createMutation.mutate({ body })} /> : null}
    </Modal>

    <Modal open={batchOpen} wide title="批量发放优惠券" description="定向发给用户或生成一批通用券码；完整券码只在发放后显示一次。" onClose={() => setBatchOpen(false)}>
      {batchOpen ? <CouponBatchForm promotions={promotionsQuery.data?.items ?? []} busy={batchMutation.isPending} error={batchMutation.error} onCancel={() => setBatchOpen(false)} onSubmit={(body) => batchMutation.mutate(body)} /> : null}
    </Modal>

    <Modal open={Boolean(issued)} title={`已生成 ${issued?.length ?? 0} 个券码`} description="请立即保存：系统只存储券码摘要。" onClose={() => setIssued(null)}>
      {issued ? <IssuedCouponsView coupons={issued} onClose={() => setIssued(null)} /> : null}
    </Modal>

    <Modal open={Boolean(productId)} wide title={productQuery.data?.name ?? '商品工作台'} description={productQuery.data ? `${productQuery.data.variants?.length ?? 0} 个 SKU · ${productQuery.data.media?.length ?? 0} 个媒体 · 更新于 ${formatRelative(productQuery.data.updatedOnUtc)}` : '读取商品信息'} onClose={() => setProductId('')}>
      {productQuery.isPending ? <Loading label="正在读取商品配置" /> : null}
      {productQuery.isError ? <div className="modal-form"><ErrorNotice error={productQuery.error} onRetry={() => void productQuery.refetch()} /></div> : null}
      {productQuery.data ? <ProductStudio product={productQuery.data} categoryName={categoryNames.get(productQuery.data.categoryId)} canEdit={can('catalog.products.edit')} /> : null}
    </Modal>

    <Modal open={Boolean(promotionId)} wide title={promotionQuery.data?.name ?? '促销活动'} description={promotionQuery.data ? `${promotionQuery.data.code} · 修订 r${promotionQuery.data.revision} · ${statusLabel(promotionQuery.data.status)}` : undefined} onClose={() => { setPromotionId(''); promotionMutation.reset(); }}>
      {promotionQuery.isPending ? <Loading label="正在读取促销定义" /> : null}
      {promotionQuery.isError ? <div className="modal-form"><ErrorNotice error={promotionQuery.error} /></div> : null}
      {promotionQuery.data ? promotionQuery.data.status === 'Draft' && can('catalog.promotions.edit')
        ? <PromotionForm key={promotionQuery.data.version} item={promotionQuery.data} categories={categoriesQuery.data?.items ?? []} busy={promotionMutation.isPending} error={promotionMutation.isError ? <ErrorNotice error={promotionMutation.error} /> : null} onCancel={() => setPromotionId('')} onSubmit={(body) => promotionMutation.mutate({ id: promotionQuery.data!.id, body })} />
        : <PromotionSummary promotion={promotionQuery.data} categoryNames={categoryNames} /> : null}
    </Modal>
  </div>;
}

type ActionHandler = (row: CatalogRow & { version: number }, action: string, label: string, options: { name: string; reason?: boolean; danger?: boolean; detail?: string }) => void;

function ProductTable({ rows, canEdit, categoryNames, onOpen, onAction }: { rows: Product[]; canEdit: boolean; categoryNames: Map<string, string>; onOpen: (id: string) => void; onAction: ActionHandler }) {
  return <table><thead><tr><th>商品</th><th>分类</th><th>SKU</th><th>价格区间</th><th>状态</th><th>更新时间</th><th>操作</th></tr></thead><tbody>{rows.map((row) => {
    const cover = [...(row.media ?? [])].sort((left, right) => left.sortOrder - right.sortOrder).find((item) => item.mediaType === 'Image');
    const range = row.maxPrice !== undefined && row.maxPrice !== (row.minPrice ?? row.price);
    return <tr key={row.id} className="is-clickable" onClick={() => onOpen(row.id)}>
      <td><div className="product-cell">{cover ? <img src={cover.url} alt="" loading="lazy" /> : <span><Icon name="image" /></span>}<div><strong>{row.name}</strong><small>{row.description}</small></div></div></td>
      <td>{categoryNames.get(row.categoryId) ?? row.categoryId.slice(0, 8)}</td>
      <td><strong>{row.variants?.length ?? 1} 个</strong><small className="mono">{row.sku}</small></td>
      <td>{formatMoney(row.minPrice ?? row.price, row.currency)}{range ? <small>至 {formatMoney(row.maxPrice!, row.currency)}</small> : null}</td>
      <td><Status>{row.lifecycleStatus}</Status></td>
      <td title={formatDate(row.updatedOnUtc)}>{formatRelative(row.updatedOnUtc)}</td>
      <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
        <button className="row-action-primary" type="button" onClick={() => onOpen(row.id)}>{canEdit ? '工作台' : '查看'}</button>
        {canEdit && (row.lifecycleStatus === 'Draft' || row.lifecycleStatus === 'Unpublished') ? <button type="button" onClick={() => onAction(row, 'publish', '发布', { name: row.name, detail: '发布后立即对前台可见，SKU 快照会同步到购物车与订单服务。' })}>发布</button> : null}
        {canEdit && row.lifecycleStatus === 'Published' ? <button type="button" onClick={() => onAction(row, 'unpublish', '下架', { name: row.name, reason: true, detail: '下架后前台不可购买，已加入购物车的商品会标记为不可售。' })}>下架</button> : null}
        {canEdit && row.lifecycleStatus !== 'Published' && row.lifecycleStatus !== 'Archived' ? <button className="danger-link" type="button" onClick={() => onAction(row, 'archive', '归档', { name: row.name, reason: true, danger: true, detail: '归档是终态，商品将不能再编辑或发布。' })}>归档</button> : null}
      </div></td>
    </tr>;
  })}</tbody></table>;
}

function CategoryTable({ rows, canEdit, onRename, onAction }: { rows: Category[]; canEdit: boolean; onRename: (row: Category) => void; onAction: ActionHandler }) {
  return <table><thead><tr><th>分类</th><th>代码</th><th>状态</th><th>版本</th><th>更新时间</th><th>操作</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}>
    <td><strong>{row.name}</strong><small>{row.id.slice(0, 8)}</small></td>
    <td className="mono">{row.code}</td>
    <td><Status>{row.status}</Status></td>
    <td>v{row.version}</td>
    <td title={formatDate(row.updatedOnUtc)}>{formatRelative(row.updatedOnUtc)}{row.updatedBy ? <small>{row.updatedBy}</small> : null}</td>
    <td><div className="row-actions">{canEdit && row.status !== 'Archived' ? <>
      <button type="button" onClick={() => onRename(row)}>重命名</button>
      {row.status === 'Draft' ? <button className="row-action-primary" type="button" onClick={() => onAction(row, 'activate', '启用', { name: row.name, detail: '启用后可以向该分类添加和发布商品。' })}>启用</button> : null}
      <button className="danger-link" type="button" onClick={() => onAction(row, 'archive', '归档', { name: row.name, danger: true, detail: '包含已发布商品的分类不能归档；归档后不可恢复。' })}>归档</button>
    </> : '—'}</div></td>
  </tr>)}</tbody></table>;
}

function PromotionTable({ rows, canEdit, onOpen, onAction }: { rows: Promotion[]; canEdit: boolean; onOpen: (id: string) => void; onAction: ActionHandler }) {
  return <table><thead><tr><th>促销</th><th>触发</th><th>活动周期</th><th>优先级</th><th>状态</th><th>操作</th></tr></thead><tbody>{rows.map((row) => {
    const now = Date.now();
    const phase = now < new Date(row.startsOnUtc).getTime() ? '未开始' : now >= new Date(row.endsOnUtc).getTime() ? '已过期' : '进行中';
    return <tr key={row.id} className="is-clickable" onClick={() => onOpen(row.id)}>
      <td><strong>{row.name}</strong><small className="mono">{row.code}</small></td>
      <td>{row.triggerType === 'Coupon' ? '凭券使用' : '自动生效'}<small>{row.stackPolicy === 'Exclusive' ? '独占' : '可叠加'}</small></td>
      <td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)} · {phase}</small></td>
      <td>{row.priority}</td>
      <td><Status>{row.status}</Status></td>
      <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
        <button type="button" onClick={() => onOpen(row.id)}>{row.status === 'Draft' && canEdit ? '编辑' : '详情'}</button>
        {canEdit && row.status === 'Draft' ? <button className="row-action-primary" type="button" onClick={() => onAction(row, 'publish', '发布', { name: row.name, detail: '发布后定义不可再修改，订单服务将立即按活动周期计算优惠。' })}>发布</button> : null}
        {canEdit && row.status === 'Active' ? <button type="button" onClick={() => onAction(row, 'pause', '暂停', { name: row.name })}>暂停</button> : null}
        {canEdit && row.status === 'Paused' ? <button type="button" onClick={() => onAction(row, 'resume', '恢复', { name: row.name })}>恢复</button> : null}
        {canEdit && ['Paused', 'Scheduled', 'Ended'].includes(row.status) ? <button type="button" onClick={() => onAction(row, 'revise', '重新编辑', { name: row.name, detail: '活动回到草稿并立即停止参与计算，修改后需要重新发布。' })}>重新编辑</button> : null}
        {canEdit && ['Active', 'Paused', 'Scheduled'].includes(row.status) ? <button className="danger-link" type="button" onClick={() => onAction(row, 'end', '结束', { name: row.name, danger: true, detail: '结束后活动不再参与计算；可重新编辑后再发布。' })}>结束</button> : null}
        {canEdit && ['Draft', 'Ended'].includes(row.status) ? <button className="danger-link" type="button" onClick={() => onAction(row, 'archive', '归档', { name: row.name, danger: true, detail: '归档是终态，活动将不能再编辑或发布。' })}>归档</button> : null}
      </div></td>
    </tr>;
  })}</tbody></table>;
}

function CouponUsageCell({ row, usage, error }: { row: Coupon; usage?: CouponUsage; error: boolean }) {
  if (!usage) return <>{error ? '—' : '…'}<small>{error ? '使用数据不可用' : '读取中'}</small></>;
  const used = usage.consumedCount + usage.reservedCount;
  const ratio = row.totalLimit ? Math.min(1, used / row.totalLimit) : 0;
  return <div className="coupon-usage">
    <strong>已核销 {usage.consumedCount}{usage.reservedCount ? <small> · 占用 {usage.reservedCount}</small> : null}</strong>
    {row.totalLimit ? <span className="coupon-usage__bar" title={`${used}/${row.totalLimit}`}><span style={{ width: `${ratio * 100}%` }} /></span> : null}
    <small>{usage.remainingQuota !== null ? `剩余 ${usage.remainingQuota}` : '不限量'}{usage.consumedDiscountTotal ? ` · 让利 ${formatMoney(usage.consumedDiscountTotal)}` : ''}</small>
  </div>;
}

function CouponTable({ rows, canEdit, promotions, usage, usageError, onAction }: { rows: Coupon[]; canEdit: boolean; promotions: Map<string, Promotion>; usage?: Map<string, CouponUsage>; usageError: boolean; onAction: ActionHandler }) {
  return <table><thead><tr><th>优惠券</th><th>促销活动</th><th>有效期</th><th>配额</th><th>使用情况</th><th>状态</th><th>操作</th></tr></thead><tbody>{rows.map((row) => {
    const promotion = promotions.get(row.promotionId);
    const ended = Date.now() >= new Date(row.validUntilUtc).getTime();
    return <tr key={row.id}>
      <td><strong className="mono">{row.maskedCode}</strong><small title={row.assignedUserId}>{row.assignedUserId ? `定向 ${row.assignedUserId.slice(0, 8)}…` : '通用券码'}</small></td>
      <td><strong>{promotion?.name ?? '未读取到活动'}</strong><small>{promotion ? `${promotion.code} · ${statusLabel(promotion.status)}` : row.promotionId}</small></td>
      <td>{formatDate(row.validFromUtc)}<small>至 {formatDate(row.validUntilUtc)}</small></td>
      <td>{row.totalLimit ?? '不限'}<small>每人 {row.perUserLimit} 次 · {row.quotaStrategy === 'Bucketed' ? '分桶' : '单行'}</small></td>
      <td><CouponUsageCell row={row} usage={usage?.get(row.id)} error={usageError} /></td>
      <td><Status>{row.status}</Status></td>
      <td><div className="row-actions">{canEdit && row.status !== 'Expired' ? <>
        {row.status !== 'Active' && !ended ? <button className="row-action-primary" type="button" onClick={() => onAction(row, 'activate', '启用', { name: row.maskedCode, detail: '所属促销活动必须已发布且处于进行中。' })}>启用</button> : null}
        {row.status !== 'Disabled' && !ended ? <button type="button" onClick={() => onAction(row, 'disable', '停用', { name: row.maskedCode, reason: true, danger: true })}>停用</button> : null}
        {ended ? <button className="danger-link" type="button" onClick={() => onAction(row, 'expire', '标记过期', { name: row.maskedCode, danger: true, detail: '有效期已结束，标记后不可再启用。' })}>标记过期</button> : null}
      </> : '—'}</div></td>
    </tr>;
  })}</tbody></table>;
}

function PromotionSummary({ promotion, categoryNames }: { promotion: Promotion; categoryNames: Map<string, string> }) {
  const describe = (json: string) => Object.entries(JSON.parse(json) as Record<string, unknown>).map(([key, value]) => `${key}: ${String(value)}`).join(' · ');
  const labels: Record<string, string> = { MinimumOrderAmount: '最低订单金额', MinimumItemQuantity: '最低购买件数', PercentageOff: '百分比折扣', FixedAmountOff: '固定金额减免' };
  return <div className="order-detail">
    <div className="order-detail__summary">
      <div><span>活动周期</span><strong className="summary-date">{formatDate(promotion.startsOnUtc)}</strong><small>至 {formatDate(promotion.endsOnUtc)}</small></div>
      <div><span>执行策略</span><p><Status>{promotion.status}</Status></p><small>{promotion.triggerType === 'Coupon' ? '凭券使用' : '自动生效'} · {promotion.stackPolicy === 'Exclusive' ? '独占' : '可叠加'} · 优先级 {promotion.priority}{promotion.exclusiveGroup ? ` · 互斥组 ${promotion.exclusiveGroup}` : ''}</small></div>
    </div>
    <section><h3>门槛</h3>{promotion.rules.length ? <div className="compact-list">{promotion.rules.map((rule) => <article key={rule.sequence}><div><strong>{labels[rule.ruleType ?? ''] ?? rule.ruleType}</strong><small>{describe(rule.configurationJson)}</small></div></article>)}</div> : <p className="muted-copy">无门槛</p>}</section>
    <section><h3>优惠</h3><div className="compact-list">{promotion.benefits.map((benefit) => <article key={benefit.sequence}><div><strong>{labels[benefit.benefitType ?? ''] ?? benefit.benefitType}</strong><small>{describe(benefit.configurationJson)}</small></div></article>)}</div></section>
    <section><h3>适用范围</h3><div className="tag-list">{promotion.targets.length ? promotion.targets.map((target, index) => <span key={index}>{target.isExcluded ? '排除 ' : ''}{target.targetType === 'All' ? '全部商品' : target.targetType === 'Category' ? `分类 ${categoryNames.get(target.targetKey ?? '') ?? target.targetKey}` : `${target.targetType} ${target.targetKey}`}</span>) : <span>全部商品</span>}</div></section>
    <p className="muted-copy"><Icon name="alert" />已发布的活动定义不可修改；暂停或结束后可在列表中「重新编辑」，修改后重新发布。<CopyButton value={promotion.id} label="复制活动 ID" /></p>
  </div>;
}

function SimpleCreateForm({ busy, error, onSubmit, onCancel, children }: { busy: boolean; error: unknown; onSubmit: (data: FormData) => void; onCancel: () => void; children: ReactNode }) {
  return <form className="modal-form" onSubmit={(event) => { event.preventDefault(); onSubmit(new FormData(event.currentTarget)); }}>
    {children}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label="创建" onCancel={onCancel} />
  </form>;
}

function ProductCreateForm({ categories, canReadCategories, canUploadMedia, busy, error, onSubmit, onCancel }: { categories: Category[]; canReadCategories: boolean; canUploadMedia: boolean; busy: boolean; error: unknown; onSubmit: (body: unknown, uploads: UploadItem[], onUpload: (item: UploadItem) => void) => void; onCancel: () => void }) {
  const [uploads, setUploads] = useState<UploadItem[]>([]);
  const active = categories.filter((item) => item.status === 'Active');
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    onSubmit(
      { categoryId: value('categoryId'), sku: value('sku'), name: value('name'), description: value('description'), price: Number(value('price')), currency: value('currency') || 'CNY' },
      uploads,
      (item) => setUploads((current) => current.map((value) => value.id === item.id ? item : value)),
    );
  };
  return <form className="modal-form" onSubmit={submit}>
    <div className="form-section-title"><span>01</span><div><strong>基础信息</strong><small>SPU 负责名称、描述和归属分类</small></div></div>
    <Field label="商品分类" hint={!canReadCategories ? '需要 catalog.categories.query 才能选择分类' : !active.length ? '没有已启用的分类，请先在「分类」中启用' : '仅显示已启用的分类'}><select name="categoryId" required disabled={!canReadCategories}><option value="">选择分类</option>{active.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.code}</option>)}</select></Field>
    <Field label="商品名称"><input name="name" required maxLength={200} autoFocus /></Field>
    <Field label="商品描述"><textarea name="description" required rows={4} maxLength={4000} /></Field>
    <div className="form-section-title"><span>02</span><div><strong>默认 SKU</strong><small>创建后可继续增加颜色、尺码等规格</small></div></div>
    <div className="form-row"><Field label="SKU 编码"><input name="sku" required maxLength={80} placeholder="例如 TSHIRT-BLK-M" /></Field><Field label="销售价格"><div className="input-group"><select name="currency" defaultValue="CNY" aria-label="币种"><option value="CNY">CNY</option><option value="USD">USD</option><option value="EUR">EUR</option></select><input name="price" required min="0" step="0.01" type="number" /></div></Field></div>
    <div className="form-section-title"><span>03</span><div><strong>商品媒体</strong><small>{canUploadMedia ? '选填；创建成功后逐个直传 RustFS，校验通过才写入商品' : '需要 catalog.products.edit 权限，创建后由商品编辑人员补充'}</small></div></div>
    {canUploadMedia ? <>
      <Dropzone accept={catalogMediaPolicy.accept} multiple disabled={busy} hint="图片 ≤ 20 MB，视频 ≤ 512 MB" onFiles={(files) => setUploads((current) => [...current, ...newUploadItems(files)])} />
      <UploadList items={uploads} onRemove={(id) => setUploads((current) => current.filter((item) => item.id !== id))} />
    </> : null}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label={busy && uploads.length ? '正在创建并上传媒体…' : '创建商品'} onCancel={onCancel} />
  </form>;
}

function CouponCreateForm({ promotions, canReadPromotions, busy, error, onSubmit, onCancel }: { promotions: Promotion[]; canReadPromotions: boolean; busy: boolean; error: unknown; onSubmit: (body: unknown) => void; onCancel: () => void }) {
  const couponPromotions = promotions.filter((item) => item.triggerType === 'Coupon' && item.status !== 'Ended' && item.status !== 'Archived');
  const [promotionId, setPromotionId] = useState('');
  const [strategy, setStrategy] = useState('SingleRow');
  const selected = couponPromotions.find((item) => item.id === promotionId);
  const generate = () => Array.from(crypto.getRandomValues(new Uint8Array(8)), (value) => 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'[value % 31]).join('');
  const [code, setCode] = useState(generate);
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    onSubmit({
      promotionId, code, validFromUtc: new Date(value('validFrom')).toISOString(), validUntilUtc: new Date(value('validUntil')).toISOString(),
      assignedUserId: value('assignedUserId') || null, totalLimit: value('totalLimit') ? Number(value('totalLimit')) : null, perUserLimit: Number(value('perUserLimit') || 1),
      quotaStrategy: strategy, bucketCount: strategy === 'Bucketed' ? Number(value('bucketCount')) : null,
    });
  };
  return <form className="modal-form" onSubmit={submit}>
    <Field label="促销活动" hint={!canReadPromotions ? '需要 catalog.promotions.query 才能选择活动' : '仅显示「凭券使用」的活动；券的有效期必须落在活动周期内'}>
      <select required disabled={!canReadPromotions} value={promotionId} onChange={(event) => setPromotionId(event.target.value)}><option value="">选择促销活动</option>{couponPromotions.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.code} · {statusLabel(item.status)}</option>)}</select>
    </Field>
    <Field label="券码" hint="至少 6 位；系统只保存摘要，创建后会再展示一次完整券码供保存"><div className="input-group"><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} required minLength={6} maxLength={80} className="mono" /><button className="button button--quiet" type="button" onClick={() => setCode(generate())}><Icon name="refresh" />生成</button><CopyButton value={code} label="复制券码" /></div></Field>
    <div className="form-row" key={selected?.id ?? 'none'}>
      <Field label="生效时间"><input name="validFrom" type="datetime-local" defaultValue={asLocalInput(selected ? new Date(Math.max(Date.now(), new Date(selected.startsOnUtc).getTime())) : new Date())} min={selected ? asLocalInput(new Date(selected.startsOnUtc)) : undefined} required /></Field>
      <Field label="失效时间"><input name="validUntil" type="datetime-local" defaultValue={asLocalInput(selected ? new Date(selected.endsOnUtc) : new Date(Date.now() + 7 * 86_400_000))} max={selected ? asLocalInput(new Date(selected.endsOnUtc)) : undefined} required /></Field>
    </div>
    <div className="form-row"><Field label="总额度" hint="留空表示不限量"><input name="totalLimit" min="1" type="number" /></Field><Field label="每用户额度"><input name="perUserLimit" min="1" type="number" defaultValue="1" required /></Field></div>
    <div className="form-row"><Field label="配额策略" hint="高并发抢券建议分桶"><select value={strategy} onChange={(event) => setStrategy(event.target.value)}><option value="SingleRow">单行（默认）</option><option value="Bucketed">分桶（需填写总额度）</option></select></Field>{strategy === 'Bucketed' ? <Field label="分桶数量"><input name="bucketCount" type="number" min="2" max="128" defaultValue="8" required /></Field> : <span />}</div>
    <Field label="指定用户 ID"><input name="assignedUserId" placeholder="留空表示任何用户可用" /></Field>
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label="创建优惠券" onCancel={onCancel} />
  </form>;
}
