import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { api, query, type Paged } from './api';
import { Icon, useDebouncedValue } from './ui';

type IdentityUser = { id: string; email: string; displayName: string; isActive: boolean };
type ProductOption = { id: string; name: string; variants?: Array<{ id: string; sku: string; displayName: string; isActive: boolean }> };

export function parseIds(value: string) {
  return Array.from(new Set(value.split(/[\s,;，；]+/).map((item) => item.trim()).filter(Boolean)));
}

function Chips({ value, labels, onChange, clearable }: { value: string[]; labels: Record<string, string>; onChange: (ids: string[]) => void; clearable: boolean }) {
  if (!value.length) return null;
  return <div className="chip-list">
    {value.slice(0, 60).map((id) => <span key={id} className="chip" title={id}>{labels[id] ?? id}<button type="button" aria-label="移除" onClick={() => onChange(value.filter((item) => item !== id))}><Icon name="close" /></button></span>)}
    {value.length > 60 ? <span className="chip">另有 {value.length - 60} 项</span> : null}
    {clearable && value.length > 1 ? <button type="button" className="link-button" onClick={() => onChange([])}>清空</button> : null}
  </div>;
}

/** Search identity users by name or email; pasted ids stay available for lists exported elsewhere or when search is not permitted. */
export function UserPicker({ value, onChange, canSearch, single = false, initialLabels }: { value: string[]; onChange: (ids: string[]) => void; canSearch: boolean; single?: boolean; initialLabels?: Record<string, string> }) {
  const [search, setSearch] = useState('');
  const [labels, setLabels] = useState<Record<string, string>>(initialLabels ?? {});
  const [paste, setPaste] = useState('');
  const term = useDebouncedValue(search.trim(), 300);
  const results = useQuery({
    queryKey: ['identity', 'user-picker', term],
    queryFn: ({ signal }) => api<IdentityUser[]>(`/api/Auth/users/admin?${query({ search: term, skip: 0, take: 8 })}`, { signal }),
    enabled: canSearch && term.length >= 2,
    staleTime: 30_000,
  });
  const add = (user: IdentityUser) => {
    setLabels((current) => ({ ...current, [user.id]: `${user.displayName} · ${user.email}` }));
    onChange(single ? [user.id] : Array.from(new Set([...value, user.id])));
    setSearch('');
  };
  const addPasted = () => {
    const ids = parseIds(paste);
    if (!ids.length) return;
    onChange(single ? ids.slice(0, 1) : Array.from(new Set([...value, ...ids])));
    setPaste('');
  };

  return <div className="user-picker">
    {canSearch && (!single || !value.length) ? <div className="user-picker__search">
      <Icon name="search" />
      <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="输入姓名或邮箱搜索用户（至少 2 个字）" aria-label="搜索用户" />
      {results.isFetching ? <span className="spinner" /> : null}
    </div> : null}
    {term.length >= 2 && results.data ? <ul className="user-picker__results" role="listbox" aria-label="搜索结果">
      {results.data.length ? results.data.map((user) => {
        const picked = value.includes(user.id);
        return <li key={user.id}><button type="button" role="option" aria-selected={picked} disabled={picked || !user.isActive} onClick={() => add(user)}>
          <strong>{user.displayName}</strong><small>{user.email}{!user.isActive ? ' · 已停用' : picked ? ' · 已添加' : ''}</small>
        </button></li>;
      }) : <li className="muted-copy">没有匹配的用户</li>}
    </ul> : null}
    {!single || !value.length ? <details className="user-picker__paste" open={!canSearch}>
      <summary>{canSearch ? '粘贴用户 ID' : '没有用户搜索权限（identity.users.query），请粘贴用户 ID'}</summary>
      <div>
        <textarea rows={single ? 1 : 2} value={paste} onChange={(event) => setPaste(event.target.value)} className="mono" placeholder={single ? '用户 ID' : '换行 / 空格 / 逗号分隔'} aria-label="粘贴用户 ID" />
        <button type="button" className="button button--quiet" onClick={addPasted} disabled={!paste.trim()}>添加</button>
      </div>
    </details> : null}
    <Chips value={value} labels={labels} onChange={onChange} clearable={!single} />
  </div>;
}

/** Browse or search the catalog and tick SKUs; a whole product can be ticked at once. */
export function SkuPicker({ value, onChange, canSearch }: { value: string[]; onChange: (skus: string[]) => void; canSearch: boolean }) {
  const [search, setSearch] = useState('');
  const [paste, setPaste] = useState('');
  const term = useDebouncedValue(search.trim(), 300);
  const products = useQuery({
    queryKey: ['catalog', 'sku-picker', term],
    queryFn: ({ signal }) => api<Paged<ProductOption>>(`/api/catalog/admin/products?${query({ search: term, skip: 0, take: 20 })}`, { signal }),
    enabled: canSearch,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });
  const selected = new Set(value);
  const toggle = (skus: string[], on: boolean) => onChange(on ? Array.from(new Set([...value, ...skus])) : value.filter((sku) => !skus.includes(sku)));
  const rows = (products.data?.items ?? []).filter((product) => product.variants?.length);
  const labels = Object.fromEntries(rows.flatMap((product) => product.variants!.map((variant) => [variant.sku, `${product.name} · ${variant.displayName} · ${variant.sku}`])));

  if (!canSearch) return <div className="user-picker">
    <p className="muted-copy"><Icon name="alert" />没有商品查询权限（catalog.products.query），请直接输入 SKU。</p>
    <div className="user-picker__paste"><div>
      <textarea rows={2} value={paste} onChange={(event) => setPaste(event.target.value)} className="mono" placeholder="换行 / 空格 / 逗号分隔" aria-label="输入 SKU" />
      <button type="button" className="button button--quiet" disabled={!paste.trim()} onClick={() => { toggle(parseIds(paste).map((sku) => sku.toUpperCase()), true); setPaste(''); }}>添加</button>
    </div></div>
    <Chips value={value} labels={{}} onChange={onChange} clearable />
  </div>;

  return <div className="user-picker">
    <div className="user-picker__search">
      <Icon name="search" />
      <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="按商品名称或 SKU 搜索" aria-label="搜索商品" />
      {products.isFetching ? <span className="spinner" /> : null}
    </div>
    <div className="sku-picker">
      {products.isPending ? <p className="muted-copy">正在读取商品…</p> : rows.length ? rows.map((product) => {
        const skus = product.variants!.map((variant) => variant.sku);
        const all = skus.every((sku) => selected.has(sku));
        return <fieldset key={product.id}>
          <legend><label><input type="checkbox" checked={all} onChange={(event) => toggle(skus, event.target.checked)} /><strong>{product.name}</strong><small>{skus.length} 个 SKU</small></label></legend>
          {product.variants!.map((variant) => <label key={variant.id}>
            <input type="checkbox" checked={selected.has(variant.sku)} onChange={(event) => toggle([variant.sku], event.target.checked)} />
            <span>{variant.displayName}<small className="mono">{variant.sku}{variant.isActive ? '' : ' · 未启用'}</small></span>
          </label>)}
        </fieldset>;
      }) : <p className="muted-copy">{term ? '没有匹配的商品' : '商品目录为空'}</p>}
      {(products.data?.total ?? 0) > (products.data?.items.length ?? 0) ? <p className="muted-copy">仅显示前 {products.data!.items.length} 个商品，输入关键字缩小范围。</p> : null}
    </div>
    <Chips value={value} labels={labels} onChange={onChange} clearable />
  </div>;
}
