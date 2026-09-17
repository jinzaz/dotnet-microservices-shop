import { useEffect, useRef, type ReactNode } from 'react';
import { Button } from './Button';
import { Icon } from './Icon';

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: 'primary' | 'danger';
  loading?: boolean;
  onConfirm: () => void;
  onClose: () => void;
};

export function ConfirmDialog({
  open,
  title,
  description,
  children,
  confirmLabel = '确认',
  cancelLabel = '再想想',
  tone = 'primary',
  loading = false,
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={dialogRef}
      className="form-dialog confirm-dialog"
      onCancel={(event) => {
        event.preventDefault();
        if (!loading) onClose();
      }}
    >
      {open ? (
        <form
          method="dialog"
          onSubmit={(event) => {
            event.preventDefault();
            onConfirm();
          }}
        >
          <div className="dialog-heading">
            <div>
              <h2>{title}</h2>
              {description ? <p>{description}</p> : null}
            </div>
            <button className="dialog-close" type="button" onClick={onClose} disabled={loading} aria-label="关闭">
              <Icon name="close" />
            </button>
          </div>
          {children}
          <div className="dialog-actions">
            <Button type="button" variant="secondary" onClick={onClose} disabled={loading}>{cancelLabel}</Button>
            <Button type="submit" variant={tone} loading={loading} autoFocus>{confirmLabel}</Button>
          </div>
        </form>
      ) : null}
    </dialog>
  );
}
