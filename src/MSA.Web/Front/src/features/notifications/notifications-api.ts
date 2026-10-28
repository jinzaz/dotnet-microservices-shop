import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';

export type UserNotification = {
  id: string;
  subject: string;
  body: string;
  status: 'Pending' | 'Sending' | 'Sent' | 'Failed';
  createdOnUtc: string;
  sentOnUtc: string | null;
};

/** Served by the notification service; the gateway rule /api/notification/** strips /api.
 * The service only lets a user read their own list (token `sub` must equal userId). */
export function listNotifications(userId: string, skip = 0, take = 20): Promise<UserNotification[]> {
  return apiRequest<UserNotification[]>(
    `${serviceRoutes.notification}/users/${encodeURIComponent(userId)}?skip=${skip}&take=${take}`,
  );
}

/** First page for the header bell: polled every 60s while the tab is visible (TanStack pauses
 * refetchInterval in background tabs) and refreshed when the window regains focus.
 * ponytail: polling; switch to SSE (see BFF FlashSaleStreamEndpoints) if latency starts to matter. */
export function useLatestNotifications(userId: string | null) {
  return useQuery({
    queryKey: ['notifications', userId, 'latest'],
    queryFn: () => listNotifications(userId!, 0, 10),
    enabled: userId !== null,
    staleTime: 30_000,
    refetchInterval: 60_000,
    refetchOnWindowFocus: 'always',
    retry: false,
  });
}

// Read state lives only in this browser: anything created after the newest notification the user
// has already seen counts as unread. The server's own createdOnUtc is stored, so clock skew can't hide items.
const lastSeenKey = (userId: string) => `msa.notifications.lastSeen.${userId}`;

export function readLastSeen(userId: string): string | null {
  try {
    return localStorage.getItem(lastSeenKey(userId));
  } catch {
    return null;
  }
}

export function writeLastSeen(userId: string, createdOnUtc: string): void {
  try {
    localStorage.setItem(lastSeenKey(userId), createdOnUtc);
  } catch {
    // Private mode / blocked storage: the badge simply comes back next visit.
  }
}

export function isUnread(notification: UserNotification, lastSeen: string | null): boolean {
  return lastSeen === null || Date.parse(notification.createdOnUtc) > Date.parse(lastSeen);
}
