import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, uploadCatalogMedia } from '../api';
import { useSession } from '../session';
import { Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, Loading, Modal, PageHeader, Status, SubmitBar } from '../ui';

type Tab = 'categories' | 'products' | 'promotions' | 'coupons';
type Paged<T> = { items: T[]; skip: number; take: number; total: number };
type Category = { id: string; code: string; name: string; status: string; version: number; updatedOnUtc: string };
type ProductVariant = { id: string; productId: string; sku: string; displayName: string; attributesJson: string; barcode?: string; price: number; currency: string; isActive: boolean; isDefault: boolean; updatedOnUtc: string };
type ProductMedia = { id: string; productId: string; variantId?: string; mediaType: 'Image' | 'Video'; url: string; thumbnailUrl?: string; altText?: string; sortOrder: number };
type Product = { id: string; categoryId: string; sku: string; name: string; description: string; price: number; currency: string; lifecycleStatus: string; version: number; updatedOnUtc: string; variants?: ProductVariant[]; media?: ProductMedia[]; minPrice?: number; maxPrice?: number };
type Promotion = { id: string; code: string; name: string; triggerType: string; status: string; startsOnUtc: string; endsOnUtc: string; priority: number; version: number };
type Coupon = { id: string; promotionId: string; maskedCode: string; status: string; validFromUtc: string; validUntilUtc: string; totalLimit?: number; perUserLimit: number; version: number };
type CatalogRow = Category | Product | Promotion | Coupon;
type VariantDraft = ProductVariant | 'new' | null;

const tabs: Array<{ id: Tab; label: string; module: string }> = [
  { id: 'products', label: '商品', module: 'catalog.products' },
  { id: 'categories', label: '分类', module: 'catalog.categories' },
  { id: 'promotions', label: '促销', module: 'catalog.promotions' },
  { id: 'coupons', label: '优惠券', module: 'catalog.coupons' },
];

const asLocalInput = (date = new Date(Date.now() + 3_600_000)) =>
  new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);

