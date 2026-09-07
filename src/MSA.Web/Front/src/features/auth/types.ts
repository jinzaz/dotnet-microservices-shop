export type LoginCredentials = {
  email: string;
  password: string;
  rememberMe: boolean;
};

export type RegistrationDetails = {
  displayName: string;
  email: string;
  password: string;
};

export type RegisteredUser = {
  id: string;
  email: string;
  displayName: string;
};

export type TokenResponse = {
  access_token: string;
  refresh_token?: string;
  token_type: string;
  expires_in: number;
  scope?: string;
};

export type AuthSession = {
  accessToken: string;
  refreshToken?: string;
  tokenType: string;
  expiresAt: number;
  scope?: string;
  rememberMe: boolean;
};
