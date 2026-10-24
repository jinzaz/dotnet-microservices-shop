import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type DragEvent, type FormEvent, type ReactNode, type SVGProps } from 'react';
import { useSearchParams } from 'react-router-dom';

export type IconName = 'overview' | 'order' | 'catalog' | 'inventory' | 'user' | 'payment' |
  'notification' | 'shield' | 'search' | 'menu' | 'logout' | 'plus' | 'refresh' |
  'arrow' | 'check' | 'alert' | 'activity' | 'key' | 'close' | 'copy' | 'upload' | 'chevron' | 'truck' | 'image' | 'download' | 'ticket' | 'link';

const iconPaths: Record<IconName, ReactNode> = {
  download: <><path d="M12 4v11m0 0-4-4m4 4 4-4" /><path d="M5 19h14" /></>,
  ticket: <><path d="M4 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-2a2 2 0 0 0 0-4V7Z" /><path d="M14 5v12" strokeDasharray="2 2" /></>,
  overview: <><path d="M4 13h6V4H4v9Zm10 7h6V11h-6v9ZM4 20h6v-3H4v3Zm10-13h6V4h-6v3Z" /></>,
  order: <><path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3Z" /><path d="M9 8h6M9 12h6" /></>,
  catalog: <><path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z" /><path d="m4.5 7.7 7.5 4.4 7.5-4.4M12 12v9" /></>,
  inventory: <><path d="M4 8h16v12H4zM3 4h18v4H3z" /><path d="M9 12h6" /></>,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4.5 21c.7-4.3 3.2-6.5 7.5-6.5s6.8 2.2 7.5 6.5" /></>,
  payment: <><rect x="3" y="5" width="18" height="14" rx="2" /><path d="M3 10h18M7 15h3" /></>,
  notification: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4" /></>,
  shield: <><path d="M12 2 4.5 5v6.2c0 4.8 2.9 8.8 7.5 10.8 4.6-2 7.5-6 7.5-10.8V5L12 2Z" /><path d="m8.5 12 2.2 2.2 4.8-5" /></>,
  search: <><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></>,
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  logout: <><path d="M10 4H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5" /><path d="m15 8 4 4-4 4M9 12h10" /></>,
  plus: <path d="M12 5v14M5 12h14" />,
  refresh: <><path d="M20 7V3l-2.2 2.2A9 9 0 1 0 21 12" /><path d="M20 3h-5" /></>,
  arrow: <><path d="M5 12h14" /><path d="m14 7 5 5-5 5" /></>,
  check: <path d="m5 12 4 4L19 6" />,
  alert: <><path d="m12 3 10 18H2L12 3Z" /><path d="M12 9v5M12 17h.01" /></>,
  activity: <path d="M3 12h4l2.2-6 4.1 12 2.1-6H21" />,
  key: <><circle cx="8" cy="15" r="4" /><path d="m11 12 9-9M16 7l2 2M14 9l2 2" /></>,
  close: <path d="m5 5 14 14M19 5 5 19" />,
  link: <><path d="M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1" /><path d="M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1" /></>,
  copy: <><rect x="8" y="8" width="12" height="12" rx="2" /><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2" /></>,
  upload: <><path d="M12 16V4M7 9l5-5 5 5" /><path d="M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" /></>,
  chevron: <path d="m9 6 6 6-6 6" />,
  truck: <><path d="M3 6h11v10H3zM14 10h4l3 3v3h-7" /><circle cx="7" cy="18" r="2" /><circle cx="17" cy="18" r="2" /></>,
  image: <><rect x="3" y="4" width="18" height="16" rx="2" /><circle cx="9" cy="10" r="2" /><path d="m21 16-5-5-9 9" /></>,
};

export function Icon({ name, ...props }: SVGProps<SVGSVGElement> & { name: IconName }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...props}>{iconPaths[name]}</svg>;
}

export function PageHeader({ eyebrow, title, description, actions }: { eyebrow: string; title: string; description: string; actions?: ReactNode }) {
  return <header className="page-header"><div><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p>{description}</p></div>{actions ? <div className="page-header__actions">{actions}</div> : null}</header>;
}

