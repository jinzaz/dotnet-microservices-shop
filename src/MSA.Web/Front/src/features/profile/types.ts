export type UserProfile = {
  id: string;
  /** 短信登录即注册的账户没有邮箱。 */
  email: string | null;
  displayName: string;
  phoneNumber: string | null;
  phoneNumberConfirmed: boolean;
  isActive: boolean;
  createdOnUtc: string;
  roles: string[];
};

export type UserAddress = {
  id: string;
  label: string;
  recipientName: string;
  phoneNumber: string;
  countryCode: string;
  province: string;
  city: string;
  district: string;
  street: string;
  postalCode: string | null;
  isDefault: boolean;
  createdOnUtc: string;
  updatedOnUtc: string;
};

export type AddressInput = {
  label: string;
  recipientName: string;
  phoneNumber: string;
  countryCode: string;
  province: string;
  city: string;
  district: string;
  street: string;
  postalCode: string | null;
  makeDefault: boolean;
};

export type LoyaltyPointAccount = {
  userId: string;
  balance: number;
  lifetimeEarned: number;
  lifetimeSpent: number;
  createdOnUtc: string | null;
  updatedOnUtc: string | null;
};

export type LoyaltyPointEntry = {
  id: string;
  delta: number;
  balanceBefore: number;
  balanceAfter: number;
  source: string;
  reason: string;
  occurredOnUtc: string;
};

export type AccountSnapshot = {
  profile: UserProfile;
  addresses: UserAddress[];
  points: LoyaltyPointAccount;
  pointEntries: LoyaltyPointEntry[];
};
