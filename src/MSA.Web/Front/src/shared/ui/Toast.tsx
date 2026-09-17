import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { Icon } from './Icon';

type ToastTone = 'success' | 'error' | 'info';
type ToastItem = { id: number; message: string; tone: ToastTone; action?: { label: string; onClick: () => void } };
type ShowToast = (message: string, options?: { tone?: ToastTone; action?: ToastItem['action'] }) => void;

const ToastContext = createContext<ShowToast | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setItems((current) => current.filter((item) => item.id !== id)), []);
  const show = useCallback<ShowToast>((message, options) => {
    const id = nextId.current++;
    // Keep the stack short so rapid actions (quantity steppers) do not bury the page.
    setItems((current) => [...current.slice(-2), { id, message, tone: options?.tone ?? 'success', action: options?.action }]);
    window.setTimeout(() => dismiss(id), options?.action ? 6_000 : 3_200);
  }, [dismiss]);

  const value = useMemo(() => show, [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-stack" role="status" aria-live="polite">
        {items.map((item) => (
          <div key={item.id} className={`toast toast--${item.tone}`}>
            <Icon name={item.tone === 'error' ? 'alert-circle' : item.tone === 'info' ? 'help-circle' : 'circle-check'} />
            <span>{item.message}</span>
            {item.action ? (
              <button type="button" onClick={() => { item.action?.onClick(); dismiss(item.id); }}>{item.action.label}</button>
            ) : null}
            <button type="button" className="toast__close" aria-label="关闭提示" onClick={() => dismiss(item.id)}><Icon name="close" /></button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ShowToast {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used within ToastProvider.');
  return context;
}
