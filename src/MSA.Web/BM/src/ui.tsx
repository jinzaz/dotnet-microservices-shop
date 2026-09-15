import { useEffect, useRef, type FormEvent, type ReactNode, type SVGProps } from 'react';

export type IconName = 'overview' | 'order' | 'catalog' | 'inventory' | 'user' | 'payment' |
  'notification' | 'shield' | 'search' | 'menu' | 'logout' | 'plus' | 'refresh' |
  'arrow' | 'check' | 'alert' | 'activity' | 'key' | 'close';

const iconPaths: Record<IconName, ReactNode> = {
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
};

export function Icon({ name, ...props }: SVGProps<SVGSVGElement> & { name: IconName }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...props}>{iconPaths[name]}</svg>;
}

export function PageHeader({ eyebrow, title, description, actions }: { eyebrow: string; title: string; description: string; actions?: ReactNode }) {
  return <header className="page-header"><div><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p>{description}</p></div>{actions ? <div className="page-header__actions">{actions}</div> : null}</header>;
}

export function Status({ children }: { children: ReactNode }) {
  const value = String(children).toLowerCase();
  const tone = /active|paid|published|sent|delivered|success|enabled|allow|正常|启用/.test(value) ? 'good'
    : /fail|archive|disabled|deny|cancel|ended|异常|停用/.test(value) ? 'bad'
      : /pending|draft|pause|shipping|transit|处理中|草稿/.test(value) ? 'warn' : 'neutral';
  return <span className={`status status--${tone}`}><i />{children}</span>;
}

export function Loading({ label = '正在读取数据' }: { label?: string }) {
  return <div className="loading"><span /><p>{label}</p></div>;
}

export function Empty({ title, detail }: { title: string; detail: string }) {
  return <div className="empty"><Icon name="activity" /><strong>{title}</strong><p>{detail}</p></div>;
}

export function ErrorNotice({ error }: { error: unknown }) {
  return <p className="notice notice--error" role="alert"><Icon name="alert" />{error instanceof Error ? error.message : '请求未完成，请稍后重试。'}</p>;
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}{hint ? <small>{hint}</small> : null}</label>;
}

export function Modal({ open, title, description, wide = false, onClose, children }: { open: boolean; title: string; description?: string; wide?: boolean; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);
  return <dialog ref={ref} className={`modal${wide ? ' modal--wide' : ''}`} onCancel={onClose} onClose={onClose}>
    <div className="modal__head"><div><p className="eyebrow">OPERATION</p><h2>{title}</h2>{description ? <p>{description}</p> : null}</div><button className="icon-button" type="button" onClick={onClose} aria-label="关闭"><Icon name="close" /></button></div>
    {children}
  </dialog>;
}

export function SubmitBar({ busy, label = '确认提交', onCancel }: { busy: boolean; label?: string; onCancel: () => void }) {
  return <div className="submit-bar"><button className="button button--quiet" type="button" onClick={onCancel}>取消</button><button className="button button--dark" type="submit" disabled={busy}>{busy ? '正在提交…' : label}</button></div>;
}

export function formJson(event: FormEvent<HTMLFormElement>) {
  return Object.fromEntries(new FormData(event.currentTarget));
}

export function formatDate(value?: string | null) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export function formatMoney(value: number, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency }).format(value);
}
