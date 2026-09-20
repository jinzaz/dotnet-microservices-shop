import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api, catalogMediaPolicy, query, type Paged } from '../api';
import { useSession } from '../session';
import { ChipFilter, Dropzone, Empty, ErrorNotice, Field, formatDate, formatMoney, formatRelative, Icon, Loading, Modal, PageHeader, Pagination, SearchInput, SkeletonRows, Status, statusLabel, SubmitBar, UploadList, useConfirm, usePage, useToast, useUrlState, type UploadItem } from '../ui';
import { attachMedia, newUploadItems, ProductStudio, type Product } from './catalog/ProductStudio';
import { CouponBatchForm, CouponCreateForm, CouponList, IssuedCouponsView, useCouponUsage, type Coupon, type CouponBulkAction, type IssuedCoupon } from './catalog/CouponTools';
import { PromotionForm, type CategoryOption } from './catalog/PromotionForm';
import { PromotionDetailView, PromotionTable, type Promotion } from './catalog/PromotionPanels';
import { campaignLink, CampaignDetailView, CampaignForm, CampaignTable, type Campaign } from './catalog/CampaignTools';

type Tab = 'products' | 'categories' | 'promotions' | 'coupons' | 'coupon-campaigns';
type Category = CategoryOption & { version: number; updatedOnUtc: string; updatedBy?: string };
type CatalogRow = Category | Product | Promotion | Coupon | Campaign;
type StateAction = { path: string; body: unknown; success: string };

