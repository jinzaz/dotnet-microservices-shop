import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, catalogMediaPolicy, discardCatalogMedia, uploadCatalogMedia, validateCatalogMedia } from '../../api';
import { CopyButton, Dropzone, Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, Status, SubmitBar, UploadList, useConfirm, useToast, type UploadItem } from '../../ui';

export type ProductVariant = { id: string; productId: string; sku: string; displayName: string; attributesJson: string; barcode?: string; price: number; currency: string; isActive: boolean; isDefault: boolean; updatedOnUtc: string };
export type ProductMedia = { id: string; productId: string; variantId?: string; mediaType: 'Image' | 'Video'; url: string; thumbnailUrl?: string; altText?: string; sortOrder: number };
export type Product = { id: string; categoryId: string; sku: string; name: string; description: string; price: number; currency: string; lifecycleStatus: string; version: number; updatedOnUtc: string; variants?: ProductVariant[]; media?: ProductMedia[]; minPrice?: number; maxPrice?: number };

type VariantPayload = { sku?: string; displayName: string; attributesJson: string; barcode: string | null; price: number; currency: string; isActive: boolean };

export function newUploadItems(files: File[]): UploadItem[] {
  return files.map((file) => ({ id: crypto.randomUUID(), file, progress: 0, error: validateCatalogMedia(file) ?? undefined }));
}

/**
 * Uploads each pending file, then writes it to the product one at a time. Catalog media writes bump the product
 * version, so they are sequential and each uses the version returned by a fresh read.
 */
export async function attachMedia(productId: string, items: UploadItem[], altText: string | null, variantId: string | null, startSortOrder: number, onChange: (item: UploadItem) => void) {
  const failures: string[] = [];
  let sortOrder = startSortOrder;
  for (const item of items.filter((value) => !value.error && !value.done)) {
    try {
      const uploaded = await uploadCatalogMedia(item.file, (progress) => onChange({ ...item, progress: Math.min(progress, 0.97) }));
      try {
        const current = await api<Product>(`/api/catalog/admin/products/${productId}`);
        await api(`/api/catalog/admin/products/${productId}/media`, {
          method: 'POST',
          body: { variantId, mediaType: item.file.type.startsWith('video/') ? 'Video' : 'Image', url: uploaded.publicUrl, thumbnailUrl: null, altText, sortOrder: sortOrder++, expectedVersion: current.version },
        });
      } catch (error) {
        // The object is verified but Catalog rejected it: remove it instead of leaving an orphan behind.
        await discardCatalogMedia({ objectKey: uploaded.objectKey });
        throw error;
      }
      onChange({ ...item, progress: 1, done: true });
    } catch (error) {
      const message = error instanceof Error ? error.message : '上传失败';
      failures.push(`${item.file.name}：${message}`);
      onChange({ ...item, error: message });
    }
  }
  return failures;
}

