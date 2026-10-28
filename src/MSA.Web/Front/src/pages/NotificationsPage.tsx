import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { listNotifications } from '../features/notifications/notifications-api';
import { getErrorMessage } from '../shared/api/api-error';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatDateTime } from '../shared/utils/format';

const PAGE_SIZE = 20;

export default function NotificationsPage() {
  const { user } = useAuth();
  const userId = user?.subject ?? null;
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page')) || 1);
  // Ask for one extra row to know whether a next page exists; the API returns no total.
  const query = useQuery({
    queryKey: ['notifications', userId, 'page', page],
    queryFn: () => listNotifications(userId!, (page - 1) * PAGE_SIZE, PAGE_SIZE + 1),
    enabled: userId !== null,
    placeholderData: keepPreviousData,
    retry: false,
  });
  const records = query.data?.slice(0, PAGE_SIZE) ?? [];
  const hasNext = (query.data?.length ?? 0) > PAGE_SIZE;
  const goTo = (next: number) => setSearchParams(next > 1 ? { page: String(next) } : {});

  return (
    <CommerceShell>
      <section className="orders-page">
        <div className="orders-title-row">
          <div>
            <h1>消息通知</h1>
            <p>订单、发货和售后的进度提醒。</p>
          </div>
        </div>

        {query.isPending ? <div className="orders-state" role="status"><span className="page-spinner" />正在加载通知…</div> : null}
        {query.isError ? (
          <div className="orders-state orders-state--error" role="alert">
            <Icon name="alert-circle" />
            <h2>通知加载失败</h2>
            <p>{getErrorMessage(query.error)}</p>
            <Button type="button" size="small" onClick={() => void query.refetch()}>重新加载</Button>
          </div>
        ) : null}
        {query.isSuccess && !records.length ? (
          <div className="orders-empty">
            <Icon name="bell" />
            <h2>{page > 1 ? '没有更多通知了' : '暂时没有通知'}</h2>
            <p>订单确认、发货签收和售后有进展时，会在这里提醒你。</p>
          </div>
        ) : null}

        {records.length ? (
          <ul className="notification-list notification-list--page">
            {records.map((item) => (
              <li key={item.id}>
                <strong>{item.subject}</strong>
                <p>{item.body}</p>
                <time dateTime={item.createdOnUtc}>{formatDateTime(item.createdOnUtc)}</time>
              </li>
            ))}
          </ul>
        ) : null}

        {page > 1 || hasNext ? (
          <nav className="notification-pager" aria-label="分页">
            <Button type="button" variant="secondary" size="small" disabled={page <= 1} onClick={() => goTo(page - 1)}>上一页</Button>
            <span>第 {page} 页</span>
            <Button type="button" variant="secondary" size="small" disabled={!hasNext || query.isPlaceholderData} onClick={() => goTo(page + 1)}>下一页</Button>
          </nav>
        ) : null}
      </section>
    </CommerceShell>
  );
}
