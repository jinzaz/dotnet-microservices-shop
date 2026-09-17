import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { cancelAfterSales, getAfterSales, submitReturnShipment } from '../features/after-sales/after-sales-api';
import { afterSalesReasons, afterSalesStatusLabels, afterSalesTypeLabels } from '../features/after-sales/after-sales-rules';
import { getErrorMessage, isServiceUnavailable } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { ConfirmDialog } from '../shared/ui/ConfirmDialog';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatDateTime, formatMoney } from '../shared/utils/format';

export default function AfterSalesDetailPage() {
  const { afterSalesId = '' } = useParams();
  const location = useLocation();
  const queryClient = useQueryClient();
  const toast = useToast();
  const demoMode = isDemoMode(location.search);
  const suffix = demoMode ? '?demo=1' : '';
  const [cancelOpen, setCancelOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [carrierName, setCarrierName] = useState('');
  const [trackingNumber, setTrackingNumber] = useState('');
  const query = useQuery({
    queryKey: ['after-sales', 'detail', afterSalesId],
    queryFn: () => getAfterSales(afterSalesId),
    enabled: Boolean(afterSalesId) && !demoMode,
    retry: false,
  });

  const refresh = (next: Awaited<ReturnType<typeof getAfterSales>>) => {
    queryClient.setQueryData(['after-sales', 'detail', afterSalesId], next);
    void queryClient.invalidateQueries({ queryKey: ['after-sales', 'list'] });
  };

  const confirmCancel = async () => {
    setBusy(true);
    try {
      refresh(await cancelAfterSales(afterSalesId));
      setCancelOpen(false);
      toast('售后申请已撤销');
    } catch (error) {
      toast(getErrorMessage(error), { tone: 'error' });
    } finally {
      setBusy(false);
    }
  };

  const submitShipment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!carrierName.trim() || !trackingNumber.trim()) return;
    setBusy(true);
    try {
      refresh(await submitReturnShipment(afterSalesId, carrierName.trim(), trackingNumber.trim()));
      toast('寄回信息已提交');
    } catch (error) {
      toast(getErrorMessage(error), { tone: 'error' });
    } finally {
      setBusy(false);
    }
  };

  if (!demoMode && query.isPending) {
    return <CommerceShell><div className="page-state" role="status"><span className="page-spinner" />正在加载售后单…</div></CommerceShell>;
  }

  const record = query.data;
  if (!record) {
    const offline = demoMode || isServiceUnavailable(query.error);
    return (
      <CommerceShell>
        <div className="page-state">
          <Icon name={offline ? 'clock' : 'alert-circle'} />
          <h1>{offline ? '在线售后服务即将开放' : '售后单暂时无法打开'}</h1>
          <p>{offline ? '服务开放后可在这里查看审核、寄回和退款进度。' : getErrorMessage(query.error)}</p>
          <Link className="button button--primary button--regular" to={`/after-sales${suffix}`}>返回售后列表</Link>
        </div>
      </CommerceShell>
    );
  }

  const status = afterSalesStatusLabels[record.status] ?? { label: record.status, tone: 'muted' };
  const reason = afterSalesReasons.find((item) => item.code === record.reasonCode)?.label ?? record.reasonCode;
  const history = [...record.history].sort((left, right) => Date.parse(right.occurredOnUtc) - Date.parse(left.occurredOnUtc));

  return (
    <CommerceShell>
      <section className="order-detail-page">
        <Link className="orders-back" to={`/after-sales${suffix}`}><Icon name="arrow-right" />返回售后列表</Link>
        <header className="order-detail-header">
          <div>
            <span className={`order-status order-status--${status.tone}`}>{status.label}</span>
            <h1>{afterSalesTypeLabels[record.type]?.label ?? record.type}</h1>
            <p>售后单 {record.requestNumber} · 关联订单 <Link to={`/orders/${record.orderId}${suffix}`}>{record.orderNumber}</Link> · 申请于 {formatDateTime(record.createdOnUtc)}</p>
            {record.rejectionReason ? <p className="order-detail-header__hint">拒绝原因：{record.rejectionReason}</p> : null}
          </div>
          <div className="order-detail-actions">
            {record.status === 'Requested' ? <Button type="button" variant="secondary" onClick={() => setCancelOpen(true)}>撤销申请</Button> : null}
          </div>
        </header>

        <div className="order-detail-layout">
          <div className="order-detail-main">
            {record.status === 'AwaitingReturn' ? (
              <section className="order-card order-card--notice">
                <h2>请寄回商品</h2>
                {record.returnAddress ? <p className="address-block"><span>{record.returnAddress}</span></p> : null}
                <form className="return-shipment-form" onSubmit={submitShipment}>
                  <label className="form-field"><span>物流公司</span><input className="form-field__control" value={carrierName} onChange={(event) => setCarrierName(event.target.value)} maxLength={120} required /></label>
                  <label className="form-field"><span>运单号</span><input className="form-field__control" value={trackingNumber} onChange={(event) => setTrackingNumber(event.target.value)} maxLength={120} required /></label>
                  <Button type="submit" loading={busy}>提交寄回信息</Button>
                </form>
              </section>
            ) : null}

            <section className="order-card">
              <h2>售后商品</h2>
              <ul className="order-lines">
                {record.lines.map((line) => (
                  <li key={line.sku}>
                    <div className="order-lines__info">
                      <strong>{line.productName}</strong>
                      <span>{line.variantName || line.sku}</span>
                    </div>
                    <div className="order-lines__price">
                      <span>× {line.quantity}</span>
                      <strong>{formatMoney(line.refundAmount, record.currency)}</strong>
                    </div>
                  </li>
                ))}
              </ul>
            </section>

            {history.length ? (
              <section className="order-card">
                <h2>处理进度</h2>
                <ol className="timeline">
                  {history.map((entry) => (
                    <li key={`${entry.status}-${entry.occurredOnUtc}`}>
                      <strong>{afterSalesStatusLabels[entry.status]?.label ?? entry.status}</strong>
                      {entry.note ? <span>{entry.note}</span> : null}
                      <time dateTime={entry.occurredOnUtc}>{formatDateTime(entry.occurredOnUtc)}</time>
                    </li>
                  ))}
                </ol>
              </section>
            ) : null}
          </div>

          <aside className="order-detail-side">
            <section className="order-card">
              <h2>申请信息</h2>
              <dl className="order-meta">
                <div><dt>售后原因</dt><dd>{reason}</dd></div>
                {record.description ? <div><dt>补充说明</dt><dd>{record.description}</dd></div> : null}
                {record.returnShipment ? <div><dt>寄回运单</dt><dd>{record.returnShipment.carrierName} {record.returnShipment.trackingNumber}</dd></div> : null}
                <div><dt>{record.type === 'Exchange' ? '换货' : '退款金额'}</dt><dd>{formatMoney(record.refundAmount, record.currency)}</dd></div>
              </dl>
              {record.evidenceUrls.length ? (
                <div className="evidence-grid">
                  {record.evidenceUrls.map((url) => <a key={url} href={url} target="_blank" rel="noreferrer"><img src={url} alt="售后凭证" loading="lazy" /></a>)}
                </div>
              ) : null}
            </section>
          </aside>
        </div>
      </section>
      <ConfirmDialog
        open={cancelOpen}
        title="撤销售后申请？"
        description="撤销后如仍在售后期内，可以重新提交申请。"
        confirmLabel="确认撤销"
        tone="danger"
        loading={busy}
        onConfirm={() => void confirmCancel()}
        onClose={() => setCancelOpen(false)}
      />
    </CommerceShell>
  );
}
