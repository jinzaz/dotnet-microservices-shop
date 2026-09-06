import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getErrorMessage } from '../../shared/api/api-error';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { Icon } from '../../shared/ui/Icon';
import type { UserProfile } from './types';

type ProfileEditorDialogProps = {
  open: boolean;
  profile: UserProfile;
  onClose: () => void;
  onSave: (displayName: string, phoneNumber: string | null) => Promise<void>;
};

export function ProfileEditorDialog({ open, profile, onClose, onSave }: ProfileEditorDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [displayName, setDisplayName] = useState(profile.displayName);
  const [phoneNumber, setPhoneNumber] = useState(profile.phoneNumber ?? '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      setDisplayName(profile.displayName);
      setPhoneNumber(profile.phoneNumber ?? '');
      setError('');
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open, profile]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!displayName.trim()) {
      setError('请输入姓名。');
      return;
    }

    setSaving(true);
    setError('');
    try {
      await onSave(displayName.trim(), phoneNumber.trim() || null);
      onClose();
    } catch (saveError) {
      setError(getErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  };

  return (
    <dialog className="form-dialog" ref={dialogRef} onCancel={onClose}>
      <form onSubmit={submit}>
        <div className="dialog-heading">
          <div><h2>编辑个人资料</h2><p>邮箱由登录账户管理，暂不可在此修改。</p></div>
          <button type="button" className="dialog-close" onClick={onClose} aria-label="关闭">
            <Icon name="close" />
          </button>
        </div>
        <FormField
          id="profile-name"
          label="姓名"
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          maxLength={100}
          required
        />
        <FormField id="profile-email" label="邮箱" value={profile.email} disabled />
        <FormField
          id="profile-phone"
          label="手机号"
          value={phoneNumber}
          onChange={(event) => setPhoneNumber(event.target.value)}
          maxLength={32}
          autoComplete="tel"
        />
        {error ? <p className="form-message form-message--error" role="alert">{error}</p> : null}
        <div className="dialog-actions">
          <Button type="button" variant="secondary" onClick={onClose}>取消</Button>
          <Button type="submit" loading={saving}>保存资料</Button>
        </div>
      </form>
    </dialog>
  );
}
