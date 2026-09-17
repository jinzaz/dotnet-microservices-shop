import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, query, type Paged } from './api';
import { useSession } from './session';
import { formatMoney, Icon, statusLabel, useDebouncedValue, type IconName } from './ui';

export type NavEntry = { path: string; label: string; icon: IconName; modules?: string[] };

type Result = { key: string; group: string; icon: IconName; title: string; detail?: string; to: string };
type OrderHit = { id: string; orderNumber: string; recipientName: string; amount: number; currency: string; fulfillmentStatus: string };
type ProductHit = { id: string; name: string; sku: string; lifecycleStatus: string };
type UserHit = { id: string; email: string; displayName: string };

export function CommandPalette({ open, onClose, nav }: { open: boolean; onClose: () => void; nav: NavEntry[] }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [term, setTerm] = useState('');
  const [active, setActive] = useState(0);
  const search = useDebouncedValue(term.trim(), 250);
  const searching = search.length >= 2;

  useEffect(() => {
    if (!open) return;
    setTerm('');
    setActive(0);
    window.setTimeout(() => inputRef.current?.focus(), 0);
  }, [open]);

  const orders = useQuery({
    queryKey: ['palette', 'orders', search],
    queryFn: ({ signal }) => api<OrderHit[]>(`/api/order/admin/orders?${query({ search, take: 5 })}`, { signal }),
    enabled: open && searching && can('order.fulfillment.query'),
  });
  const products = useQuery({
    queryKey: ['palette', 'products', search],
    queryFn: ({ signal }) => api<Paged<ProductHit>>(`/api/catalog/admin/products?${query({ search, take: 5 })}`, { signal }),
    enabled: open && searching && can('catalog.products.query'),
  });
  const users = useQuery({
    queryKey: ['palette', 'users', search],
    queryFn: ({ signal }) => api<UserHit[]>(`/api/Auth/users/admin?${query({ search, take: 5 })}`, { signal }),
    enabled: open && searching && can('identity.users.query'),
  });

  const results = useMemo<Result[]>(() => {
    const lowered = term.trim().toLowerCase();
    const pages = nav
      .filter((item) => !lowered || item.label.toLowerCase().includes(lowered) || item.path.includes(lowered))
      .map((item) => ({ key: `page-${item.path}`, group: '页面', icon: item.icon, title: item.label, detail: item.path, to: item.path }));
    return [
      ...pages,
      ...(orders.data ?? []).map((item) => ({ key: `order-${item.id}`, group: '订单', icon: 'order' as const, title: item.orderNumber, detail: `${item.recipientName} · ${formatMoney(item.amount, item.currency)} · ${statusLabel(item.fulfillmentStatus)}`, to: `/orders?order=${item.id}` })),
      ...(products.data?.items ?? []).map((item) => ({ key: `product-${item.id}`, group: '商品', icon: 'catalog' as const, title: item.name, detail: `${item.sku} · ${statusLabel(item.lifecycleStatus)}`, to: `/catalog?product=${item.id}` })),
      ...(users.data ?? []).map((item) => ({ key: `user-${item.id}`, group: '用户', icon: 'user' as const, title: item.displayName || item.email, detail: item.email, to: `/customers?user=${encodeURIComponent(item.id)}` })),
    ];
  }, [nav, orders.data, products.data, term, users.data]);

  useEffect(() => setActive(0), [results.length]);
  const busy = searching && (orders.isFetching || products.isFetching || users.isFetching);

  const go = (result: Result | undefined) => {
    if (!result) return;
    navigate(result.to);
    onClose();
  };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') { event.preventDefault(); setActive((value) => Math.min(value + 1, results.length - 1)); }
    if (event.key === 'ArrowUp') { event.preventDefault(); setActive((value) => Math.max(value - 1, 0)); }
    if (event.key === 'Enter') { event.preventDefault(); go(results[active]); }
    if (event.key === 'Escape') onClose();
  };

  if (!open) return null;
  let lastGroup = '';
  return <div className="palette-scrim" onMouseDown={onClose}>
    <div className="palette" role="dialog" aria-modal="true" aria-label="全局搜索" onMouseDown={(event) => event.stopPropagation()}>
      <label className="palette__input"><Icon name="search" />
        <input ref={inputRef} value={term} onChange={(event) => setTerm(event.target.value)} onKeyDown={onKeyDown}
          placeholder="搜索页面、订单号 / 手机号 / 运单号、商品名称 / SKU、用户邮箱…" aria-label="搜索" role="combobox" aria-expanded="true" aria-controls="palette-results" aria-activedescendant={results[active]?.key} />
        {busy ? <span className="spinner" /> : <kbd>ESC</kbd>}
      </label>
      <ul id="palette-results" className="palette__results" role="listbox">
        {results.map((result, index) => {
          const header = result.group !== lastGroup ? result.group : null;
          lastGroup = result.group;
          return <li key={result.key} role="presentation">
            {header ? <p className="palette__group">{header}</p> : null}
            <button id={result.key} type="button" role="option" aria-selected={index === active} onMouseEnter={() => setActive(index)} onClick={() => go(result)}>
              <Icon name={result.icon} /><span><strong>{result.title}</strong>{result.detail ? <small>{result.detail}</small> : null}</span><Icon name="arrow" />
            </button>
          </li>;
        })}
        {!results.length ? <li className="palette__empty">{searching && !busy ? '没有匹配结果，换个关键词试试。' : '输入至少 2 个字符开始搜索业务数据。'}</li> : null}
      </ul>
      <footer><span><kbd>↑</kbd><kbd>↓</kbd> 选择</span><span><kbd>Enter</kbd> 打开</span><span>仅搜索当前身份有查询权限的模块</span></footer>
    </div>
  </div>;
}