const statusLabels: Record<string, string> = {
  active: '启用', draft: '草稿', published: '已发布', unpublished: '已下架', archived: '已归档', paused: '已暂停', ended: '已结束',
  scheduled: '待开始', disabled: '已停用', expired: '已过期', open: '开放', closed: '已关闭', soldout: '已售罄',
  pendingpayment: '待支付', awaitingshipment: '待发货', shipped: '已发货', intransit: '运输中', delivered: '已送达', cancelled: '已取消', returnedtosender: '拒收退回',
  submitted: '已提交', awaitingpayment: '待支付', confirmed: '已确认',
  pending: '处理中', succeeded: '支付成功', failed: '失败', refunding: '退款中', refunded: '已退款', cancelling: '取消中',
  sending: '发送中', sent: '已送达', allow: '允许', deny: '拒绝',
  running: '进行中', compensating: '补偿中', compensated: '已补偿',
  requested: '待审核', approved: '待补寄', rejected: '已拒绝', awaitingreturn: '待寄回', returnintransit: '退货在途',
  returnreceived: '已收到退货', refundfailed: '退款失败', replacementshipped: '已发出新品', completed: '已完成',
  refundonly: '仅退款', returnandrefund: '退货退款', exchange: '换货', reship: '补寄',
};

export function statusLabel(value: string) {
  return statusLabels[value.toLowerCase()] ?? value;
}

export function Status({ children }: { children: ReactNode }) {
  const raw = String(children);
  const value = raw.toLowerCase();
  const tone = /active|paid|published|sent|delivered|succeed|success|confirmed|completed|enabled|allow|open|正常|启用/.test(value) ? 'good'
    : /fail|archive|disabled|deny|cancel|reject|ended|expired|closed|soldout|异常|停用/.test(value) ? 'bad'
      : /pending|awaiting|requested|returnreceived|draft|pause|shipp|transit|refund|scheduled|submitted|sending|处理中|草稿/.test(value) ? 'warn' : 'neutral';
  return <span className={`status status--${tone}`} title={raw}><i />{typeof children === 'string' ? statusLabel(children) : children}</span>;
}

export function Loading({ label = '正在读取数据' }: { label?: string }) {
  return <div className="loading"><span /><p>{label}</p></div>;
}

export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return <div className="skeleton-rows" aria-busy="true" aria-label="正在加载">{Array.from({ length: rows }, (_, index) => <span key={index} />)}</div>;
}

export function Empty({ title, detail, action }: { title: string; detail: string; action?: ReactNode }) {
  return <div className="empty"><Icon name="activity" /><strong>{title}</strong><p>{detail}</p>{action ? <div className="empty__action">{action}</div> : null}</div>;
}

export function ErrorNotice({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return <p className="notice notice--error" role="alert"><Icon name="alert" /><span>{error instanceof Error ? error.message : '请求未完成，请稍后重试。'}</span>{onRetry ? <button className="notice__action" type="button" onClick={onRetry}>重试</button> : null}</p>;
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}{hint ? <small>{hint}</small> : null}</label>;
}

export function Modal({ open, title, description, wide = false, onClose, children }: { open: boolean; title: string; description?: string; wide?: boolean; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  // Content mounts only after showModal(): React's autoFocus fails on elements inside a closed dialog.
  const [shown, setShown] = useState(false);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
    setShown(open);
  }, [open]);
  return <dialog ref={ref} className={`modal${wide ? ' modal--wide' : ''}`} onCancel={onClose} onClose={onClose} onClick={(event) => { if (event.target === ref.current) onClose(); }}>
    <div className="modal__head"><div><p className="eyebrow">OPERATION</p><h2>{title}</h2>{description ? <p>{description}</p> : null}</div><button className="icon-button" type="button" onClick={onClose} aria-label="关闭"><Icon name="close" /></button></div>
    {open && shown ? children : null}
  </dialog>;
}

export function SubmitBar({ busy, label = '确认提交', onCancel }: { busy: boolean; label?: string; onCancel: () => void }) {
  return <div className="submit-bar"><button className="button button--quiet" type="button" onClick={onCancel}>取消</button><button className="button button--dark" type="submit" disabled={busy}>{busy ? <><span className="spinner" />正在提交…</> : label}</button></div>;
}

