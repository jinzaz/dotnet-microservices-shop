import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError, getErrorMessage } from '../../shared/api/api-error';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { Icon } from '../../shared/ui/Icon';

type ChangePasswordDialogProps = {
  open: boolean;
  onClose: () => void;
  onSave: (currentPassword: string, newPassword: string) => Promise<void>;
};

type PasswordErrors = Partial<Record<'currentPassword' | 'newPassword' | 'confirmPassword' | 'form', string>>;

const uppercasePattern = /[A-Z]/;
const lowercasePattern = /[a-z]/;
const digitPattern = /\d/;
const symbolPattern = /[\p{P}\p{S}]/u;

function passwordErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const codes = Object.keys(error.problem?.errors ?? {});
    if (codes.includes('PasswordMismatch')) return '当前密码不正确，请重新输入。';
    if (codes.some((code) => code.startsWith('Password'))) return '新密码不符合密码安全要求。';
  }

  return getErrorMessage(error, '密码修改失败，请稍后重试。');
}

export function ChangePasswordDialog({ open, onClose, onSave }: ChangePasswordDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<PasswordErrors>({});

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setErrors({});
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const nextErrors: PasswordErrors = {};

    if (!currentPassword) nextErrors.currentPassword = '请输入当前密码。';
    if (
      newPassword.length < 10
      || !uppercasePattern.test(newPassword)
      || !lowercasePattern.test(newPassword)
      || !digitPattern.test(newPassword)
      || !symbolPattern.test(newPassword)
    ) {
      nextErrors.newPassword = '新密码需至少 10 位，并包含大小写字母、数字和符号。';
    } else if (newPassword === currentPassword) {
      nextErrors.newPassword = '新密码不能与当前密码相同。';
    }
    if (!confirmPassword) nextErrors.confirmPassword = '请再次输入新密码。';
    else if (confirmPassword !== newPassword) nextErrors.confirmPassword = '两次输入的新密码不一致。';

    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSaving(true);
    setErrors({});
    try {
      await onSave(currentPassword, newPassword);
      onClose();
    } catch (error) {
      setErrors({ form: passwordErrorMessage(error) });
    } finally {
      setSaving(false);
    }
  };

  return (
    <dialog className="form-dialog" ref={dialogRef} onCancel={onClose}>
      <form onSubmit={submit} noValidate>
        <div className="dialog-heading">
          <div><h2>修改登录密码</h2><p>修改后请使用新密码继续登录。</p></div>
          <button type="button" className="dialog-close" onClick={onClose} aria-label="关闭密码表单">
            <Icon name="close" />
          </button>
        </div>
        <FormField
          id="current-password"
          label="当前密码"
          type="password"
          value={currentPassword}
          onChange={(event) => setCurrentPassword(event.target.value)}
          autoComplete="current-password"
          required
          leading={<Icon name="lock" />}
          error={errors.currentPassword}
        />
        <FormField
          id="new-password"
          label="新密码"
          type="password"
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
          autoComplete="new-password"
          minLength={10}
          required
          leading={<Icon name="lock" />}
          aria-describedby="new-password-hint"
          error={errors.newPassword}
        />
        <p className="password-hint" id="new-password-hint">需包含大写字母、小写字母、数字和符号。</p>
        <FormField
          id="confirm-new-password"
          label="确认新密码"
          type="password"
          value={confirmPassword}
          onChange={(event) => setConfirmPassword(event.target.value)}
          autoComplete="new-password"
          minLength={10}
          required
          leading={<Icon name="lock" />}
          error={errors.confirmPassword}
        />
        {errors.form ? <p className="form-message form-message--error" role="alert">{errors.form}</p> : null}
        <div className="dialog-actions">
          <Button type="button" variant="secondary" onClick={onClose}>取消</Button>
          <Button type="submit" loading={saving}>确认修改</Button>
        </div>
      </form>
    </dialog>
  );
}
