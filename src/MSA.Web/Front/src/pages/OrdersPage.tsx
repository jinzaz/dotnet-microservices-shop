import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { demoOrders } from '../features/orders/demo-orders';
import { OrderList } from '../features/orders/OrderList';
import { listOrders } from '../features/orders/orders-api';
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
  { value: 'Delivered', label: '已完成' },
  { value: 'Cancelled', label: '已取消' },
];

export default function OrdersPage() {
  const demoMode = isDemoMode();
  const [filter, setFilter] = useState<OrderFilter>('all');
  const orderQuery = useQuery({
    queryKey: ['ordering', 'orders', filter],
    queryFn: () => listOrders(filter === 'all' ? undefined : filter as OrderFulfillmentStatus),
    enabled: !demoMode,
    staleTime: 30_000,
    retry: 1,
    placeholderData: keepPreviousData,
  });

  const orders = demoMode
    ? demoOrders.filter((order) => filter === 'all' || order.fulfillmentStatus === filter)
    : orderQuery.data ?? [];
  const accountPath = demoMode ? '/account?demo=1' : '/account';

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
            <p>查看订单状态、收货信息与物流进度。</p>
            {demoMode ? <span className="demo-notice">开发预览数据 · 分类筛选可正常操作</span> : null}
          </div>
          {!demoMode ? (
            <Button
              type="button"
              variant="secondary"
              size="small"
              loading={orderQuery.isFetching}
              onClick={() => void orderQuery.refetch()}
            >
              刷新订单
            </Button>
          ) : null}
        </div>

        <nav className="order-filters" aria-label="订单分类">
          {filters.map((item) => (
            <button
              key={item.value}
              type="button"
              className={filter === item.value ? 'is-active' : undefined}
              aria-pressed={filter === item.value}
              onClick={() => setFilter(item.value)}
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
        {demoMode || orderQuery.isSuccess ? <OrderList orders={orders} demoMode={demoMode} /> : null}
      </section>
      <ServicePromises />
    </CommerceShell>
  );
}