export function ProductStudio({ product, categoryName, canEdit }: { product: Product; categoryName?: string; canEdit: boolean }) {
  const client = useQueryClient();
  const toast = useToast();
  const confirm = useConfirm();
  const [variantDraft, setVariantDraft] = useState<ProductVariant | 'new' | null>(null);
  const [uploads, setUploads] = useState<UploadItem[]>([]);
  const [uploadScope, setUploadScope] = useState('');
  const variants = product.variants ?? [];
  const media = [...(product.media ?? [])].sort((left, right) => left.sortOrder - right.sortOrder);

  const refresh = () => client.invalidateQueries({ queryKey: ['catalog'] });
  const mutation = useMutation({
    mutationFn: ({ path, method = 'POST', body }: { path: string; method?: string; body?: unknown; success: string }) => api<void>(path, { method, body }),
    onSuccess: (_, { success }) => {
      toast.success(success);
      setVariantDraft(null);
      return refresh();
    },
    onError: (error) => toast.error(error),
  });
  const uploadMutation = useMutation({
    mutationFn: () => attachMedia(product.id, uploads, product.name, uploadScope || null, media.length, (item) => setUploads((current) => current.map((value) => value.id === item.id ? item : value))),
    onSuccess: async (failures) => {
      await refresh();
      if (failures.length) {
        toast.error(`${failures.length} 个文件未能上传，请查看列表中的错误。`);
      } else {
        toast.success('媒体已上传并写入商品');
        setUploads([]);
      }
    },
  });

  const saveDetails = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    mutation.mutate({
      path: `/api/catalog/admin/products/${product.id}/details`,
      method: 'PUT',
      body: { name: String(data.get('name') ?? '').trim(), description: String(data.get('description') ?? '').trim(), expectedVersion: product.version },
      success: '商品内容已保存',
    });
  };

  const saveVariant = (payload: VariantPayload) => {
    if (!variantDraft) return;
    if (variantDraft === 'new') {
      mutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants`, body: payload, success: `SKU ${payload.sku} 已添加` });
    } else {
      const { sku: _, ...update } = payload;
      mutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants/${variantDraft.id}`, method: 'PUT', body: { ...update, expectedVersion: product.version }, success: `SKU ${variantDraft.sku} 已更新` });
    }
  };

  const deleteVariant = async (variant: ProductVariant) => {
    const result = await confirm({ title: `删除 SKU ${variant.sku}？`, detail: '该 SKU 关联的媒体会一并移除，已下单记录不受影响。', confirmLabel: '删除 SKU', tone: 'danger' });
    if (result) mutation.mutate({ path: `/api/catalog/admin/products/${product.id}/variants/${variant.id}?expectedVersion=${product.version}`, method: 'DELETE', success: `SKU ${variant.sku} 已删除` });
  };

  const deleteMedia = async (item: ProductMedia) => {
    const result = await confirm({ title: '移除这个媒体文件？', detail: '文件会从商品中移除，并从对象存储中删除。', confirmLabel: '移除', tone: 'danger' });
    if (!result) return;
    mutation.mutate({ path: `/api/catalog/admin/products/${product.id}/media/${item.id}?expectedVersion=${product.version}`, method: 'DELETE', success: '媒体已移除' }, {
      onSuccess: () => void discardCatalogMedia({ publicUrl: item.url }),
    });
  };

  // Swaps sort orders with the current cover; each write re-reads the product version it must match.
  const coverMutation = useMutation({
    mutationFn: async (item: ProductMedia) => {
      const cover = media[0];
      if (!cover) return;
      const put = async (target: ProductMedia, sortOrder: number) => {
        const current = await api<Product>(`/api/catalog/admin/products/${product.id}`);
        await api(`/api/catalog/admin/products/${product.id}/media/${target.id}`, {
          method: 'PUT',
          body: { variantId: target.variantId ?? null, mediaType: target.mediaType, url: target.url, thumbnailUrl: target.thumbnailUrl ?? null, altText: target.altText ?? null, sortOrder, expectedVersion: current.version },
        });
      };
      await put(cover, item.sortOrder === cover.sortOrder ? cover.sortOrder + 1 : item.sortOrder);
      await put(item, cover.sortOrder);
    },
    onSuccess: () => { toast.success('已设为封面'); return refresh(); },
    onError: (error) => { toast.error(error); void refresh(); },
  });
  const makeCover = (item: ProductMedia) => coverMutation.mutate(item);

  const priceRange = product.maxPrice !== undefined && product.maxPrice !== (product.minPrice ?? product.price)
    ? `${formatMoney(product.minPrice ?? product.price, product.currency)} – ${formatMoney(product.maxPrice, product.currency)}`
    : formatMoney(product.minPrice ?? product.price, product.currency);

  return <div className="product-studio">
    <section className="product-studio__summary">
      <div><Status>{product.lifecycleStatus}</Status><span className="mono">{product.id}<CopyButton value={product.id} label="复制商品 ID" /></span></div>
      <dl><div><dt>分类</dt><dd>{categoryName ?? '—'}</dd></div><div><dt>SKU</dt><dd>{variants.length}</dd></div><div><dt>媒体</dt><dd>{media.length}</dd></div><div><dt>价格</dt><dd>{priceRange}</dd></div><div><dt>更新</dt><dd>{formatDate(product.updatedOnUtc)}</dd></div></dl>
    </section>

    {canEdit ? <form key={`details-${product.version}`} className="studio-section studio-details" onSubmit={saveDetails}>
      <div className="studio-heading"><div><span>商品内容</span><h3>SPU 基础信息</h3></div><button className="button button--quiet" type="submit" disabled={mutation.isPending}>保存内容</button></div>
      <Field label="商品名称"><input name="name" defaultValue={product.name} required maxLength={200} /></Field>
      <Field label="商品描述"><textarea name="description" defaultValue={product.description} required rows={3} maxLength={4000} /></Field>
    </form> : null}

    <section className="studio-section">
      <div className="studio-heading"><div><span>SELLABLE VARIANTS</span><h3>SKU 与规格</h3></div>{canEdit && variantDraft === null ? <button className="button button--quiet" type="button" onClick={() => setVariantDraft('new')}><Icon name="plus" />添加 SKU</button> : null}</div>
      {variantDraft ? <VariantForm key={variantDraft === 'new' ? 'new' : variantDraft.id} item={variantDraft === 'new' ? undefined : variantDraft} defaultCurrency={product.currency} busy={mutation.isPending} error={mutation.error} onSave={saveVariant} onCancel={() => { setVariantDraft(null); mutation.reset(); }} />
        : <div className="variant-list">{variants.map((variant) => <article key={variant.id}>
          <div><strong>{variant.displayName}</strong><small className="mono">{variant.sku}{variant.barcode ? ` · ${variant.barcode}` : ''}</small><VariantAttributes json={variant.attributesJson} /></div>
          <div><strong>{formatMoney(variant.price, variant.currency)}</strong><Status>{variant.isActive ? 'Active' : 'Disabled'}</Status>{variant.isDefault ? <small>默认 SKU</small> : null}</div>
          {canEdit ? <div className="row-actions"><button type="button" onClick={() => setVariantDraft(variant)}>编辑</button>{!variant.isDefault ? <button className="danger-link" type="button" onClick={() => void deleteVariant(variant)}>删除</button> : null}</div> : null}
        </article>)}</div>}
    </section>

    <section className="studio-section">
      <div className="studio-heading"><div><span>RUSTFS MEDIA</span><h3>图片与视频</h3></div><span>{media.length} 个对象 · 第一张为封面</span></div>
      {media.length ? <div className="media-grid">{media.map((item, index) => <article key={item.id}>
        <div className="media-frame">{item.mediaType === 'Video' ? <video src={item.url} controls preload="metadata" /> : <a href={item.url} target="_blank" rel="noreferrer"><img src={item.url} alt={item.altText ?? ''} loading="lazy" /></a>}{index === 0 ? <b className="media-badge">封面</b> : null}</div>
        <div><strong>{item.altText || (item.mediaType === 'Video' ? '商品视频' : '商品图片')}</strong><small>{item.variantId ? variants.find((variant) => variant.id === item.variantId)?.displayName ?? 'SKU 媒体' : 'SPU 通用媒体'} · 排序 {item.sortOrder}</small></div>
        {canEdit ? <div className="row-actions">{index !== 0 && item.mediaType === 'Image' ? <button type="button" onClick={() => makeCover(item)}>设为封面</button> : null}<button className="danger-link" type="button" onClick={() => void deleteMedia(item)}>移除</button></div> : null}
      </article>)}</div> : <Empty title="还没有商品媒体" detail="上传图片或视频后，校验通过的 public bucket URL 会写入 Catalog。" />}
      {canEdit ? <div className="media-upload">
        <Dropzone accept={catalogMediaPolicy.accept} multiple disabled={uploadMutation.isPending} hint="图片 ≤ 20 MB，视频 ≤ 512 MB；支持 JPEG、PNG、WebP、GIF、AVIF、MP4、WebM、MOV" onFiles={(files) => setUploads((current) => [...current.filter((item) => !item.done), ...newUploadItems(files)])} />
        <UploadList items={uploads} onRemove={(id) => setUploads((current) => current.filter((item) => item.id !== id))} />
        {uploads.some((item) => !item.done && !item.error) ? <div className="media-upload__actions">
          <Field label="关联范围"><select value={uploadScope} onChange={(event) => setUploadScope(event.target.value)}><option value="">整个商品（SPU）</option>{variants.map((variant) => <option key={variant.id} value={variant.id}>{variant.displayName} · {variant.sku}</option>)}</select></Field>
          <button className="button button--dark" type="button" disabled={uploadMutation.isPending} onClick={() => uploadMutation.mutate()}>{uploadMutation.isPending ? <><span className="spinner" />正在上传…</> : <><Icon name="upload" />上传 {uploads.filter((item) => !item.done && !item.error).length} 个文件</>}</button>
        </div> : null}
      </div> : null}
    </section>
  </div>;
}

