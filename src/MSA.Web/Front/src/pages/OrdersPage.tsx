import { keepPreviousData, useInfiniteQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { demoOrders } from '../features/orders/demo-orders';
import { OrderList } from '../features/orders/OrderList';
import { listOrders, ORDERS_PAGE_SIZE } from '../features/orders/orders-api';
import type { OrderFilter, OrderFulfillmentStatus } from '../features/orders/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';

const filters: ReadonlyArray<{ value: OrderFilter; label: string }> = [
  { value: 'all', label: '全部' },
  { value: 'PendingPayment', label: '待付款' },
  { value: 'AwaitingShipment', label: '待发货' },
  { value: 'Shipped', label: '已发货' },
  { value: 'InTransit', label: '运输中' },
  { value: 'Delivered', label: '已签收' },
  { value: 'Cancelled', label: '已取消' },
];

export default function OrdersPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const demoMode = isDemoMode(`?${searchParams}`);
  const requested = searchParams.get('status') as OrderFilter | null;
  const filter: OrderFilter = filters.some((item) => item.value === requested) ? requested! : 'all';
  const orderQuery = useInfiniteQuery({
    queryKey: ['ordering', 'orders', filter],
    queryFn: ({ pageParam }) => listOrders(filter === 'all' ? undefined : filter as OrderFulfillmentStatus, pageParam),
    initialPageParam: 0,
    // The endpoint returns a plain list, so a short page means there is nothing more to load.
    getNextPageParam: (lastPage, pages) => lastPage.length < ORDERS_PAGE_SIZE ? undefined : pages.length * ORDERS_PAGE_SIZE,
    enabled: !demoMode,
    staleTime: 30_000,
    placeholderData: keepPreviousData,
  });

  const orders = demoMode
    ? demoOrders.filter((order) => filter === 'all' || order.fulfillmentStatus === filter)
    : orderQuery.data?.pages.flat() ?? [];
  const accountPath = demoMode ? '/account?demo=1' : '/account';

  const selectFilter = (value: OrderFilter) => {
    const params = new URLSearchParams(searchParams);
    if (value === 'all') params.delete('status');
    else params.set('status', value);
    setSearchParams(params, { replace: true });
  };

  return (
    <CommerceShell>
      <section className="orders-page">
        <Link className="orders-back" to={accountPath}>
          <Icon name="arrow-right" />
          返回我的账户
        </Link>
        <div className="orders-title-row">
          <div>
            <h1>我的订单</h1>
            <p>查看订单状态、物流进度，付款后的订单可在详情中申请售后。</p>
            {demoMode ? <span className="demo-notice">开发预览数据 · 分类筛选可正常操作</span> : null}
          </div>
          <div className="orders-title-row__actions">
            <Link className="button button--secondary button--small" to={`/after-sales${demoMode ? '?demo=1' : ''}`}><Icon name="refresh" />退换 / 售后</Link>
            {!demoMode ? (
              <Button type="button" variant="ghost" size="small" loading={orderQuery.isRefetching && !orderQuery.isFetchingNextPage} onClick={() => void orderQuery.refetch()}>
                刷新
              </Button>
            ) : null}
          </div>
        </div>

        <nav className="order-filters" aria-label="订单分类">
          {filters.map((item) => (
            <button
              key={item.value}
              type="button"
              className={filter === item.value ? 'is-active' : undefined}
              aria-pressed={filter === item.value}
              onClick={() => selectFilter(item.value)}
            >
              {item.label}
            </button>
          ))}
        </nav>

        {!demoMode && orderQuery.isPending ? (
          <div className="orders-state" role="status"><span className="page-spinner" />正在加载订单…</div>
        ) : null}
        {!demoMode && orderQuery.isError ? (
          <div className="orders-state orders-state--error" role="alert">
            <Icon name="clipboard" />
            <h2>订单加载失败</h2>
            <p>{getErrorMessage(orderQuery.error)}</p>
            <Button type="button" size="small" onClick={() => void orderQuery.refetch()}>重新加载</Button>
          </div>
        ) : null}
        {demoMode || orderQuery.isSuccess ? (
          <div className={orderQuery.isPlaceholderData ? 'is-stale' : undefined}>
            <OrderList orders={orders} demoMode={demoMode} />
          </div>
        ) : null}
        {orderQuery.hasNextPage ? (
          <div className="load-more">
            <Button type="button" variant="secondary" loading={orderQuery.isFetchingNextPage} onClick={() => void orderQuery.fetchNextPage()}>加载更多订单</Button>
          </div>
        ) : null}
      </section>
      <ServicePromises />
    </CommerceShell>
  );
}
