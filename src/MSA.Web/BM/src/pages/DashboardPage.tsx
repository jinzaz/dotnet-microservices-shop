import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { countOf, LOW_STOCK_THRESHOLD, lowStockQuery, ordersSummaryQuery, paymentsSummaryQuery, type MoneyTotal } from '../queries';
import { useSession } from '../session';
import { Empty, ErrorNotice, formatMoney, formatRelative, Icon, PageHeader, SkeletonRows, Sparkline, statusLabel, Status, type IconName } from '../ui';

const modules: Array<{ bases: string[]; label: string; path: string; icon: IconName; note: string }> = [
  { bases: ['order.fulfillment'], label: '订单履约', path: '/orders', icon: 'order', note: '发货与签收状态推进' },
  { bases: ['catalog.categories', 'catalog.products', 'catalog.promotions', 'catalog.coupons'], label: '商品目录', path: '/catalog', icon: 'catalog', note: '商品、分类、促销与券' },
  { bases: ['inventory.stock', 'inventory.allocations'], label: '库存运营', path: '/inventory', icon: 'inventory', note: '库存流水与活动配额' },
  { bases: ['identity.users', 'user.loyalty-points'], label: '用户运营', path: '/customers', icon: 'user', note: '账号状态与会员积分' },
  { bases: ['payment.transactions'], label: '支付交易', path: '/payments', icon: 'payment', note: '按订单追踪支付状态' },
  { bases: ['notification.messages'], label: '消息通知', path: '/notifications', icon: 'notification', note: '用户触达记录审阅' },
  { bases: ['system.authorization'], label: '权限治理', path: '/access', icon: 'shield', note: '用户、角色与策略' },
];

const pipeline = ['PendingPayment', 'AwaitingShipment', 'Shipped', 'InTransit', 'Delivered', 'Cancelled'];
const paymentTones: Record<string, string> = { Succeeded: 'good', Refunded: 'neutral', Refunding: 'warn', Pending: 'warn', Cancelling: 'warn', Failed: 'bad', Closed: 'bad', Expired: 'bad' };

function greeting() {
  const hour = new Date().getHours();
  return hour < 6 ? '夜深了' : hour < 12 ? '早上好' : hour < 18 ? '下午好' : '晚上好';
}

function primaryAmount(amounts: MoneyTotal[] | undefined) {
  const primary = amounts?.find((item) => item.currency === 'CNY') ?? amounts?.[0];
  return primary ? formatMoney(primary.amount, primary.currency) : formatMoney(0);
}

function delta(today: number, yesterday: number) {
  if (!yesterday) return today ? <small className="delta delta--up">新增</small> : <small className="delta">较昨日持平</small>;
  const ratio = (today - yesterday) / yesterday;
  return <small className={`delta ${ratio >= 0 ? 'delta--up' : 'delta--down'}`}>{ratio >= 0 ? '↑' : '↓'} {Math.abs(ratio * 100).toFixed(0)}% 较昨日</small>;
}

