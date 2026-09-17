import { useQuery } from '@tanstack/react-query';
import { Link, useLocation } from 'react-router-dom';
import { listAfterSales } from '../features/after-sales/after-sales-api';
import { afterSalesStatusLabels, afterSalesTypeLabels, AFTER_SALES_WINDOW_DAYS } from '../features/after-sales/after-sales-rules';
import { getErrorMessage, isServiceUnavailable } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatDateTime, formatMoney } from '../shared/utils/format';

const guideSteps = [
  { icon: 'clipboard', title: '提交申请', detail: `订单详情中选择商品，签收后 ${AFTER_SALES_WINDOW_DAYS} 天内可申请` },
  { icon: 'shield', title: '商家审核', detail: '通常 1 个工作日内处理' },
  { icon: 'truck', title: '寄回商品', detail: '退货或换货需填写寄回运单号' },
  { icon: 'coins', title: '退款 / 换新', detail: '商家签收后原路退款或发出新品' },
] as const;

export default function AfterSalesListPage() {
  const location = useLocation();
  const demoMode = isDemoMode(location.search);
  const suffix = demoMode ? '?demo=1' : '';
  const query = useQuery({
    queryKey: ['after-sales', 'list'],
    queryFn: () => listAfterSales(),
    enabled: !demoMode,
    retry: false,
    staleTime: 30_000,
  });
  const offline = demoMode || isServiceUnavailable(query.error);
  const records = query.data ?? [];

  return (
    <CommerceShell>
      <section className="orders-page">
        <Link className="orders-back" to={`/orders${suffix}`}><Icon name="arrow-right" />返回我的订单</Link>
        <div className="orders-title-row">
          <div>
            <h1>退换 / 售后</h1>
            <p>跟踪退款、退货和换货进度。</p>
          </div>
        </div>

        <ol className="after-sales-guide" aria-label="售后流程">
          {guideSteps.map((step, index) => (
            <li key={step.title}>
              <span>{index + 1}</span>
              <Icon name={step.icon} />
              <strong>{step.title}</strong>
              <small>{step.detail}</small>
            </li>
          ))}
        </ol>

        {!demoMode && query.isPending ? <div className="orders-state" role="status"><span className="page-spinner" />正在加载售后记录…</div> : null}
        {offline ? (
          <div className="orders-empty">
            <Icon name="clock" />
            <h2>在线售后服务即将开放</h2>
            <p>开放后，你提交的售后申请会在这里显示处理进度。现在可以先在订单详情中查看可售后的商品。</p>
            <Link className="button button--secondary button--small" to={`/orders?status=Delivered${demoMode ? '&demo=1' : ''}`}>查看已签收订单</Link>
          </div>
        ) : null}
        {query.isError && !offline ? (
          <div className="orders-state orders-state--error" role="alert">
            <Icon name="alert-circle" />
            <h2>售后记录加载失败</h2>
            <p>{getErrorMessage(query.error)}</p>
            <Button type="button" size="small" onClick={() => void query.refetch()}>重新加载</Button>
          </div>
        ) : null}
        {query.isSuccess && !records.length ? (
          <div className="orders-empty">
            <Icon name="refresh" />
            <h2>暂无售后记录</h2>
            <p>如商品有问题，可在订单详情中申请售后。</p>
          </div>
        ) : null}
        {records.length ? (
          <div className="order-list">
            {records.map((record) => {
              const status = afterSalesStatusLabels[record.status] ?? { label: record.status, tone: 'muted' };
              return (
                <Link className="order-row after-sales-row" key={record.id} to={`/after-sales/${record.id}${suffix}`}>
                  <header className="order-row__header">
                    <div>
                      <time dateTime={record.createdOnUtc}>{formatDateTime(record.createdOnUtc)}</time>
                      <span>售后单 {record.requestNumber} · 订单 {record.orderNumber}</span>
                    </div>
                    <span className={`order-status order-status--${status.tone}`}>{status.label}</span>
                  </header>
                  <div className="order-row__body">
                    <div className="order-delivery">
                      <span>{afterSalesTypeLabels[record.type]?.label ?? record.type}</span>
                      <strong>{record.productNames.join('、')}</strong>
                    </div>
                    <div className="order-amount">
                      <span>{record.type === 'Exchange' ? '换货' : '退款金额'}</span>
                      <strong>{formatMoney(record.refundAmount, record.currency)}</strong>
                    </div>
                  </div>
                </Link>
              );
            })}
          </div>
        ) : null}
      </section>
    </CommerceShell>
  );
}
