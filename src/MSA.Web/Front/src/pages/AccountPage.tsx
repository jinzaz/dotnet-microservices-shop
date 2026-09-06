import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { AccountSidebar, type AccountSection } from '../features/profile/AccountSidebar';
import { AddressDrawer } from '../features/profile/AddressDrawer';
import { AddressesSection } from '../features/profile/AddressesSection';
import { demoAccountSnapshot } from '../features/profile/demo-account';
import { PointsLedger } from '../features/profile/PointsLedger';
import { PointsOverview } from '../features/profile/PointsOverview';
import { ProfileEditorDialog } from '../features/profile/ProfileEditorDialog';
import { ProfileSection } from '../features/profile/ProfileSection';
import {
  createAddress,
  deleteAddress,
  getAccountSnapshot,
  setDefaultAddress,
  updateAddress,
  updateProfile,
} from '../features/profile/profile-api';
import type { AccountSnapshot, AddressInput, UserAddress } from '../features/profile/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';

type DrawerState = { mode: 'create' } | { mode: 'edit'; address: UserAddress } | null;

export default function AccountPage() {
  const demoMode = isDemoMode();
  const [demoSnapshot, setDemoSnapshot] = useState<AccountSnapshot>(demoAccountSnapshot);
  const [activeSection, setActiveSection] = useState<AccountSection>('overview');
  const [drawer, setDrawer] = useState<DrawerState>(null);
  const [profileEditorOpen, setProfileEditorOpen] = useState(false);
  const [pendingAddressId, setPendingAddressId] = useState<string | null>(null);
  const [actionError, setActionError] = useState('');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { logout } = useAuth();
  const accountQuery = useQuery({
    queryKey: ['identity', 'account-snapshot'],
    queryFn: getAccountSnapshot,
    enabled: !demoMode,
    staleTime: 30_000,
    retry: 1,
  });

  const snapshot = demoMode ? demoSnapshot : accountQuery.data;

  const refreshAccount = async () => {
    await queryClient.invalidateQueries({ queryKey: ['identity', 'account-snapshot'] });
  };

  const saveProfile = async (displayName: string, phoneNumber: string | null) => {
    if (demoMode) {
      setDemoSnapshot((current) => ({
        ...current,
        profile: { ...current.profile, displayName, phoneNumber },
      }));
      return;
    }
    await updateProfile(displayName, phoneNumber);
    await refreshAccount();
  };

  const saveAddress = async (input: AddressInput) => {
    setActionError('');
    const editingAddress = drawer?.mode === 'edit' ? drawer.address : null;

    if (demoMode) {
      const now = new Date().toISOString();
      setDemoSnapshot((current) => {
        const resetDefault = (address: UserAddress) => input.makeDefault ? { ...address, isDefault: false } : address;
        const addresses = editingAddress
          ? current.addresses.map((address) => address.id === editingAddress.id
              ? { ...address, ...input, postalCode: input.postalCode, isDefault: input.makeDefault || address.isDefault, updatedOnUtc: now }
              : resetDefault(address))
          : [
              ...current.addresses.map(resetDefault),
              {
                ...input,
                id: crypto.randomUUID(),
                postalCode: input.postalCode,
                isDefault: input.makeDefault || current.addresses.length === 0,
                createdOnUtc: now,
                updatedOnUtc: now,
              },
            ];
        return { ...current, addresses };
      });
      return;
    }

    if (editingAddress) {
      await updateAddress(editingAddress.id, input);
      if (input.makeDefault && !editingAddress.isDefault) await setDefaultAddress(editingAddress.id);
    } else {
      await createAddress(input);
    }
    await refreshAccount();
  };

  const makeDefault = async (address: UserAddress) => {
    setPendingAddressId(address.id);
    setActionError('');
    try {
      if (demoMode) {
        setDemoSnapshot((current) => ({
          ...current,
          addresses: current.addresses.map((item) => ({ ...item, isDefault: item.id === address.id })),
        }));
      } else {
        await setDefaultAddress(address.id);
        await refreshAccount();
      }
    } catch (error) {
      setActionError(getErrorMessage(error));
    } finally {
      setPendingAddressId(null);
    }
  };

  const removeAddress = async (address: UserAddress) => {
    if (!window.confirm(`确定删除“${address.label}”地址吗？`)) return;
    setPendingAddressId(address.id);
    setActionError('');
    try {
      if (demoMode) {
        setDemoSnapshot((current) => ({
          ...current,
          addresses: current.addresses.filter((item) => item.id !== address.id),
        }));
      } else {
        await deleteAddress(address.id);
        await refreshAccount();
      }
    } catch (error) {
      setActionError(getErrorMessage(error));
    } finally {
      setPendingAddressId(null);
    }
  };

  const handleLogout = () => {
    logout();
    queryClient.clear();
    navigate('/');
  };

  if (!demoMode && accountQuery.isPending) {
    return (
      <CommerceShell>
        <div className="page-state" role="status"><span className="page-spinner" />正在加载账户信息…</div>
      </CommerceShell>
    );
  }

  if (!snapshot) {
    return (
      <CommerceShell>
        <div className="page-state page-state--error">
          <h1>账户信息加载失败</h1>
          <p>{getErrorMessage(accountQuery.error)}</p>
          <Button type="button" onClick={() => void accountQuery.refetch()}>重新加载</Button>
        </div>
      </CommerceShell>
    );
  }

  const drawerAddress = drawer?.mode === 'edit' ? drawer.address : null;

  return (
    <CommerceShell>
      <div className="account-page" id="account-overview">
        <AccountSidebar
          profile={snapshot.profile}
          activeSection={activeSection}
          onSectionChange={setActiveSection}
          onLogout={handleLogout}
        />
        <div className="account-content">
          <div className="account-title-row">
            <div>
              <h1>我的账户</h1>
              {demoMode ? <p className="demo-notice">开发预览数据 · 所有编辑仅保留在当前页面</p> : null}
            </div>
          </div>
          <PointsOverview points={snapshot.points} />
          {actionError ? <p className="form-message form-message--error" role="alert">{actionError}</p> : null}
          <ProfileSection profile={snapshot.profile} onEdit={() => setProfileEditorOpen(true)} />
          <AddressesSection
            addresses={snapshot.addresses}
            pendingAddressId={pendingAddressId}
            onCreate={() => setDrawer({ mode: 'create' })}
            onEdit={(address) => setDrawer({ mode: 'edit', address })}
            onDelete={(address) => void removeAddress(address)}
            onSetDefault={(address) => void makeDefault(address)}
          />
          <PointsLedger entries={snapshot.pointEntries} />
        </div>
      </div>
      <ServicePromises />
      <ProfileEditorDialog
        open={profileEditorOpen}
        profile={snapshot.profile}
        onClose={() => setProfileEditorOpen(false)}
        onSave={saveProfile}
      />
      <AddressDrawer
        open={drawer !== null}
        address={drawerAddress}
        onClose={() => setDrawer(null)}
        onSave={saveAddress}
      />
    </CommerceShell>
  );
}
