import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getErrorMessage } from '../../shared/api/api-error';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { Icon } from '../../shared/ui/Icon';
import type { AddressInput, UserAddress } from './types';

const emptyAddress: AddressInput = {
  label: '家',
  recipientName: '',
  phoneNumber: '',
  countryCode: 'CN',
  province: '',
  city: '',
  district: '',
  street: '',
  postalCode: '',
  makeDefault: false,
};

function toInput(address: UserAddress | null): AddressInput {
  if (!address) return { ...emptyAddress };
  return {
    label: address.label,
    recipientName: address.recipientName,
    phoneNumber: address.phoneNumber,
    countryCode: address.countryCode,
    province: address.province,
    city: address.city,
    district: address.district,
    street: address.street,
    postalCode: address.postalCode,
    makeDefault: address.isDefault,
  };
}

type AddressDrawerProps = {
  open: boolean;
  address: UserAddress | null;
  onClose: () => void;
  onSave: (input: AddressInput) => Promise<void>;
};

export function AddressDrawer({ open, address, onClose, onSave }: AddressDrawerProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [value, setValue] = useState<AddressInput>(() => toInput(address));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open) {
      setValue(toInput(address));
      setError('');
      if (!dialog.open) dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [address, open]);

  const setField = <Key extends keyof AddressInput>(key: Key, fieldValue: AddressInput[Key]) => {
    setValue((current) => ({ ...current, [key]: fieldValue }));
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    setError('');
    try {
      await onSave({
        ...value,
        label: value.label.trim(),
        recipientName: value.recipientName.trim(),
        phoneNumber: value.phoneNumber.trim(),
        countryCode: value.countryCode.trim().toLocaleUpperCase(),
        province: value.province.trim(),
        city: value.city.trim(),
        district: value.district.trim(),
        street: value.street.trim(),
        postalCode: value.postalCode?.trim() || null,
      });
      onClose();
    } catch (saveError) {
      setError(getErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  };

  return (
    <dialog className="address-drawer" ref={dialogRef} onCancel={onClose}>
      <form onSubmit={submit}>
        <div className="dialog-heading">
          <div>
            <h2>{address ? '编辑收货地址' : '新增收货地址'}</h2>
            <p>请填写真实有效的配送信息。</p>
          </div>
          <button type="button" className="dialog-close" onClick={onClose} aria-label="关闭地址表单">
            <Icon name="close" />
          </button>
        </div>

        <FormField id="address-label" label="地址标签" value={value.label} onChange={(event) => setField('label', event.target.value)} maxLength={50} required />
        <FormField id="address-recipient" label="收货人" value={value.recipientName} onChange={(event) => setField('recipientName', event.target.value)} maxLength={100} autoComplete="name" required />
        <FormField id="address-phone" label="手机号" value={value.phoneNumber} onChange={(event) => setField('phoneNumber', event.target.value)} minLength={6} maxLength={32} autoComplete="tel" required />
        <div className="address-location-grid">
          <FormField id="address-province" label="省" value={value.province} onChange={(event) => setField('province', event.target.value)} maxLength={100} required />
          <FormField id="address-city" label="市" value={value.city} onChange={(event) => setField('city', event.target.value)} maxLength={100} required />
          <FormField id="address-district" label="区" value={value.district} onChange={(event) => setField('district', event.target.value)} maxLength={100} required />
        </div>
        <FormField id="address-street" label="详细地址" value={value.street} onChange={(event) => setField('street', event.target.value)} maxLength={300} autoComplete="street-address" required />
        <div className="address-location-grid address-location-grid--postal">
          <FormField id="address-postal" label="邮编" value={value.postalCode ?? ''} onChange={(event) => setField('postalCode', event.target.value)} maxLength={20} autoComplete="postal-code" />
          <FormField id="address-country" label="国家代码" value={value.countryCode} onChange={(event) => setField('countryCode', event.target.value)} minLength={2} maxLength={2} required />
        </div>
        <label className="checkbox-row">
          <input type="checkbox" checked={value.makeDefault} onChange={(event) => setField('makeDefault', event.target.checked)} />
          <span><Icon name="check" /></span>
          设为默认地址
        </label>
        {error ? <p className="form-message form-message--error" role="alert">{error}</p> : null}
        <div className="dialog-actions">
          <Button type="button" variant="secondary" onClick={onClose}>取消</Button>
          <Button type="submit" loading={saving}>保存地址</Button>
        </div>
      </form>
    </dialog>
  );
}