export function formJson(event: FormEvent<HTMLFormElement>) {
  return Object.fromEntries(new FormData(event.currentTarget));
}

export function formatDate(value?: string | null) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export function formatRelative(value?: string | null) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return value;
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const units: Array<[Intl.RelativeTimeFormatUnit, number]> = [['day', 86_400], ['hour', 3_600], ['minute', 60]];
  const format = new Intl.RelativeTimeFormat('zh-CN', { numeric: 'auto' });
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit);
  }
  return '刚刚';
}

export function formatMoney(value: number, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency }).format(value);
}

export function asLocalInput(date = new Date(Date.now() + 3_600_000)) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

/* ---------- Toasts ---------- */

type Toast = { id: number; tone: 'success' | 'error' | 'info'; message: string };
type ToastApi = { success: (message: string) => void; error: (error: unknown) => void; info: (message: string) => void };
const ToastContext = createContext<ToastApi | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(0);
  const push = useCallback((tone: Toast['tone'], message: string) => {
    const id = ++nextId.current;
    setToasts((current) => [...current.slice(-3), { id, tone, message }]);
    window.setTimeout(() => setToasts((current) => current.filter((toast) => toast.id !== id)), tone === 'error' ? 6_000 : 3_200);
  }, []);
  const value = useMemo<ToastApi>(() => ({
    success: (message) => push('success', message),
    info: (message) => push('info', message),
    error: (error) => push('error', error instanceof Error ? error.message : typeof error === 'string' ? error : '操作未完成，请稍后重试。'),
  }), [push]);
  return <ToastContext.Provider value={value}>
    {children}
    <div className="toast-stack" role="status" aria-live="polite">{toasts.map((toast) => <div key={toast.id} className={`toast toast--${toast.tone}`}>
      <Icon name={toast.tone === 'error' ? 'alert' : 'check'} /><span>{toast.message}</span>
      <button type="button" aria-label="关闭提示" onClick={() => setToasts((current) => current.filter((item) => item.id !== toast.id))}><Icon name="close" /></button>
    </div>)}</div>
  </ToastContext.Provider>;
}

export function useToast() {
  const value = useContext(ToastContext);
  if (!value) throw new Error('useToast must be used within ToastProvider.');
  return value;
}

/* ---------- Confirm / reason dialog ---------- */

type ConfirmOptions = {
  title: string;
  detail?: string;
  confirmLabel?: string;
  tone?: 'danger' | 'default';
  /** Collects a text value (reason, new name…) together with the confirmation. */
  input?: { label: string; placeholder?: string; defaultValue?: string; required?: boolean; maxLength?: number; multiline?: boolean };
};
type ConfirmResult = { value: string } | null;
type PendingConfirm = ConfirmOptions & { resolve: (result: ConfirmResult) => void };
const ConfirmContext = createContext<((options: ConfirmOptions) => Promise<ConfirmResult>) | null>(null);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirm | null>(null);
  const confirm = useCallback((options: ConfirmOptions) => new Promise<ConfirmResult>((resolve) => setPending({ ...options, resolve })), []);
  const close = (result: ConfirmResult) => {
    pending?.resolve(result);
    setPending(null);
  };
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    close({ value: String(new FormData(event.currentTarget).get('value') ?? '').trim() });
  };
  const input = pending?.input;
  return <ConfirmContext.Provider value={confirm}>
    {children}
    <Modal open={pending !== null} title={pending?.title ?? ''} description={pending?.detail} onClose={() => close(null)}>
      <form className="modal-form confirm-form" onSubmit={submit}>
        {input ? <Field label={input.label}>{input.multiline
          ? <textarea name="value" rows={3} defaultValue={input.defaultValue} placeholder={input.placeholder} required={input.required} maxLength={input.maxLength} autoFocus />
          : <input name="value" defaultValue={input.defaultValue} placeholder={input.placeholder} required={input.required} maxLength={input.maxLength} autoFocus />}</Field> : null}
        <div className="submit-bar">
          <button className="button button--quiet" type="button" onClick={() => close(null)}>取消</button>
          <button className={`button ${pending?.tone === 'danger' ? 'button--danger' : 'button--dark'}`} type="submit" autoFocus={!input}>{pending?.confirmLabel ?? '确认'}</button>
        </div>
      </form>
    </Modal>
  </ConfirmContext.Provider>;
}