export default function DashboardPage() {
  const { session, permissions, can } = useSession();
  const canOrders = can('order.fulfillment.query');
  const canPayments = can('payment.transactions.query');
  const canStock = can('inventory.stock.query');
  const orders = useQuery({ ...ordersSummaryQuery(), enabled: canOrders });
  const payments = useQuery({ ...paymentsSummaryQuery(), enabled: canPayments });
  const lowStock = useQuery({ ...lowStockQuery(), enabled: canStock });
  const available = modules.filter((module) => module.bases.some((base) => permissions.some((code) => code.startsWith(`${base}.`))));

  const trend = orders.data?.trend ?? [];
  const today = trend.at(-1);
  const yesterday = trend.at(-2);
  const primaryCurrency = today?.paidAmounts[0]?.currency ?? 'CNY';
  const paidSeries = trend.map((day) => day.paidAmounts.find((item) => item.currency === primaryCurrency)?.amount ?? 0);
  const pipelineMax = Math.max(1, ...pipeline.map((status) => countOf(orders.data?.fulfillmentStatuses, status)));

  const paymentStatuses = Object.values((payments.data?.statuses ?? []).reduce<Record<string, { status: string; count: number }>>((result, row) => {
    result[row.status] = { status: row.status, count: (result[row.status]?.count ?? 0) + row.count };
    return result;
  }, {})).sort((left, right) => right.count - left.count);
  const paymentTotal = payments.data?.totalCount ?? 0;
  const succeededAmount = (payments.data?.statuses ?? []).filter((row) => row.status === 'Succeeded' && row.currency === primaryCurrency).reduce((sum, row) => sum + row.amount, 0);

  return <div className="page page--dashboard">
    <PageHeader eyebrow="OPERATIONS CONTROL" title={`${greeting()}，${session.name ?? '运营同事'}`}
      description={canOrders && orders.data ? `今天已产生 ${today?.orderCount ?? 0} 笔订单，${countOf(orders.data.fulfillmentStatuses, 'AwaitingShipment')} 笔等待发货。数据更新于 ${formatRelative(orders.data.generatedOnUtc)}。` : '权限、服务与操作入口都已按当前 BM 身份实时收敛。'}
      actions={canOrders ? <button className="button button--quiet" type="button" onClick={() => void Promise.all([orders.refetch(), payments.refetch(), lowStock.refetch()])} disabled={orders.isFetching}><Icon name="refresh" className={orders.isFetching ? 'is-spinning' : undefined} />刷新数据</button> : undefined} />

    {canOrders ? <>
      {orders.isError ? <ErrorNotice error={orders.error} onRetry={() => void orders.refetch()} /> : null}
      <section className="kpi-strip" aria-label="经营指标">
        <article><span>今日订单</span><strong>{orders.isPending ? '—' : (today?.orderCount ?? 0).toLocaleString('zh-CN')}</strong>{today && yesterday ? delta(today.orderCount, yesterday.orderCount) : <small className="delta">&nbsp;</small>}<Sparkline values={trend.map((day) => day.orderCount)} label="近 7 日订单量" /></article>
        <article><span>今日成交额</span><strong>{orders.isPending ? '—' : primaryAmount(today?.paidAmounts)}</strong>{today && yesterday ? delta(paidSeries.at(-1) ?? 0, paidSeries.at(-2) ?? 0) : <small className="delta">&nbsp;</small>}<Sparkline values={paidSeries} label="近 7 日成交额" /></article>
        <article><span>近 7 日成交订单</span><strong>{trend.reduce((sum, day) => sum + day.paidOrderCount, 0).toLocaleString('zh-CN')}</strong><small className="delta">共 {trend.reduce((sum, day) => sum + day.orderCount, 0).toLocaleString('zh-CN')} 笔下单</small><Sparkline values={trend.map((day) => day.paidOrderCount)} label="近 7 日成交订单" /></article>
        <Link to="/orders?status=AwaitingShipment" className="kpi-strip__action"><span>待发货</span><strong>{countOf(orders.data?.fulfillmentStatuses, 'AwaitingShipment').toLocaleString('zh-CN')}</strong><small className="delta">立即处理 <Icon name="arrow" /></small></Link>
      </section>
    </> : null}

    <div className="dashboard-board">
      {canOrders ? <section className="panel board-panel">
        <div className="section-heading"><div><p className="eyebrow">FULFILLMENT PIPELINE</p><h2>订单履约</h2></div><Link className="text-link" to="/orders">查看全部 <Icon name="arrow" /></Link></div>
        {orders.isPending ? <SkeletonRows rows={6} /> : <ul className="pipeline">{pipeline.map((status) => {
          const count = countOf(orders.data?.fulfillmentStatuses, status);
          return <li key={status}><Link to={`/orders?status=${status}`}><Status>{status}</Status><span className="pipeline__bar"><i style={{ width: `${count / pipelineMax * 100}%` }} /></span><strong>{count.toLocaleString('zh-CN')}</strong></Link></li>;
        })}</ul>}
      </section> : null}

      {canStock ? <section className="panel board-panel">
        <div className="section-heading"><div><p className="eyebrow">STOCK RISK</p><h2>库存风险</h2></div><Link className="text-link" to="/inventory?low=1">查看更多 <Icon name="arrow" /></Link></div>
        {lowStock.isError ? <ErrorNotice error={lowStock.error} onRetry={() => void lowStock.refetch()} /> : null}
        {lowStock.isPending ? <SkeletonRows rows={5} /> : lowStock.data?.items.length ? <>
          <p className="board-panel__lead"><strong>{lowStock.data.total}</strong> 个 SKU 可用库存 ≤ {LOW_STOCK_THRESHOLD}</p>
          <ul className="risk-list">{lowStock.data.items.map((row) => <li key={row.id}>
            <Link to={`/inventory?sku=${encodeURIComponent(row.sku)}`}><span className="mono">{row.sku}</span><small>在库 {row.onHand} · 预留 {row.reserved}</small><strong className={row.available === 0 ? 'number-bad' : 'number-warn'}>{row.available}</strong></Link>
          </li>)}</ul>
        </> : <Empty title="库存健康" detail={`没有可用库存低于 ${LOW_STOCK_THRESHOLD} 的 SKU。`} />}
      </section> : null}

      {canPayments ? <section className="panel board-panel">
        <div className="section-heading"><div><p className="eyebrow">PAYMENTS · 7 DAYS</p><h2>支付状态</h2></div><Link className="text-link" to="/payments">交易明细 <Icon name="arrow" /></Link></div>
        {payments.isError ? <ErrorNotice error={payments.error} onRetry={() => void payments.refetch()} /> : null}
        {payments.isPending ? <SkeletonRows rows={4} /> : paymentTotal ? <>
          <p className="board-panel__lead"><strong>{formatMoney(succeededAmount, primaryCurrency)}</strong> 支付成功 · 共 {paymentTotal} 笔交易</p>
          <div className="stack-bar" role="img" aria-label="支付状态分布">{paymentStatuses.map((row) => <i key={row.status} className={`tone--${paymentTones[row.status] ?? 'neutral'}`} style={{ flex: row.count }} title={`${statusLabel(row.status)} ${row.count}`} />)}</div>
          <ul className="legend">{paymentStatuses.map((row) => <li key={row.status}><Link to={`/payments?status=${row.status}`}><i className={`tone--${paymentTones[row.status] ?? 'neutral'}`} />{statusLabel(row.status)}<strong>{(row.count / paymentTotal * 100).toFixed(1)}%</strong><small>{row.count} 笔</small></Link></li>)}</ul>
        </> : <Empty title="暂无支付交易" detail="近 7 日没有新的支付记录。" />}
      </section> : null}

      <section className="panel board-panel">
        <div className="section-heading"><div><p className="eyebrow">AVAILABLE WORKSPACES</p><h2>运营入口</h2></div><span>{available.length} / {modules.length}</span></div>
        <div className="operation-list">{available.map((module, index) => <Link to={module.path} key={module.path}>
          <span className="operation-list__index">{String(index + 1).padStart(2, '0')}</span><Icon name={module.icon} /><span><strong>{module.label}</strong><small>{module.note}</small></span><Icon name="arrow" />
        </Link>)}</div>
      </section>

      <section className="panel board-panel">
        <div className="section-heading"><div><p className="eyebrow">SECURITY POSTURE</p><h2>权限与安全</h2></div><span className="live-label"><i />系统正常</span></div>
        <div className="security-score"><span><Icon name="shield" /></span><div><strong>{permissions.length} 项有效权限</strong><p>BFF 在 YARP 透传前按 HTTP 方法和路径匹配 BM 策略，未匹配即拒绝。</p></div></div>
        <ul className="grant-list">{modules.map((module) => {
          const allowed = module.bases.some((base) => permissions.some((code) => code.startsWith(`${base}.`)));
          return <li key={module.path} className={allowed ? 'is-allowed' : ''}><Icon name={allowed ? 'check' : 'close'} />{module.label}</li>;
        })}</ul>
        {can('system.authorization.query') ? <Link className="text-link" to="/access">查看策略控制台 <Icon name="arrow" /></Link> : null}
      </section>
    </div>
  </div>;
}
