import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getErrorMessage } from '../../shared/api/api-error';
import { Button } from '../../shared/ui/Button';
import { CountryCodeSelect, splitE164, toE164 } from '../../shared/ui/CountryCodeSelect';
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
  const [countryCode, setCountryCode] = useState(() => splitE164(profile.phoneNumber)[0]);
  const [phoneNumber, setPhoneNumber] = useState(() => splitE164(profile.phoneNumber)[1]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      setDisplayName(profile.displayName);
      const [code, national] = splitE164(profile.phoneNumber);
      setCountryCode(code);
      setPhoneNumber(national);
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
    const normalizedPhoneNumber = phoneNumber.trim() ? toE164(countryCode, phoneNumber) : '';
    if (normalizedPhoneNumber === null) {
      setError('请输入有效的手机号。');
      return;
    }

    setSaving(true);
    setError('');
    try {
      await onSave(displayName.trim(), normalizedPhoneNumber || null);
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
          <div><h2>编辑个人资料</h2><p>手机号修改后会清除验证状态。</p></div>
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
        <FormField id="profile-email" label="邮箱" value={profile.email ?? ''} placeholder="未绑定" disabled />
        <FormField
          id="profile-phone"
          label="手机号"
          type="tel"
          value={phoneNumber}
          onChange={(event) => setPhoneNumber(event.target.value)}
          placeholder="13800138000"
          maxLength={16}
          autoComplete="tel-national"
          leading={<CountryCodeSelect value={countryCode} onChange={setCountryCode} disabled={saving} />}
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
