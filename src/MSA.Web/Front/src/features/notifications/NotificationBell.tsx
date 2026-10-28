import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getErrorMessage } from '../../shared/api/api-error';
import { Icon } from '../../shared/ui/Icon';
import { formatRelativeTime } from '../../shared/utils/format';
import { isUnread, readLastSeen, useLatestNotifications, writeLastSeen } from './notifications-api';

export function NotificationBell({ userId }: { userId: string }) {
  const [open, setOpen] = useState(false);
  const [lastSeen, setLastSeen] = useState(() => readLastSeen(userId));
  // What was unread when the panel opened, so the dots stay visible while it is open.
  const [highlightAfter, setHighlightAfter] = useState<string | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const query = useLatestNotifications(userId);
  const notifications = query.data ?? [];
  const newest = notifications[0];
  const unreadCount = notifications.filter((item) => isUnread(item, lastSeen)).length;

  // Everything visible in an open panel counts as seen, including items a refetch brings in meanwhile.
  useEffect(() => {
    if (!open || !newest || !isUnread(newest, lastSeen)) return;
    writeLastSeen(userId, newest.createdOnUtc);
    setLastSeen(newest.createdOnUtc);
  }, [open, newest, lastSeen, userId]);

  useEffect(() => {
    if (!open) return;
    const closeOnOutside = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false); };
    document.addEventListener('pointerdown', closeOnOutside);
    document.addEventListener('keydown', closeOnEscape);
    return () => {
      document.removeEventListener('pointerdown', closeOnOutside);
      document.removeEventListener('keydown', closeOnEscape);
    };
  }, [open]);

  const toggle = () => {
    if (!open) {
      setHighlightAfter(lastSeen);
      void query.refetch();
    }
    setOpen(!open);
  };

  return (
    <div className="notification-bell" ref={rootRef}>
      <button
        className="header-action"
        type="button"
        onClick={toggle}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={unreadCount ? `通知，${unreadCount} 条未读` : '通知'}
      >
        <Icon name="bell" />
        {unreadCount ? <strong className="cart-count" key={unreadCount}>{unreadCount > 9 ? '9+' : unreadCount}</strong> : null}
      </button>

      {open ? (
        <div className="notification-panel" role="dialog" aria-label="通知">
          <header className="notification-panel__header">
            <h2>通知</h2>
            {query.isFetching ? <span className="page-spinner" aria-label="正在刷新" /> : null}
          </header>

          {query.isPending ? <p className="notification-panel__state">正在加载通知…</p> : null}
          {query.isError && !notifications.length ? (
            <div className="notification-panel__state" role="alert">
              <p>{getErrorMessage(query.error)}</p>
              <button className="text-link" type="button" onClick={() => void query.refetch()}>重新加载</button>
            </div>
          ) : null}
          {query.isSuccess && !notifications.length ? (
            <div className="notification-panel__state">
              <Icon name="bell" />
              <p>暂时没有通知，订单和售后有进展时会提醒你。</p>
            </div>
          ) : null}

          {notifications.length ? (
            <ul className="notification-list">
              {notifications.map((item) => (
                <li key={item.id} className={isUnread(item, highlightAfter) ? 'is-unread' : undefined}>
                  <strong>{item.subject}</strong>
                  <p>{item.body}</p>
                  <time dateTime={item.createdOnUtc}>{formatRelativeTime(item.createdOnUtc)}</time>
                </li>
              ))}
            </ul>
          ) : null}

          {notifications.length ? (
            <Link className="notification-panel__footer" to="/notifications" onClick={() => setOpen(false)}>
              查看全部
              <Icon name="chevron-right" />
            </Link>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