export function useConfirm() {
  const value = useContext(ConfirmContext);
  if (!value) throw new Error('useConfirm must be used within ConfirmProvider.');
  return value;
}

/* ---------- Lists ---------- */

export function Pagination({ page, pageSize, total, hasMore, onChange, busy }: { page: number; pageSize: number; total?: number; hasMore?: boolean; onChange: (page: number) => void; busy?: boolean }) {
  const pages = total === undefined ? undefined : Math.max(1, Math.ceil(total / pageSize));
  const canNext = pages === undefined ? Boolean(hasMore) : page < pages;
  if (page === 1 && !canNext) return null;
  const from = (page - 1) * pageSize + 1;
  return <nav className="pagination" aria-label="分页">
    <span>{total === undefined ? `第 ${page} 页` : `${from}–${Math.min(page * pageSize, total)} / 共 ${total} 条`}{busy ? <span className="spinner" /> : null}</span>
    <div>
      <button type="button" onClick={() => onChange(page - 1)} disabled={page <= 1}>上一页</button>
      {pages !== undefined ? <strong>{page} / {pages}</strong> : null}
      <button type="button" onClick={() => onChange(page + 1)} disabled={!canNext}>下一页</button>
    </div>
  </nav>;
}

export function useDebouncedValue<T>(value: T, delay = 300) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

export function SearchInput({ value, onChange, placeholder, label }: { value: string; onChange: (value: string) => void; placeholder: string; label?: string }) {
  const [draft, setDraft] = useState(value);
  const debounced = useDebouncedValue(draft);
  const latest = useRef({ value, onChange });
  latest.current = { value, onChange };
  useEffect(() => setDraft(value), [value]);
  // Emit only when the debounced draft itself changes; an external reset must not replay a stale draft.
  useEffect(() => { if (debounced !== latest.current.value) latest.current.onChange(debounced); }, [debounced]);
  return <label className="search-input"><Icon name="search" /><input type="search" value={draft} onChange={(event) => setDraft(event.target.value)} placeholder={placeholder} aria-label={label ?? placeholder} />{draft ? <button type="button" aria-label="清空搜索" onClick={() => setDraft('')}><Icon name="close" /></button> : null}</label>;
}

export type Chip = { value: string; label: string; count?: number };

export function ChipFilter({ chips, value, onChange, label }: { chips: Chip[]; value: string; onChange: (value: string) => void; label: string }) {
  return <div className="chip-filter" role="radiogroup" aria-label={label}>{chips.map((chip) => <button key={chip.value} type="button" role="radio" aria-checked={chip.value === value} onClick={() => onChange(chip.value)}>
    {chip.label}{chip.count !== undefined ? <b>{chip.count.toLocaleString('zh-CN')}</b> : null}
  </button>)}</div>;
}

export function CopyButton({ value, label = '复制' }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1_400);
    } catch {
      // Clipboard access can be blocked by the browser; the value stays selectable.
    }
  };
  return <button className={`copy-button${copied ? ' is-copied' : ''}`} type="button" onClick={(event) => { event.stopPropagation(); void copy(); }} aria-label={`${label} ${value}`} title={copied ? '已复制' : label}><Icon name={copied ? 'check' : 'copy'} /></button>;
}

export function Sparkline({ values, label }: { values: number[]; label: string }) {
  const id = useId();
  if (values.length < 2) return null;
  const max = Math.max(...values, 1);
  const points = values.map((value, index) => [index / (values.length - 1) * 100, 34 - value / max * 30] as const);
  const line = points.map(([x, y]) => `${x},${y}`).join(' ');
  return <svg className="sparkline" viewBox="0 0 100 36" preserveAspectRatio="none" role="img" aria-label={label}>
    <defs><linearGradient id={id} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor="currentColor" stopOpacity=".24" /><stop offset="1" stopColor="currentColor" stopOpacity="0" /></linearGradient></defs>
    <polygon points={`0,36 ${line} 100,36`} fill={`url(#${id})`} />
    <polyline points={line} fill="none" stroke="currentColor" strokeWidth="2" vectorEffect="non-scaling-stroke" />
  </svg>;
}