function VariantForm({ item, defaultCurrency, busy, error, onSave, onCancel }: { item?: ProductVariant; defaultCurrency: string; busy: boolean; error: unknown; onSave: (payload: VariantPayload) => void; onCancel: () => void }) {
  const [attributes, setAttributes] = useState(() => parseAttributes(item?.attributesJson));
  const save = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const values = Object.fromEntries(attributes.filter((attribute) => attribute.key.trim()).map((attribute) => [attribute.key.trim(), attribute.value.trim()]));
    onSave({ sku: item ? undefined : String(data.get('sku') ?? '').trim(), displayName: String(data.get('displayName') ?? '').trim(), attributesJson: JSON.stringify(values), barcode: String(data.get('barcode') ?? '').trim() || null, price: Number(data.get('price')), currency: String(data.get('currency') ?? defaultCurrency), isActive: data.has('isActive') });
  };
  const update = (index: number, key: 'key' | 'value', value: string) => setAttributes((current) => current.map((attribute, itemIndex) => itemIndex === index ? { ...attribute, [key]: value } : attribute));
  return <form className="variant-editor" onSubmit={save}>
    <div className="form-row">{item ? <Field label="SKU 编码" hint="SKU 创建后不可修改"><input value={item.sku} disabled /></Field> : <Field label="SKU 编码"><input name="sku" required maxLength={80} autoFocus placeholder="例如 TSHIRT-BLK-M" /></Field>}<Field label="规格显示名"><input name="displayName" defaultValue={item?.displayName} required maxLength={200} placeholder="例如 黑色 / M" /></Field></div>
    <div className="form-row"><Field label="条码"><input name="barcode" defaultValue={item?.barcode} maxLength={80} /></Field><Field label="价格" hint="同一商品的全部 SKU 须使用相同币种"><div className="input-group"><select name="currency" defaultValue={item?.currency ?? defaultCurrency} aria-label="币种"><option value="CNY">CNY</option><option value="USD">USD</option><option value="EUR">EUR</option></select><input name="price" type="number" min="0" step="0.01" defaultValue={item?.price} required /></div></Field></div>
    <fieldset className="attribute-editor"><legend>规格属性</legend>
      {attributes.map((attribute, index) => <div key={index}>
        <input aria-label={`属性名 ${index + 1}`} value={attribute.key} onChange={(event) => update(index, 'key', event.target.value)} placeholder="属性名，如 颜色" list="variant-attribute-names" />
        <input aria-label={`属性值 ${index + 1}`} value={attribute.value} onChange={(event) => update(index, 'value', event.target.value)} placeholder="属性值，如 黑色" />
        <button type="button" aria-label="删除属性" onClick={() => setAttributes((current) => current.filter((_, itemIndex) => itemIndex !== index))}><Icon name="close" /></button>
      </div>)}
      <datalist id="variant-attribute-names"><option value="颜色" /><option value="尺码" /><option value="版本" /><option value="容量" /><option value="材质" /></datalist>
      <button type="button" onClick={() => setAttributes((current) => [...current, { key: '', value: '' }])}><Icon name="plus" />添加属性</button>
    </fieldset>
    <label className="switch-row"><input name="isActive" type="checkbox" defaultChecked={item?.isActive ?? true} /><span />SKU 可售</label>
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label={item ? '保存 SKU' : '添加 SKU'} onCancel={onCancel} />
  </form>;
}

function parseAttributes(json?: string) {
  if (!json) return [{ key: '', value: '' }];
  try {
    const entries = Object.entries(JSON.parse(json) as Record<string, unknown>).map(([key, value]) => ({ key, value: String(value) }));
    return entries.length ? entries : [{ key: '', value: '' }];
  } catch {
    return [{ key: '', value: '' }];
  }
}

export function VariantAttributes({ json }: { json: string }) {
  const attributes = parseAttributes(json).filter((item) => item.key);
  return attributes.length ? <span className="tag-list">{attributes.map((item) => <span key={item.key}>{item.key}: {item.value}</span>)}</span> : <small>无规格属性</small>;
}