const PAGE_SIZE = 20;
const tabs: Array<{ id: Tab; label: string; module: string; statuses: string[] }> = [
  { id: 'products', label: '商品', module: 'catalog.products', statuses: ['Draft', 'Published', 'Unpublished', 'Archived'] },
  { id: 'categories', label: '分类', module: 'catalog.categories', statuses: ['Draft', 'Active', 'Archived'] },
  { id: 'promotions', label: '促销', module: 'catalog.promotions', statuses: ['Draft', 'Active', 'Paused', 'Ended', 'Archived'] },
  { id: 'coupons', label: '优惠券', module: 'catalog.coupons', statuses: ['Draft', 'Active', 'Disabled', 'Expired'] },
  { id: 'coupon-campaigns', label: '领券活动', module: 'catalog.coupons', statuses: ['Draft', 'Active', 'Paused', 'Ended'] },
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
  const [campaignId, setCampaignId] = useUrlState('campaign', '', { resetPage: false });
  const [duplicateCampaign, setDuplicateCampaign] = useState<Campaign | null>(null);
  const [campaignPromotionId, setCampaignPromotionId] = useState<string | undefined>();
  const [createOpen, setCreateOpen] = useState(false);
  const [batchOpen, setBatchOpen] = useState(false);
  const [batchPromotionId, setBatchPromotionId] = useState<string | undefined>();
  const [duplicateSource, setDuplicateSource] = useState<Promotion | null>(null);
  const [issued, setIssued] = useState<IssuedCoupon[] | null>(null);
  const module = tabMeta.module;
  const canQuery = can(`${module}.query`);
  const canEdit = can(`${module}.edit`);

  const listQuery = useQuery({
    queryKey: ['catalog', tab, { status, search, page, promotionFilter }],
    queryFn: ({ signal }) => api<Paged<CatalogRow>>(`/api/catalog/admin/${tab}?${query({
      status,
      search: tab === 'products' ? search : '',
      promotionId: tab === 'coupons' || tab === 'coupon-campaigns' ? promotionFilter : '',
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

  const campaignQuery = useQuery({
    queryKey: ['catalog', 'campaign', campaignId],
    queryFn: () => api<Campaign>(`/api/catalog/admin/coupon-campaigns/${campaignId}`),
    enabled: Boolean(campaignId) && can('catalog.coupons.query'),
  });

  const categoryNames = useMemo(() => new Map(categoriesQuery.data?.items.map((item) => [item.id, item.name]) ?? []), [categoriesQuery.data]);
  const promotionById = useMemo(() => new Map(promotionsQuery.data?.items.map((item) => [item.id, item]) ?? []), [promotionsQuery.data]);
  const invalidateCatalog = () => client.invalidateQueries({ queryKey: ['catalog'] });

  const actionMutation = useMutation({
    mutationFn: ({ path, body }: StateAction) => api<void>(path, { method: 'POST', body }),
    onSuccess: (_, { success }) => { toast.success(success); return invalidateCatalog(); },
    onError: (error) => toast.error(error),
  });

  const runAction = async (row: CatalogRow & { version: number }, action: string, label: string, options: { name: string; reason?: boolean; danger?: boolean; detail?: string }, resource: Tab = tab) => {
    const result = await confirm({
      title: `${label}「${options.name}」？`,
      detail: options.detail,
      confirmLabel: label,
      tone: options.danger ? 'danger' : 'default',
      input: options.reason ? { label: '原因（可选）', placeholder: '记录到审计日志，便于追溯', maxLength: 500, multiline: true } : undefined,
    });
    if (!result) return;
    actionMutation.mutate({
      path: `/api/catalog/admin/${resource}/${row.id}/${action}`,
      body: { expectedVersion: row.version, ...(options.reason ? { reason: result.value || null } : {}) },
      success: `已${label}「${options.name}」`,
    });
  };

  // Sequential on purpose: each call carries its own expected version, and partial failures are reported per coupon.
  const runCouponBulk = async (action: CouponBulkAction, rows: Coupon[]) => {
    if (!rows.length) return;
    const label = action === 'activate' ? '启用' : '停用';
    const result = await confirm({
      title: `批量${label} ${rows.length} 张优惠券？`,
      detail: action === 'activate' ? '所属促销活动必须已发布且处于进行中，否则对应的券会启用失败。' : '停用后用户无法再使用这些券；已下单占用的额度不受影响。',
      confirmLabel: `${label} ${rows.length} 张`,
      tone: action === 'disable' ? 'danger' : 'default',
    });
    if (!result) return;
    const failures: string[] = [];
    for (const row of rows) {
      try {
        await api<void>(`/api/catalog/admin/coupons/${row.id}/${action}`, { method: 'POST', body: { expectedVersion: row.version, ...(action === 'disable' ? { reason: '批量停用' } : {}) } });
      } catch (error) {
        failures.push(`${row.maskedCode}：${error instanceof Error ? error.message : '失败'}`);
      }
    }
    await invalidateCatalog();
    if (!failures.length) toast.success(`已${label} ${rows.length} 张优惠券`);
    else toast.error(`${rows.length - failures.length} 张已${label}，${failures.length} 张失败：${failures.slice(0, 3).join('；')}${failures.length > 3 ? '…' : ''}`);
  };

  const openBatch = (promotionIdForBatch?: string) => {
    batchMutation.reset();
    setBatchPromotionId(promotionIdForBatch);
    setBatchOpen(true);
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
    onSuccess: async ({ id, failures }, variables) => {
      await invalidateCatalog();
      setCreateOpen(false);
      if (failures.length) toast.error(`已创建，但 ${failures.length} 个媒体上传失败，可在商品工作台重试。`); else toast.success(`${tabMeta.label}已创建`);
      if (tab === 'products') setProductId(id);
      if (tab === 'promotions') {
        setPromotionId(id);
        setDuplicateSource(null);
        if ((variables.body as { triggerType?: string }).triggerType === 'Coupon') toast.info('凭券活动需要发布后，才能发放和启用优惠券');
      }
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

  const campaignCreateMutation = useMutation({
    mutationFn: async ({ body, publish }: { body: unknown; publish: boolean }) => {
      const created = await api<{ id: string }>('/api/catalog/admin/coupon-campaigns', { method: 'POST', body });
      // Two calls on purpose: if publishing fails the draft is kept and the error says why.
      if (publish) await api<void>(`/api/catalog/admin/coupon-campaigns/${created.id}/publish`, { method: 'POST', body: { expectedVersion: 1 } });
      return { id: created.id, publish };
    },
    onSuccess: async ({ id, publish }) => {
      setCreateOpen(false);
      setDuplicateCampaign(null);
      toast.success(publish ? '领券活动已发布' : '领券活动草稿已保存');
      await invalidateCatalog();
      setCampaignId(id);
    },
    onError: () => invalidateCatalog(),
  });

  const campaignMutation = useMutation({
    mutationFn: ({ id, body }: { id: string; body: unknown }) => api<void>(`/api/catalog/admin/coupon-campaigns/${id}`, { method: 'PUT', body }),
    onSuccess: async () => { toast.success('领券活动草稿已保存'); await invalidateCatalog(); },
  });

  const changeCampaignQuantity = async (row: Campaign) => {
    const result = await confirm({
      title: `调整「${row.title}」的库存`,
      detail: `已领取 ${row.claimedQuantity} 张。填写新的发放总量（不能低于已领取数量），留空表示不限量。`,
      confirmLabel: '保存库存',
      input: { label: '发放总量（张）', defaultValue: row.totalQuantity === null ? '' : String(row.totalQuantity), placeholder: '留空为不限量', maxLength: 9 },
    });
    if (!result) return;
    const value = result.value.trim();
    const totalQuantity = value === '' ? null : Number(value);
    if (totalQuantity !== null && (!Number.isInteger(totalQuantity) || totalQuantity < Math.max(1, row.claimedQuantity))) {
      toast.error(`请输入不小于 ${Math.max(1, row.claimedQuantity)} 的整数`);
      return;
    }
    try {
      await api<void>(`/api/catalog/admin/coupon-campaigns/${row.id}/quantity`, { method: 'POST', body: { totalQuantity, expectedVersion: row.version } });
      toast.success(totalQuantity === null ? '已改为不限量' : `发放总量已调整为 ${totalQuantity} 张`);
      await invalidateCatalog();
    } catch (error) {
      toast.error(error);
    }
  };

  const copyCampaignLink = async (row: Campaign) => {
    try {
      await navigator.clipboard.writeText(campaignLink(row.id));
      toast.success('领取链接已复制');
    } catch {
      toast.error('无法访问剪贴板，请在详情中手动复制');
    }
  };

  const openCampaignCreate = (promotionIdForCampaign?: string, source?: Campaign) => {
    campaignCreateMutation.reset();
    setCampaignPromotionId(promotionIdForCampaign);
    setDuplicateCampaign(source ?? null);
    setCreateOpen(true);
  };

  const openCreate = () => {
    if (tab === 'coupon-campaigns') {
      openCampaignCreate(promotionFilter || undefined);
      return;
    }
    createMutation.reset();
    setDuplicateSource(null);
    setCreateOpen(true);
  };

  const rows = listQuery.data?.items ?? [];
  const usageQuery = useCouponUsage(tab === 'coupons' ? rows.map((row) => row.id) : [], tab === 'coupons' && canQuery);
  const couponPromotions = (promotionsQuery.data?.items ?? []).filter((item) => item.triggerType === 'Coupon');
  const createLabel = tabMeta.label;
  // Coupon forms decide "no coupon promotion yet" from this list, so never render them before it has loaded.
  const promotionOptionsGate = !can('catalog.promotions.query') ? null
    : promotionsQuery.isPending ? <Loading label="正在读取促销活动" />
      : promotionsQuery.isError ? <ErrorNotice error={promotionsQuery.error} onRetry={() => void promotionsQuery.refetch()} /> : null;

  return <div className="page">
    <PageHeader eyebrow="CATALOG / MERCHANDISING" title="商品与营销目录" description="以 SPU 维护商品内容，以 SKU 维护规格、价格和可售状态；媒体文件直传 RustFS 并在写入前校验。"
      actions={can(`${module}.create`) ? <>
        {tab === 'coupons' ? <button className="button button--quiet" type="button" onClick={() => openBatch()}><Icon name="ticket" />批量发券</button> : null}
        <button className="button button--dark" type="button" onClick={openCreate}><Icon name="plus" />新建{createLabel}</button>
      </> : undefined} />

    <div className="catalog-toolbar"><div className="tabs" role="tablist">{visibleTabs.map((item) => <button type="button" role="tab" key={item.id} aria-selected={tab === item.id} onClick={() => { setTab(item.id); setStatus(''); setSearch(''); }}>{item.label}</button>)}</div></div>

    <section className="panel table-panel">
      <div className="list-toolbar">
        <ChipFilter label="状态筛选" value={status} onChange={setStatus} chips={[{ value: '', label: '全部' }, ...tabMeta.statuses.map((value) => ({ value, label: statusLabel(value) }))]} />
        <div className="list-toolbar__filters">
          {tab === 'products' ? <SearchInput value={search} onChange={setSearch} placeholder="商品名称或 SKU" /> : null}
          {(tab === 'coupons' || tab === 'coupon-campaigns') && couponPromotions.length ? <select className="catalog-filter-select" aria-label="按促销活动筛选" value={promotionFilter} onChange={(event) => setPromotionFilter(event.target.value)}>
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
            {tab === 'promotions' ? <PromotionTable rows={rows as Promotion[]} canEdit={canEdit} categoryNames={categoryNames} onOpen={setPromotionId} onAction={(row, action, label, options) => void runAction(row, action, label, options, 'promotions')} /> : null}
            {tab === 'coupon-campaigns' ? <CampaignTable rows={rows as Campaign[]} canEdit={canEdit} promotions={promotionById} onOpen={setCampaignId} onCopyLink={(row) => void copyCampaignLink(row)} onAction={(row, action, label, options) => void runAction(row, action, label, options, 'coupon-campaigns')} /> : null}
            {tab === 'coupons' ? <CouponList key={`${page}-${status}-${promotionFilter}`} rows={rows as Coupon[]} canEdit={canEdit} promotions={promotionById} usage={usageQuery.data} usageError={usageQuery.isError} onAction={(row, action, label, options) => void runAction(row, action, label, options, 'coupons')} onBulk={runCouponBulk} /> : null}
          </div>
            : <Empty
              title={`没有${tabMeta.label}`}
              detail={status || search || promotionFilter ? '当前筛选条件下没有记录。' : tab === 'promotions' ? '用「满额立减」「多件折扣」等模板，一分钟创建第一个活动。' : tab === 'coupon-campaigns' ? '设置发放总量、每人限领和开抢时间，用户在领券中心或凭链接自助领取。' : tab === 'coupons' ? couponPromotions.length ? `已有 ${couponPromotions.length} 个凭券活动，点击「新建优惠券」或「批量发券」开始发放。` : '先创建「凭券使用」的促销活动，再单张创建或批量发放优惠券。' : `创建第一条${tabMeta.label}记录开始运营。`}
              action={status || search || promotionFilter
                ? <button className="button button--quiet" type="button" onClick={() => { setStatus(''); setSearch(''); setPromotionFilter(''); }}>清空筛选</button>
                : can(`${module}.create`) ? <button className="button button--dark" type="button" onClick={openCreate}><Icon name="plus" />新建{createLabel}</button> : undefined} />}
      <Pagination page={page} pageSize={PAGE_SIZE} total={listQuery.data?.total} onChange={setPage} busy={listQuery.isFetching} />
    </section>
    <p className="capability-note"><Icon name="alert" />商品采用下架 / 归档而不是物理删除；已发布商品需先下架再归档。SKU 与媒体在商品工作台内管理。</p>

    <Modal open={createOpen} wide={tab === 'products' || tab === 'promotions' || tab === 'coupon-campaigns'} title={duplicateSource ? `复制活动「${duplicateSource.name}」` : duplicateCampaign ? `复制领券活动「${duplicateCampaign.title}」` : `新建${createLabel}`} description={tab === 'products' ? '先创建 SPU 和默认 SKU，保存后自动进入商品工作台。' : undefined} onClose={() => setCreateOpen(false)}>
      {tab === 'products' ? <ProductCreateForm categories={categoriesQuery.data?.items ?? []} canReadCategories={can('catalog.categories.query')} canUploadMedia={can('catalog.products.edit')} busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(body, uploads, onUpload) => createMutation.mutate({ body, uploads, onUpload })} /> : null}
      {tab === 'categories' ? <SimpleCreateForm busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(data) => createMutation.mutate({ body: { code: data.get('code'), name: data.get('name') } })}>
        <div className="form-row"><Field label="分类代码" hint="创建后不可修改，建议使用英文大写"><input name="code" required maxLength={60} autoFocus placeholder="例如 PHONES" /></Field><Field label="分类名称"><input name="name" required maxLength={120} /></Field></div>
        <p className="muted-copy">新分类为草稿状态，启用后才能挂载商品。</p>
      </SimpleCreateForm> : null}
      {tab === 'promotions' && createOpen ? <PromotionForm key={duplicateSource?.id ?? 'new'} duplicateOf={duplicateSource ?? undefined} categories={categoriesQuery.data?.items ?? []} busy={createMutation.isPending} error={createMutation.isError ? <ErrorNotice error={createMutation.error} /> : null} onCancel={() => setCreateOpen(false)} onSubmit={(body) => createMutation.mutate({ body })} /> : null}
      {tab === 'coupons' && createOpen ? promotionOptionsGate ?? <CouponCreateForm promotions={promotionsQuery.data?.items ?? []} canReadPromotions={can('catalog.promotions.query')} canSearchUsers={can('identity.users.query')} initialPromotionId={promotionFilter || undefined} busy={createMutation.isPending} error={createMutation.error} onCancel={() => setCreateOpen(false)} onSubmit={(body) => createMutation.mutate({ body })} /> : null}
      {tab === 'coupon-campaigns' && createOpen ? promotionOptionsGate ?? <CampaignForm key={duplicateCampaign?.id ?? campaignPromotionId ?? 'new'} duplicateOf={duplicateCampaign ?? undefined} promotions={promotionsQuery.data?.items ?? []} canPublish={can('catalog.coupons.edit')} initialPromotionId={campaignPromotionId} busy={campaignCreateMutation.isPending} error={campaignCreateMutation.isError ? <ErrorNotice error={campaignCreateMutation.error} /> : null} onCancel={() => setCreateOpen(false)} onSubmit={(body, publish) => campaignCreateMutation.mutate({ body, publish })} /> : null}
    </Modal>

    <Modal open={batchOpen} wide title="批量发放优惠券" description="定向发给用户或生成一批通用券码；完整券码只在发放后显示一次。" onClose={() => setBatchOpen(false)}>
      {batchOpen ? promotionOptionsGate ?? <CouponBatchForm promotions={promotionsQuery.data?.items ?? []} canSearchUsers={can('identity.users.query')} initialPromotionId={batchPromotionId ?? (promotionFilter || undefined)} busy={batchMutation.isPending} error={batchMutation.error} onCancel={() => setBatchOpen(false)} onSubmit={(body) => batchMutation.mutate(body)} /> : null}
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
        ? <>
          <div className="draft-bar">
            <span><Icon name="alert" />草稿不会参与计算。保存修改后点击「发布」生效。</span>
            <button type="button" className="button button--dark" disabled={promotionMutation.isPending} onClick={() => void runAction(promotionQuery.data!, 'publish', '发布', { name: promotionQuery.data!.name, detail: '将按最近保存的草稿发布；未保存的修改不会生效。发布后定义不可修改。' }, 'promotions')}>发布</button>
          </div>
          <PromotionForm key={promotionQuery.data.version} item={promotionQuery.data} categories={categoriesQuery.data?.items ?? []} busy={promotionMutation.isPending} error={promotionMutation.isError ? <ErrorNotice error={promotionMutation.error} /> : null} onCancel={() => setPromotionId('')} onSubmit={(body) => promotionMutation.mutate({ id: promotionQuery.data!.id, body })} />
        </>
        : <PromotionDetailView
          promotion={promotionQuery.data}
          categoryNames={categoryNames}
          canEdit={can('catalog.promotions.edit')}
          canCreate={can('catalog.promotions.create')}
          canQueryCoupons={can('catalog.coupons.query')}
          canIssueCoupons={can('catalog.coupons.create')}
          onAction={(row, action, label, options) => void runAction(row, action, label, options, 'promotions')}
          onDuplicate={() => { const source = promotionQuery.data!; setPromotionId(''); createMutation.reset(); setDuplicateSource(source); setCreateOpen(true); }}
          onIssueCoupons={() => openBatch(promotionQuery.data!.id)}
          onCreateCampaign={can('catalog.coupons.create') ? () => { const id = promotionQuery.data!.id; setPromotionId(''); setTab('coupon-campaigns'); openCampaignCreate(id); } : undefined}
          onShowCoupons={() => { const id = promotionQuery.data!.id; setPromotionId(''); setTab('coupons'); setPromotionFilter(id); }} /> : null}
    </Modal>

    <Modal open={Boolean(campaignId)} wide title={campaignQuery.data?.title ?? '领券活动'} description={campaignQuery.data ? `${statusLabel(campaignQuery.data.status)} · 更新于 ${formatRelative(campaignQuery.data.updatedOnUtc)}` : undefined} onClose={() => { setCampaignId(''); campaignMutation.reset(); }}>
      {campaignQuery.isPending ? <Loading label="正在读取领券活动" /> : null}
      {campaignQuery.isError ? <div className="modal-form"><ErrorNotice error={campaignQuery.error} /></div> : null}
      {campaignQuery.data ? campaignQuery.data.status === 'Draft' && can('catalog.coupons.edit')
        ? <>
          <div className="draft-bar">
            <span><Icon name="alert" />草稿不会对用户展示。保存修改后点击「发布」开放领取。</span>
            <button type="button" className="button button--dark" onClick={() => void runAction(campaignQuery.data!, 'publish', '发布', { name: campaignQuery.data!.title, detail: '将按最近保存的草稿发布；发布后规则不可修改，库存仍可调整。' }, 'coupon-campaigns')}>发布</button>
          </div>
          {promotionOptionsGate ?? <CampaignForm key={campaignQuery.data.version} item={campaignQuery.data} promotions={promotionsQuery.data?.items ?? []} canPublish={false} busy={campaignMutation.isPending} error={campaignMutation.isError ? <ErrorNotice error={campaignMutation.error} /> : null} onCancel={() => setCampaignId('')} onSubmit={(body) => campaignMutation.mutate({ id: campaignQuery.data!.id, body: { definition: body.definition, expectedVersion: campaignQuery.data!.version } })} />}
        </>
        : <CampaignDetailView
          campaign={campaignQuery.data}
          promotion={promotionById.get(campaignQuery.data.promotionId)}
          categoryNames={categoryNames}
          canEdit={can('catalog.coupons.edit')}
          canCreate={can('catalog.coupons.create')}
          onAction={(row, action, label, options) => void runAction(row, action, label, options, 'coupon-campaigns')}
          onChangeQuantity={() => void changeCampaignQuantity(campaignQuery.data!)}
          onDuplicate={() => { const source = campaignQuery.data!; setCampaignId(''); openCampaignCreate(source.promotionId, source); }} /> : null}
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

