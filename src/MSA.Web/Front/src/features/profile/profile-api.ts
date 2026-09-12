import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type {
  AccountSnapshot,
  AddressInput,
  LoyaltyPointAccount,
  LoyaltyPointEntry,
  UserAddress,
  UserProfile,
} from './types';

const identityUserBase = `${serviceRoutes.identity}/api/users/me`;
const userBase = `${serviceRoutes.user}/api/user/me`;

export function getProfile(): Promise<UserProfile> {
  return apiRequest<UserProfile>(identityUserBase);
}

export function listAddresses(): Promise<UserAddress[]> {
  return apiRequest<UserAddress[]>(`${userBase}/addresses`);
}

export function getPoints(): Promise<LoyaltyPointAccount> {
  return apiRequest<LoyaltyPointAccount>(`${userBase}/points`);
}

export async function getAccountSnapshot(): Promise<AccountSnapshot> {
  const [profile, addresses, points, pointEntries] = await Promise.all([
    getProfile(),
    listAddresses(),
    getPoints(),
    apiRequest<LoyaltyPointEntry[]>(`${userBase}/points/entries?skip=0&take=5`),
  ]);

  return { profile, addresses, points, pointEntries };
}

export function updateProfile(displayName: string, phoneNumber: string | null): Promise<void> {
  return apiRequest<void>(identityUserBase, {
    method: 'PUT',
    body: { displayName, phoneNumber },
  });
}

export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiRequest<void>(`${serviceRoutes.identity}/api/auth/change-password`, {
    method: 'POST',
    body: { currentPassword, newPassword },
  });
}

export function createAddress(input: AddressInput): Promise<UserAddress> {
  return apiRequest<UserAddress>(`${userBase}/addresses`, {
    method: 'POST',
    body: input,
  });
}

export function updateAddress(addressId: string, input: AddressInput): Promise<UserAddress> {
  const { makeDefault: _makeDefault, ...address } = input;
  return apiRequest<UserAddress>(`${userBase}/addresses/${addressId}`, {
    method: 'PUT',
    body: address,
  });
}

export function setDefaultAddress(addressId: string): Promise<UserAddress> {
  return apiRequest<UserAddress>(`${userBase}/addresses/${addressId}/default`, {
    method: 'PUT',
  });
}

export function deleteAddress(addressId: string): Promise<void> {
  return apiRequest<void>(`${userBase}/addresses/${addressId}`, {
    method: 'DELETE',
  });
}
