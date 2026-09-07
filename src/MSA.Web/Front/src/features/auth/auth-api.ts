import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { RegisteredUser, RegistrationDetails } from './types';

export function registerAccount(details: RegistrationDetails): Promise<RegisteredUser> {
  return apiRequest<RegisteredUser>(`${serviceRoutes.identity}/api/auth/register`, {
    method: 'POST',
    auth: false,
    body: details,
  });
}