export default function CatalogPage() {
  const { can } = useSession();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<Tab>('products');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [variantDraft, setVariantDraft] = useState<VariantDraft>(null);
  const module = `catalog.${tab}`;

  const params = new URLSearchParams({ skip: '0', take: '50' });
  if (status) params.set('status', status);
  if (search && tab === 'products') params.set('search', search);
  const listQuery = useQuery({
    queryKey: ['catalog', tab, status, search],
    queryFn: () => api<Paged<CatalogRow>>(`/api/catalog/admin/${tab}?${params}`),
    enabled: can(`${module}.query`),
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
    queryKey: ['catalog', 'product', selectedProductId],
    queryFn: () => api<Product>(`/api/catalog/admin/products/${selectedProductId}`),
    enabled: Boolean(selectedProductId) && can('catalog.products.query'),
  });

  const categoryNames = useMemo(() => new Map(categoriesQuery.data?.items.map((item) => [item.id, item.name]) ?? []), [categoriesQuery.data]);
  const promotionNames = useMemo(() => new Map(promotionsQuery.data?.items.map((item) => [item.id, item]) ?? []), [promotionsQuery.data]);

  const refreshProducts = () => {
    void queryClient.invalidateQueries({ queryKey: ['catalog', 'products'] });
    void queryClient.invalidateQueries({ queryKey: ['catalog', 'product', selectedProductId] });
    void queryClient.invalidateQueries({ queryKey: ['catalog', 'product-options'] });
  };

  const createMutation = useMutation({
    mutationFn: async ({ kind, body, mediaFiles }: { kind: Tab; body: unknown; mediaFiles?: File[] }) => {
      const created = await api<{ id: string }>(`/api/catalog/admin/${kind}`, { method: 'POST', body });
      if (kind === 'products' && mediaFiles?.length) {
        const uploads = await Promise.all(mediaFiles.map(async (file) => ({ file, upload: await uploadCatalogMedia(file) })));
        for (const [index, { file, upload }] of uploads.entries()) {
          await api(`/api/catalog/admin/products/${created.id}/media`, {
            method: 'POST',
            body: {
              variantId: null,
              mediaType: file.type.startsWith('video/') ? 'Video' : 'Image',
              url: upload.publicUrl,
              thumbnailUrl: null,
              altText: (body as { name: string }).name,
              sortOrder: index,
              expectedVersion: null,
            },
          });
        }
      }
      return { ...created, kind };
    },
    onSuccess: ({ id, kind }) => {
      setCreateOpen(false);
      void queryClient.invalidateQueries({ queryKey: ['catalog', kind] });
      if (kind === 'categories') void queryClient.invalidateQueries({ queryKey: ['catalog', 'category-options'] });
      if (kind === 'promotions') void queryClient.invalidateQueries({ queryKey: ['catalog', 'promotion-options'] });
      if (kind === 'products') setSelectedProductId(id);
    },
  });
  const actionMutation = useMutation({
    mutationFn: ({ path, method = 'POST', body }: { path: string; method?: string; body?: unknown }) => api<void>(path, { method, body }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['catalog', tab] }),
  });
  const productMutation = useMutation({
    mutationFn: ({ path, method = 'POST', body }: { path: string; method?: string; body?: unknown }) => api<void>(path, { method, body }),
    onSuccess: () => {
      setVariantDraft(null);
      refreshProducts();
    },
  });
  const mediaMutation = useMutation({
    mutationFn: async ({ product, file, variantId, altText, sortOrder }: { product: Product; file: File; variantId: string | null; altText: string | null; sortOrder: number }) => {
      const upload = await uploadCatalogMedia(file);
      return api(`/api/catalog/admin/products/${product.id}/media`, {
        method: 'POST',
        body: {
          variantId,
          mediaType: file.type.startsWith('video/') ? 'Video' : 'Image',
          url: upload.publicUrl,
          thumbnailUrl: null,
          altText,
          sortOrder,
          expectedVersion: product.version,
        },
      });
    },
    onSuccess: refreshProducts,
  });

  const create = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    let body: unknown;
    let mediaFiles: File[] | undefined;
    if (tab === 'categories') {
      body = { code: value('code'), name: value('name') };
    } else if (tab === 'products') {
      body = { categoryId: value('categoryId'), sku: value('sku'), name: value('name'), description: value('description'), price: Number(value('price')), currency: value('currency') || 'CNY' };
      mediaFiles = data.getAll('mediaFiles').filter((entry): entry is File => entry instanceof File && entry.size > 0);
    } else if (tab === 'coupons') {
      body = { promotionId: value('promotionId'), code: value('code'), validFromUtc: new Date(value('startsOnUtc')).toISOString(), validUntilUtc: new Date(value('endsOnUtc')).toISOString(), assignedUserId: value('assignedUserId') || null, totalLimit: value('totalLimit') ? Number(value('totalLimit')) : null, perUserLimit: Number(value('perUserLimit') || 1), quotaStrategy: 'SingleRow', bucketCount: null };
    } else {
      const ruleAmount = Number(value('minimumAmount'));
      const benefitType = value('benefitType');
      const benefitValue = Number(value('benefitValue'));
      body = { code: value('code'), name: value('name'), triggerType: value('triggerType'), startsOnUtc: new Date(value('startsOnUtc')).toISOString(), endsOnUtc: new Date(value('endsOnUtc')).toISOString(), priority: Number(value('priority') || 0), stackPolicy: value('stackPolicy'), exclusiveGroup: value('exclusiveGroup') || null, rules: ruleAmount > 0 ? [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: JSON.stringify({ amount: ruleAmount }), sequence: 0 }] : [], benefits: [{ benefitType, schemaVersion: 1, configurationJson: JSON.stringify(benefitType === 'PercentageOff' ? { percentage: benefitValue } : { amount: benefitValue }), sequence: 0 }], targets: [{ targetType: 'All', targetKey: null, isExcluded: false }] };
    }
    createMutation.mutate({ kind: tab, body, mediaFiles });
  };

  const stateAction = (row: CatalogRow, action: string, reason?: boolean) => {
    const note = reason ? window.prompt('请输入状态变更原因（可留空）') : null;
    actionMutation.mutate({ path: `/api/catalog/admin/${tab}/${row.id}/${action}`, body: { expectedVersion: row.version, ...(reason ? { reason: note } : {}) } });
  };

  const editCategory = (row: Category) => {
    const name = window.prompt('新的分类名称', row.name)?.trim();
    if (name) actionMutation.mutate({ path: `/api/catalog/admin/categories/${row.id}/name`, method: 'PUT', body: { name, expectedVersion: row.version } });
  };

  const saveProductDetails = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const product = productQuery.data;
    if (!product) return;
    const data = new FormData(event.currentTarget);
    productMutation.mutate({
      path: `/api/catalog/admin/products/${product.id}/details`,
      method: 'PUT',
      body: { name: String(data.get('name') ?? '').trim(), description: String(data.get('description') ?? '').trim(), expectedVersion: product.version },
    });
  };

  const saveVariant = (payload: { sku?: string; displayName: string; attributesJson: string; barcode: string | null; price: number; currency: string; isActive: boolean }) => {
    const product = productQuery.data;
    if (!product || !variantDraft) return;
    if (variantDraft === 'new') {
      productMutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants`, body: { ...payload, sku: payload.sku, expectedVersion: product.version } });
    } else {
      const { sku: _, ...update } = payload;
      productMutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants/${variantDraft.id}`, method: 'PUT', body: { ...update, expectedVersion: product.version } });
    }
  };

  const deleteVariant = (product: Product, variant: ProductVariant) => {
    if (!window.confirm(`确定删除 SKU“${variant.sku}”吗？`)) return;
    productMutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants/${variant.id}?expectedVersion=${product.version}`, method: 'DELETE' });
  };

  const uploadMedia = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const product = productQuery.data;
    if (!product) return;
    const data = new FormData(event.currentTarget);
    const file = data.get('mediaFile');
    if (!(file instanceof File) || file.size === 0) return;
    mediaMutation.mutate({
      product,
      file,
      variantId: String(data.get('variantId') ?? '') || null,
      altText: String(data.get('altText') ?? '').trim() || null,
      sortOrder: Number(data.get('sortOrder') ?? 0),
    });
  };

  const deleteMedia = (product: Product, media: ProductMedia) => {
    if (!window.confirm('确定从商品中移除这个媒体文件吗？')) return;
    productMutation.mutate({ path: `/api/catalog/admin/products/${product.id}/media/${media.id}?expectedVersion=${product.version}`, method: 'DELETE' });
  };

  return <div className="page">
    <PageHeader eyebrow="CATALOG / MERCHANDISING" title="商品与营销目录" description="以 SPU 维护商品内容，以 SKU 维护规格、价格和可售状态；媒体文件直接上传到 RustFS。" actions={can(`${module}.create`) ? <button className="button button--dark" type="button" onClick={() => setCreateOpen(true)}><Icon name="plus" />新建{tabs.find((item) => item.id === tab)?.label}</button> : undefined} />
    <div className="catalog-toolbar"><div className="tabs">{tabs.filter((item) => can(`${item.module}.query`) || can(`${item.module}.create`) || can(`${item.module}.edit`)).map((item) => <button type="button" key={item.id} aria-selected={tab === item.id} onClick={() => { setTab(item.id); setStatus(''); setSearch(''); }}>{item.label}</button>)}</div><div className="filters">{tab === 'products' ? <label><Icon name="search" /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="名称或 SKU" /></label> : null}<select value={status} onChange={(event) => setStatus(event.target.value)} aria-label="状态筛选"><option value="">全部状态</option><option value="Draft">Draft</option><option value="Active">Active</option><option value="Published">Published</option><option value="Paused">Paused</option><option value="Archived">Archived</option><option value="Disabled">Disabled</option></select></div></div>
    {listQuery.isError ? <ErrorNotice error={listQuery.error} /> : null}
    {actionMutation.isError ? <ErrorNotice error={actionMutation.error} /> : null}
    <section className="panel table-panel"><div className="section-heading"><div><p className="eyebrow">LIVE CATALOG</p><h2>{tabs.find((item) => item.id === tab)?.label}列表</h2></div><span>{listQuery.data?.total ?? 0} 条</span></div>{listQuery.data?.items.length ? <CatalogTable tab={tab} rows={listQuery.data.items} canEdit={can(`${module}.edit`)} categoryNames={categoryNames} promotionNames={promotionNames} onState={stateAction} onEditCategory={editCategory} onOpenProduct={setSelectedProductId} /> : !listQuery.isFetching ? <Empty title="没有可展示的数据" detail={can(`${module}.query`) ? '调整筛选条件，或创建第一条记录。' : '当前身份没有该模块的查询权限。'} /> : null}</section>
    <p className="capability-note"><Icon name="alert" />顶层商品采用归档而不是物理删除；SKU 与媒体在商品工作台内管理。</p>

    <Modal open={createOpen} wide={tab === 'products'} title={`新建${tabs.find((item) => item.id === tab)?.label}`} description={tab === 'products' ? '先创建 SPU 和默认 SKU；提交后会进入商品工作台继续维护规格与媒体。' : '表单字段直接对应当前 CatalogService 契约。'} onClose={() => { setCreateOpen(false); createMutation.reset(); }}><form key={tab} className="modal-form" onSubmit={create}>
      {tab === 'products' ? <ProductCreateFields categories={categoriesQuery.data?.items ?? []} canReadCategories={can('catalog.categories.query')} canUploadMedia={can('catalog.products.edit')} /> : null}
      {tab === 'categories' ? <div className="form-row"><Field label="分类代码"><input name="code" required /></Field><Field label="分类名称"><input name="name" required /></Field></div> : null}
      {tab === 'promotions' ? <PromotionFields /> : null}
      {tab === 'coupons' ? <CouponFields promotions={promotionsQuery.data?.items ?? []} canReadPromotions={can('catalog.promotions.query')} /> : null}
      {createMutation.isError ? <ErrorNotice error={createMutation.error} /> : null}<SubmitBar busy={createMutation.isPending} label={createMutation.isPending && tab === 'products' ? '正在创建并上传媒体…' : '创建记录'} onCancel={() => setCreateOpen(false)} />
    </form></Modal>

    <Modal open={selectedProductId !== null} wide title={productQuery.data?.name ?? '商品工作台'} description={productQuery.data ? `${categoryNames.get(productQuery.data.categoryId) ?? '未命名分类'} · ${productQuery.data.variants?.length ?? 0} 个 SKU · ${productQuery.data.media?.length ?? 0} 个媒体` : '读取商品信息'} onClose={() => { setSelectedProductId(null); setVariantDraft(null); productMutation.reset(); mediaMutation.reset(); }}>
      {productQuery.isPending ? <Loading label="正在读取商品配置" /> : null}
      {productQuery.isError ? <div className="modal-form"><ErrorNotice error={productQuery.error} /></div> : null}
      {productQuery.data ? <ProductStudio product={productQuery.data} variantDraft={variantDraft} canEdit={can('catalog.products.edit')} productBusy={productMutation.isPending} mediaBusy={mediaMutation.isPending} productError={productMutation.error} mediaError={mediaMutation.error} onSaveDetails={saveProductDetails} onEditVariant={setVariantDraft} onSaveVariant={saveVariant} onDeleteVariant={deleteVariant} onUploadMedia={uploadMedia} onDeleteMedia={deleteMedia} /> : null}
    </Modal>
  </div>;
}

function CatalogTable({ tab, rows, canEdit, categoryNames, promotionNames, onState, onEditCategory, onOpenProduct }: { tab: Tab; rows: CatalogRow[]; canEdit: boolean; categoryNames: Map<string, string>; promotionNames: Map<string, Promotion>; onState: (row: CatalogRow, action: string, reason?: boolean) => void; onEditCategory: (row: Category) => void; onOpenProduct: (id: string) => void }) {
  if (tab === 'products') return <div className="data-table"><table><thead><tr><th>商品</th><th>分类</th><th>SKU</th><th>价格区间</th><th>状态</th><th>更新时间</th><th>操作</th></tr></thead><tbody>{(rows as Product[]).map((row) => {
    const cover = row.media?.find((item) => item.mediaType === 'Image');
    return <tr key={row.id}><td><div className="product-cell">{cover ? <img src={cover.url} alt="" /> : <span><Icon name="catalog" /></span>}<div><strong>{row.name}</strong><small>{row.description}</small></div></div></td><td>{categoryNames.get(row.categoryId) ?? row.categoryId.slice(0, 8)}</td><td><strong>{row.variants?.length ?? 1}</strong><small>{row.sku}</small></td><td>{formatMoney(row.minPrice ?? row.price, row.currency)}{row.maxPrice !== undefined && row.maxPrice !== (row.minPrice ?? row.price) ? <small>至 {formatMoney(row.maxPrice, row.currency)}</small> : null}</td><td><Status>{row.lifecycleStatus}</Status></td><td>{formatDate(row.updatedOnUtc)}</td><td><div className="row-actions">{canEdit ? <><button className="row-action-primary" type="button" onClick={() => onOpenProduct(row.id)}>商品工作台</button>{row.lifecycleStatus === 'Draft' ? <button type="button" onClick={() => onState(row, 'publish')}>发布</button> : <button type="button" onClick={() => onState(row, 'unpublish', true)}>下架</button>}<button type="button" onClick={() => onState(row, 'archive', true)}>归档</button></> : <button type="button" onClick={() => onOpenProduct(row.id)}>查看</button>}</div></td></tr>;
  })}</tbody></table></div>;
  if (tab === 'categories') return <div className="data-table"><table><thead><tr><th>分类</th><th>代码</th><th>状态</th><th>版本</th><th>操作</th></tr></thead><tbody>{(rows as Category[]).map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.id.slice(0, 8)}</small></td><td className="mono">{row.code}</td><td><Status>{row.status}</Status></td><td>v{row.version}</td><td><div className="row-actions">{canEdit ? <><button type="button" onClick={() => onEditCategory(row)}>重命名</button>{row.status === 'Draft' ? <button type="button" onClick={() => onState(row, 'activate')}>启用</button> : null}<button type="button" onClick={() => onState(row, 'archive')}>归档</button></> : '—'}</div></td></tr>)}</tbody></table></div>;
  if (tab === 'promotions') return <div className="data-table"><table><thead><tr><th>促销</th><th>触发</th><th>周期</th><th>优先级</th><th>状态</th><th>操作</th></tr></thead><tbody>{(rows as Promotion[]).map((row) => <tr key={row.id}><td><strong>{row.name}</strong><small>{row.code}</small></td><td>{row.triggerType}</td><td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)}</small></td><td>{row.priority}</td><td><Status>{row.status}</Status></td><td><div className="row-actions">{canEdit ? <>{row.status === 'Draft' ? <button type="button" onClick={() => onState(row, 'publish')}>发布</button> : null}{row.status === 'Published' ? <button type="button" onClick={() => onState(row, 'pause')}>暂停</button> : null}{row.status === 'Paused' ? <button type="button" onClick={() => onState(row, 'resume')}>恢复</button> : null}<button type="button" onClick={() => onState(row, 'end')}>结束</button></> : '—'}</div></td></tr>)}</tbody></table></div>;
  return <div className="data-table"><table><thead><tr><th>优惠券</th><th>促销活动</th><th>有效期</th><th>配额</th><th>状态</th><th>操作</th></tr></thead><tbody>{(rows as Coupon[]).map((row) => { const promotion = promotionNames.get(row.promotionId); return <tr key={row.id}><td><strong>{row.maskedCode}</strong><small>{row.id.slice(0, 8)}</small></td><td><strong>{promotion?.name ?? '未读取到活动'}</strong><small>{promotion?.code ?? row.promotionId}</small></td><td>{formatDate(row.validFromUtc)}<small>至 {formatDate(row.validUntilUtc)}</small></td><td>{row.totalLimit ?? '不限'} / 每人 {row.perUserLimit}</td><td><Status>{row.status}</Status></td><td><div className="row-actions">{canEdit ? row.status !== 'Active' ? <button type="button" onClick={() => onState(row, 'activate')}>启用</button> : <button type="button" onClick={() => onState(row, 'disable', true)}>停用</button> : '—'}</div></td></tr>; })}</tbody></table></div>;
}

function ProductCreateFields({ categories, canReadCategories, canUploadMedia }: { categories: Category[]; canReadCategories: boolean; canUploadMedia: boolean }) {
  return <>
    <div className="form-section-title"><span>01</span><div><strong>基础信息</strong><small>SPU 负责名称、描述和归属分类</small></div></div>
    <Field label="商品分类" hint={!canReadCategories ? '需要 catalog.categories.query 才能选择分类' : undefined}><select name="categoryId" required disabled={!canReadCategories}><option value="">选择分类</option>{categories.filter((item) => item.status !== 'Archived').map((item) => <option key={item.id} value={item.id}>{item.name} · {item.code}</option>)}</select></Field>
    <Field label="商品名称"><input name="name" required maxLength={200} autoFocus /></Field>
    <Field label="商品描述"><textarea name="description" required rows={4} maxLength={4000} /></Field>
    <div className="form-section-title"><span>02</span><div><strong>默认 SKU</strong><small>创建后可继续增加颜色、尺码等规格</small></div></div>
    <div className="form-row"><Field label="SKU 编码"><input name="sku" required maxLength={80} placeholder="例如 TSHIRT-BLK-M" /></Field><Field label="币种"><select name="currency" defaultValue="CNY"><option value="CNY">CNY · 人民币</option><option value="USD">USD · 美元</option><option value="EUR">EUR · 欧元</option></select></Field></div>
    <Field label="销售价格"><input name="price" required min="0.01" step="0.01" type="number" /></Field>
    <div className="form-section-title"><span>03</span><div><strong>商品媒体</strong><small>{canUploadMedia ? '选填；提交时直传 RustFS public bucket，并把永久 URL 写入商品媒体' : '需要 catalog.products.edit 权限，创建后由商品编辑人员补充'}</small></div></div>
    {canUploadMedia ? <MediaFilesInput /> : null}
  </>;
}

function MediaFilesInput() {
  const [files, setFiles] = useState<File[]>([]);
  return <Field label="图片或视频" hint="图片最大 20 MB，视频最大 512 MB；支持 JPEG、PNG、WebP、GIF、AVIF、MP4、WebM、MOV">
    <input name="mediaFiles" type="file" accept="image/jpeg,image/png,image/webp,image/gif,image/avif,video/mp4,video/webm,video/quicktime" multiple onChange={(event) => setFiles(Array.from(event.target.files ?? []))} />
    {files.length ? <div className="selected-media">{files.map((file) => <LocalMediaPreview key={`${file.name}-${file.lastModified}`} file={file} />)}</div> : null}
  </Field>;
}

function LocalMediaPreview({ file }: { file: File }) {
  const preview = useMemo(() => URL.createObjectURL(file), [file]);
  useEffect(() => () => URL.revokeObjectURL(preview), [preview]);
  return <span>{file.type.startsWith('image/') ? <img src={preview} alt="" /> : <video src={preview} muted /> }<small>{file.name}</small></span>;
}

function PromotionFields() {
  return <><div className="form-row"><Field label="活动代码"><input name="code" required /></Field><Field label="活动名称"><input name="name" required /></Field></div><div className="form-row"><Field label="触发方式"><select name="triggerType"><option>Automatic</option><option>Coupon</option></select></Field><Field label="叠加策略"><select name="stackPolicy"><option>Stackable</option><option>Exclusive</option></select></Field></div><div className="form-row"><Field label="开始时间"><input name="startsOnUtc" type="datetime-local" defaultValue={asLocalInput()} required /></Field><Field label="结束时间"><input name="endsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date(Date.now() + 7 * 86_400_000))} required /></Field></div><div className="form-row"><Field label="最低订单金额"><input name="minimumAmount" type="number" min="0" step="0.01" placeholder="可选" /></Field><Field label="优先级"><input name="priority" type="number" defaultValue="0" /></Field></div><div className="form-row"><Field label="优惠类型"><select name="benefitType"><option>PercentageOff</option><option>FixedAmountOff</option></select></Field><Field label="优惠值"><input name="benefitValue" type="number" min="0.01" step="0.01" required /></Field></div><Field label="互斥组"><input name="exclusiveGroup" placeholder="可选" /></Field></>;
}

function CouponFields({ promotions, canReadPromotions }: { promotions: Promotion[]; canReadPromotions: boolean }) {
  return <><div className="form-row"><Field label="促销活动" hint={!canReadPromotions ? '需要 catalog.promotions.query 才能选择活动' : undefined}><select name="promotionId" required disabled={!canReadPromotions}><option value="">选择促销活动</option>{promotions.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.code} · {item.status}</option>)}</select></Field><Field label="券码"><input name="code" required /></Field></div><div className="form-row"><Field label="生效时间"><input name="startsOnUtc" type="datetime-local" defaultValue={asLocalInput()} required /></Field><Field label="失效时间"><input name="endsOnUtc" type="datetime-local" defaultValue={asLocalInput(new Date(Date.now() + 7 * 86_400_000))} required /></Field></div><div className="form-row"><Field label="总额度"><input name="totalLimit" min="1" type="number" /></Field><Field label="每用户额度"><input name="perUserLimit" min="1" type="number" defaultValue="1" required /></Field></div><Field label="指定用户 ID"><input name="assignedUserId" placeholder="留空表示不定向" /></Field></>;
}

function ProductStudio({ product, variantDraft, canEdit, productBusy, mediaBusy, productError, mediaError, onSaveDetails, onEditVariant, onSaveVariant, onDeleteVariant, onUploadMedia, onDeleteMedia }: { product: Product; variantDraft: VariantDraft; canEdit: boolean; productBusy: boolean; mediaBusy: boolean; productError: unknown; mediaError: unknown; onSaveDetails: (event: FormEvent<HTMLFormElement>) => void; onEditVariant: (value: VariantDraft) => void; onSaveVariant: (payload: { sku?: string; displayName: string; attributesJson: string; barcode: string | null; price: number; currency: string; isActive: boolean }) => void; onDeleteVariant: (product: Product, variant: ProductVariant) => void; onUploadMedia: (event: FormEvent<HTMLFormElement>) => void; onDeleteMedia: (product: Product, media: ProductMedia) => void }) {
  const variants = product.variants ?? [];
  const media = product.media ?? [];
  return <div className="product-studio">
    <section className="product-studio__summary"><div><Status>{product.lifecycleStatus}</Status><span className="mono">{product.id}</span></div><dl><div><dt>SKU</dt><dd>{variants.length}</dd></div><div><dt>媒体</dt><dd>{media.length}</dd></div><div><dt>价格</dt><dd>{formatMoney(product.minPrice ?? product.price, product.currency)}{product.maxPrice !== undefined && product.maxPrice !== (product.minPrice ?? product.price) ? ` – ${formatMoney(product.maxPrice, product.currency)}` : ''}</dd></div></dl></section>
    {canEdit ? <form key={`details-${product.version}`} className="studio-section studio-details" onSubmit={onSaveDetails}><div className="studio-heading"><div><span>商品内容</span><h3>SPU 基础信息</h3></div><button className="button button--quiet" type="submit" disabled={productBusy}>保存内容</button></div><Field label="商品名称"><input name="name" defaultValue={product.name} required maxLength={200} /></Field><Field label="商品描述"><textarea name="description" defaultValue={product.description} required rows={3} maxLength={4000} /></Field></form> : null}
    <section className="studio-section"><div className="studio-heading"><div><span>SELLABLE VARIANTS</span><h3>SKU 与规格</h3></div>{canEdit && variantDraft === null ? <button className="button button--quiet" type="button" onClick={() => onEditVariant('new')}><Icon name="plus" />添加 SKU</button> : null}</div>
      {variantDraft ? <VariantForm key={variantDraft === 'new' ? 'new' : variantDraft.id} item={variantDraft === 'new' ? undefined : variantDraft} busy={productBusy} error={productError} onSave={onSaveVariant} onCancel={() => onEditVariant(null)} /> : <div className="variant-list">{variants.map((variant) => <article key={variant.id}><div><strong>{variant.displayName}</strong><small className="mono">{variant.sku}{variant.barcode ? ` · ${variant.barcode}` : ''}</small><VariantAttributes json={variant.attributesJson} /></div><div><strong>{formatMoney(variant.price, variant.currency)}</strong><Status>{variant.isActive ? 'Active' : 'Disabled'}</Status>{variant.isDefault ? <small>默认 SKU</small> : null}</div>{canEdit ? <div className="row-actions"><button type="button" onClick={() => onEditVariant(variant)}>编辑</button>{!variant.isDefault ? <button className="danger-link" type="button" onClick={() => onDeleteVariant(product, variant)}>删除</button> : null}</div> : null}</article>)}</div>}
    </section>
    <section className="studio-section"><div className="studio-heading"><div><span>RUSTFS MEDIA</span><h3>图片与视频</h3></div><span>{media.length} 个对象</span></div>
      {media.length ? <div className="media-grid">{media.map((item) => <article key={item.id}><div className="media-frame">{item.mediaType === 'Video' ? <video src={item.url} controls preload="metadata" /> : <img src={item.url} alt={item.altText ?? ''} loading="lazy" />}</div><div><strong>{item.altText || (item.mediaType === 'Video' ? '商品视频' : '商品图片')}</strong><small>{item.variantId ? variants.find((variant) => variant.id === item.variantId)?.displayName ?? 'SKU 媒体' : 'SPU 通用媒体'} · 排序 {item.sortOrder}</small></div>{canEdit ? <button className="danger-link" type="button" onClick={() => onDeleteMedia(product, item)}>移除</button> : null}</article>)}</div> : <Empty title="还没有商品媒体" detail="上传图片或视频后，其 public bucket 永久 URL 会写入 Catalog 数据库。" />}
      {canEdit ? <form className="media-upload" onSubmit={onUploadMedia}><Field label="选择文件"><input name="mediaFile" type="file" accept="image/jpeg,image/png,image/webp,image/gif,image/avif,video/mp4,video/webm,video/quicktime" required /></Field><Field label="关联范围"><select name="variantId"><option value="">整个商品（SPU）</option>{variants.map((variant) => <option key={variant.id} value={variant.id}>{variant.displayName} · {variant.sku}</option>)}</select></Field><Field label="替代文本"><input name="altText" maxLength={300} placeholder="描述图片内容，便于无障碍访问" /></Field><Field label="排序"><input name="sortOrder" type="number" min="0" max="10000" defaultValue={media.length} /></Field><button className="button button--dark" type="submit" disabled={mediaBusy}>{mediaBusy ? '正在上传…' : '上传并写入商品'}</button></form> : null}
      {mediaError ? <ErrorNotice error={mediaError} /> : null}
    </section>
    {productError && !variantDraft ? <ErrorNotice error={productError} /> : null}
  </div>;
}

function VariantForm({ item, busy, error, onSave, onCancel }: { item?: ProductVariant; busy: boolean; error: unknown; onSave: (payload: { sku?: string; displayName: string; attributesJson: string; barcode: string | null; price: number; currency: string; isActive: boolean }) => void; onCancel: () => void }) {
  const [attributes, setAttributes] = useState(() => parseAttributes(item?.attributesJson));
  const save = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const values = Object.fromEntries(attributes.filter((attribute) => attribute.key.trim()).map((attribute) => [attribute.key.trim(), attribute.value.trim()]));
    onSave({ sku: item ? undefined : String(data.get('sku') ?? '').trim(), displayName: String(data.get('displayName') ?? '').trim(), attributesJson: JSON.stringify(values), barcode: String(data.get('barcode') ?? '').trim() || null, price: Number(data.get('price')), currency: String(data.get('currency') ?? 'CNY'), isActive: data.has('isActive') });
  };
  return <form className="variant-editor" onSubmit={save}><div className="form-row">{item ? <Field label="SKU 编码"><input value={item.sku} disabled /></Field> : <Field label="SKU 编码"><input name="sku" required maxLength={80} autoFocus /></Field>}<Field label="规格显示名"><input name="displayName" defaultValue={item?.displayName} required maxLength={200} placeholder="例如 黑色 / M" /></Field></div><div className="form-row"><Field label="条码"><input name="barcode" defaultValue={item?.barcode} maxLength={80} /></Field><Field label="币种"><select name="currency" defaultValue={item?.currency ?? 'CNY'}><option value="CNY">CNY · 人民币</option><option value="USD">USD · 美元</option><option value="EUR">EUR · 欧元</option></select></Field></div><Field label="价格"><input name="price" type="number" min="0" step="0.01" defaultValue={item?.price} required /></Field><fieldset className="attribute-editor"><legend>规格属性</legend>{attributes.map((attribute, index) => <div key={index}><input aria-label={`属性名 ${index + 1}`} value={attribute.key} onChange={(event) => setAttributes((current) => current.map((value, itemIndex) => itemIndex === index ? { ...value, key: event.target.value } : value))} placeholder="属性名，如 颜色" /><input aria-label={`属性值 ${index + 1}`} value={attribute.value} onChange={(event) => setAttributes((current) => current.map((value, itemIndex) => itemIndex === index ? { ...value, value: event.target.value } : value))} placeholder="属性值，如 黑色" /><button type="button" aria-label="删除属性" onClick={() => setAttributes((current) => current.filter((_, itemIndex) => itemIndex !== index))}><Icon name="close" /></button></div>)}<button type="button" onClick={() => setAttributes((current) => [...current, { key: '', value: '' }])}><Icon name="plus" />添加属性</button></fieldset><label className="switch-row"><input name="isActive" type="checkbox" defaultChecked={item?.isActive ?? true} /><span />SKU 可售</label>{error ? <ErrorNotice error={error} /> : null}<SubmitBar busy={busy} label={item ? '保存 SKU' : '添加 SKU'} onCancel={onCancel} /></form>;
}

function parseAttributes(json?: string) {
  if (!json) return [{ key: '', value: '' }];
  try {
    const value = JSON.parse(json) as Record<string, unknown>;
    const entries = Object.entries(value).map(([key, item]) => ({ key, value: String(item) }));
    return entries.length ? entries : [{ key: '', value: '' }];
  } catch {
    return [{ key: '', value: '' }];
  }
}

function VariantAttributes({ json }: { json: string }) {
  const attributes = parseAttributes(json).filter((item) => item.key);
  return attributes.length ? <span className="tag-list">{attributes.map((item) => <span key={item.key}>{item.key}: {item.value}</span>)}</span> : <small>无规格属性</small>;
}