/* ---------- Upload ---------- */

export type UploadItem = { id: string; file: File; progress: number; error?: string; done?: boolean };

export function Dropzone({ accept, multiple, disabled, hint, onFiles }: { accept: string; multiple?: boolean; disabled?: boolean; hint: string; onFiles: (files: File[]) => void }) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const accepted = accept.split(',');
  const take = (list: FileList | null) => {
    const files = Array.from(list ?? []).filter((file) => accepted.includes(file.type) || accepted.some((type) => type.endsWith('/*') && file.type.startsWith(type.slice(0, -1))));
    if (files.length) onFiles(multiple ? files : files.slice(0, 1));
  };
  const drop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    if (!disabled) take(event.dataTransfer.files);
  };
  return <div className={`dropzone${dragging ? ' is-dragging' : ''}${disabled ? ' is-disabled' : ''}`} role="button" tabIndex={disabled ? -1 : 0}
    onClick={() => !disabled && inputRef.current?.click()}
    onKeyDown={(event) => { if ((event.key === 'Enter' || event.key === ' ') && !disabled) { event.preventDefault(); inputRef.current?.click(); } }}
    onDragOver={(event) => { event.preventDefault(); if (!disabled) setDragging(true); }} onDragLeave={() => setDragging(false)} onDrop={drop}>
    <Icon name="upload" /><strong>拖拽文件到这里，或点击选择</strong><small>{hint}</small>
    <input ref={inputRef} type="file" accept={accept} multiple={multiple} hidden onChange={(event) => { take(event.target.files); event.target.value = ''; }} />
  </div>;
}

export function UploadList({ items, onRemove }: { items: UploadItem[]; onRemove?: (id: string) => void }) {
  if (!items.length) return null;
  return <ul className="upload-list">{items.map((item) => <UploadRow key={item.id} item={item} onRemove={onRemove} />)}</ul>;
}

function UploadRow({ item, onRemove }: { item: UploadItem; onRemove?: (id: string) => void }) {
  const preview = useMemo(() => URL.createObjectURL(item.file), [item.file]);
  useEffect(() => () => URL.revokeObjectURL(preview), [preview]);
  return <li className={item.error ? 'is-error' : item.done ? 'is-done' : ''}>
    {item.file.type.startsWith('image/') ? <img src={preview} alt="" /> : <video src={preview} muted />}
    <div><strong>{item.file.name}</strong><small>{item.error ?? (item.done ? '已上传并校验' : item.progress > 0 ? `上传中 ${Math.round(item.progress * 100)}%` : `${(item.file.size / 1024 / 1024).toFixed(1)} MB · 待上传`)}</small><span className="progress"><i style={{ width: `${Math.round((item.done ? 1 : item.progress) * 100)}%` }} /></span></div>
    {onRemove && item.progress === 0 && !item.done ? <button type="button" aria-label={`移除 ${item.file.name}`} onClick={() => onRemove(item.id)}><Icon name="close" /></button> : null}
  </li>;
}

/* ---------- URL state ---------- */

/** Keeps list context (tab, filters, page) in the URL so refreshes, back navigation and shared links preserve it. */
export function useUrlState(key: string, fallback = '', { resetPage = true }: { resetPage?: boolean } = {}): [string, (value: string) => void] {
  const [params, setParams] = useSearchParams();
  const value = params.get(key) ?? fallback;
  const set = useCallback((next: string) => {
    // Read the live URL so several setters called in one handler compose instead of overwriting each other.
    const updated = new URLSearchParams(window.location.search);
    if (next === fallback || next === '') updated.delete(key); else updated.set(key, next);
    // Changing a filter invalidates the current page; opening a detail view must not.
    if (resetPage && key !== 'page') updated.delete('page');
    setParams(updated, { replace: true });
  }, [fallback, key, resetPage, setParams]);
  return [value, set];
}

export function usePage(): [number, (page: number) => void] {
  const [value, setValue] = useUrlState('page', '1');
  const page = Math.max(1, Number.parseInt(value, 10) || 1);
  return [page, useCallback((next: number) => setValue(String(Math.max(1, next))), [setValue])];
}
