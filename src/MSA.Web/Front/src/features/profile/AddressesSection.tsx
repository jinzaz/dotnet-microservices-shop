import { Icon } from '../../shared/ui/Icon';
import type { UserAddress } from './types';

type AddressesSectionProps = {
  addresses: UserAddress[];
  pendingAddressId: string | null;
  onCreate: () => void;
  onEdit: (address: UserAddress) => void;
  onDelete: (address: UserAddress) => void;
  onSetDefault: (address: UserAddress) => void;
};

export function AddressesSection({
  addresses,
  pendingAddressId,
  onCreate,
  onEdit,
  onDelete,
  onSetDefault,
}: AddressesSectionProps) {
  return (
    <section className="account-section addresses-section" id="addresses">
      <div className="account-section__heading">
        <h2>收货地址</h2>
        <button className="outline-action outline-action--accent" type="button" onClick={onCreate}>
          <Icon name="plus" />
          新增地址
        </button>
      </div>
      {addresses.length ? (
        <div className="address-list">
          {addresses.map((address) => {
            const pending = pendingAddressId === address.id;
            return (
              <article className="address-row" key={address.id} aria-busy={pending}>
                <div className="address-row__label">
                  {address.isDefault ? <span className="default-label">默认地址</span> : null}
                  <Icon name="home" />
                  <strong>{address.label}</strong>
                </div>
                <span>{address.recipientName}</span>
                <span>{address.phoneNumber}</span>
                <address>{address.province} {address.city} {address.district} {address.street}</address>
                <div className="address-row__actions">
                  <button type="button" onClick={() => onEdit(address)} disabled={pending}>编辑</button>
                  <button className="danger-link" type="button" onClick={() => onDelete(address)} disabled={pending}>删除</button>
                  {!address.isDefault ? (
                    <button type="button" onClick={() => onSetDefault(address)} disabled={pending}>设为默认</button>
                  ) : null}
                </div>
              </article>
            );
          })}
        </div>
      ) : (
        <div className="account-empty">
          <Icon name="map-pin" />
          <p>还没有收货地址，添加一个常用地址吧。</p>
          <button type="button" onClick={onCreate}>新增地址</button>
        </div>
      )}
    </section>
  );
}
