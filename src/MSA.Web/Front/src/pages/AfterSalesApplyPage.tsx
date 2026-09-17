import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { createAfterSales } from '../features/after-sales/after-sales-api';
import {
  afterSalesReasons,
  afterSalesTypeLabels,
  estimateRefund,
  getAfterSalesEligibility,
} from '../features/after-sales/after-sales-rules';
import type { AfterSalesReasonCode, AfterSalesType } from '../features/after-sales/types';
import { imageForSku } from '../features/catalog/product-view';
import { createDemoOrderDetails } from '../features/orders/demo-orders';
import { getOrder } from '../features/orders/orders-api';
import { getErrorMessage, isServiceUnavailable } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatDateTime, formatMoney } from '../shared/utils/format';

const DESCRIPTION_LIMIT = 500;

export default function AfterSalesApplyPage() {
  const { orderId = '' } = useParams();
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const demoMode = isDemoMode(location.search);
  const suffix = demoMode ? '?demo=1' : '';
  const orderQuery = useQuery({
    queryKey: ['ordering', 'order', orderId],
    queryFn: () => getOrder(orderId),
    enabled: Boolean(orderId) && !demoMode,
    staleTime: 15_000,
  });
  const order = demoMode ? createDemoOrderDetails(orderId) : orderQuery.data;
  const eligibility = order ? getAfterSalesEligibility(order) : null;
  const preselectedSku = searchParams.get('sku');

  const [type, setType] = useState<AfterSalesType | null>(null);
  const [reasonCode, setReasonCode] = useState<AfterSalesReasonCode | ''>('');
  const [description, setDescription] = useState('');
  const [selected, setSelected] = useState<Record<string, number> | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [serviceOffline, setServiceOffline] = useState(false);
  const idempotencyKey = useRef(crypto.randomUUID());

  const effectiveType = type ?? eligibility?.types[0] ?? null;
  // Default selection: the line the shopper came from, otherwise every line in full.
  const quantities = useMemo(() => selected ?? Object.fromEntries((order?.lines ?? []).map((line) => [
    line.sku,
    !preselectedSku || preselectedSku === line.sku ? line.quantity : 0,
  ])), [order?.lines, preselectedSku, selected]);
  const reasons = afterSalesReasons.filter((reason) => effectiveType && reason.types.includes(effectiveType));
  const chosenLines = (order?.lines ?? []).filter((line) => (quantities[line.sku] ?? 0) > 0);
  const refund = order
    ? estimateRefund(order.lines.map((line) => ({ lineAmount: line.lineAmount, quantity: line.quantity, selected: quantities[line.sku] ?? 0 })))
    : 0;

  const setQuantity = (sku: string, value: number, max: number) => {
    setSelected({ ...quantities, [sku]: Math.min(max, Math.max(0, value)) });
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || !effectiveType) return;
    if (!chosenLines.length) return setError('请至少选择一件需要售后的商品。');
    if (!reasonCode) return setError('请选择售后原因。');
    if (reasonCode === 'Other' && description.trim().length < 5) return setError('选择“其他原因”时，请简要描述问题（至少 5 个字）。');

    setSubmitting(true);
    setError('');
    try {
      if (demoMode) throw Object.assign(new Error('demo'), { demo: true });
      const created = await createAfterSales(order.id, {
        type: effectiveType,
        reasonCode,
        description: description.trim(),
        lines: chosenLines.map((line) => ({ sku: line.sku, quantity: quantities[line.sku] ?? 0 })),
        evidenceObjectKeys: [],
      }, idempotencyKey.current);
      void queryClient.invalidateQueries({ queryKey: ['after-sales'] });
      toast('售后申请已提交，商家会尽快处理');
      navigate(`/after-sales/${created.id}`, { replace: true });
    } catch (submitError) {
      if (demoMode || isServiceUnavailable(submitError)) {
        setServiceOffline(true);
        toast('在线售后服务即将开放，本次申请暂未提交', { tone: 'info' });
      } else {
        setError(getErrorMessage(submitError));
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (!demoMode && orderQuery.isPending) {
    return <CommerceShell><div className="page-state" role="status"><span className="page-spinner" />正在加载订单…</div></CommerceShell>;
  }

  if (!order || !eligibility) {
    return (
      <CommerceShell>
        <div className="page-state">
          <Icon name="receipt" />
          <h1>订单暂时无法打开</h1>
          <p>{getErrorMessage(orderQuery.error, '请返回订单列表后重试。')}</p>
          <Link className="button button--primary button--regular" to={`/orders${suffix}`}>返回我的订单</Link>
        </div>
      </CommerceShell>
    );
  }

  const backToOrder = `/orders/${order.id}${suffix}`;

  if (!eligibility.eligible) {
    return (
      <CommerceShell>
        <div className="page-state">
          <Icon name="help-circle" />
          <h1>该订单暂不能申请售后</h1>
          <p>{eligibility.reason}</p>
          <Link className="button button--primary button--regular" to={backToOrder}>返回订单详情</Link>
        </div>
      </CommerceShell>
    );
  }

  return (
    <CommerceShell>
      <section className="after-sales-page">
        <Link className="orders-back" to={backToOrder}><Icon name="arrow-right" />返回订单详情</Link>
        <div className="orders-title-row">
          <div>
            <h1>申请售后</h1>
            <p>订单号 {order.orderNumber}{eligibility.deadlineUtc ? ` · 申请截止 ${formatDateTime(eligibility.deadlineUtc)}` : ''}</p>
          </div>
        </div>

        {serviceOffline ? (
          <div className="inline-alert inline-alert--info after-sales-offline" role="status">
            <Icon name="clock" />
            <div>
              <strong>在线售后服务即将开放</strong>
              <span>你的申请内容还保留在当前页面，暂未提交。急需处理请联系在线客服（9:00–21:00），并提供订单号 {order.orderNumber}。</span>
            </div>
          </div>
        ) : null}

        <form className="after-sales-form" onSubmit={submit} noValidate>
          <section className="order-card">
            <h2>1. 售后类型</h2>
            <div className="choice-cards">
              {(Object.keys(afterSalesTypeLabels) as AfterSalesType[]).map((value) => {
                const allowed = eligibility.types.includes(value);
                return (
                  <label key={value} className={`${effectiveType === value ? 'is-selected' : ''} ${allowed ? '' : 'is-disabled'}`.trim() || undefined}>
                    <input
                      type="radio"
                      name="after-sales-type"
                      value={value}
                      checked={effectiveType === value}
                      disabled={!allowed}
                      onChange={() => { setType(value); setReasonCode(''); }}
                    />
                    <strong>{afterSalesTypeLabels[value].label}</strong>
                    <span>{allowed ? afterSalesTypeLabels[value].description : '当前订单状态不支持'}</span>
                  </label>
                );
              })}
            </div>
          </section>

          <section className="order-card">
            <h2>2. 选择商品</h2>
            <ul className="order-lines order-lines--selectable">
              {order.lines.map((line) => {
                const quantity = quantities[line.sku] ?? 0;
                return (
                  <li key={line.sku} className={quantity ? 'is-selected' : undefined}>
                    <label className="line-check">
                      <input type="checkbox" checked={quantity > 0} onChange={(event) => setQuantity(line.sku, event.target.checked ? line.quantity : 0, line.quantity)} />
                      <span className="sr-only">选择{line.productName}</span>
                    </label>
                    <img src={imageForSku(line.sku, line.productId ?? line.sku)} alt="" width="72" height="72" loading="lazy" />
                    <div className="order-lines__info">
                      <strong>{line.productName}</strong>
                      <span>{line.variantName || line.sku}</span>
                      <small>实付 {formatMoney(line.lineAmount, order.currency)} · 共 {line.quantity} 件</small>
                    </div>
                    <div className="quantity-stepper" aria-label={`${line.productName}售后数量`}>
                      <button type="button" aria-label="减少数量" disabled={quantity <= 0} onClick={() => setQuantity(line.sku, quantity - 1, line.quantity)}><Icon name="minus" /></button>
                      <output>{quantity}</output>
                      <button type="button" aria-label="增加数量" disabled={quantity >= line.quantity} onClick={() => setQuantity(line.sku, quantity + 1, line.quantity)}><Icon name="plus" /></button>
                    </div>
                  </li>
                );
              })}
            </ul>
          </section>

          <section className="order-card">
            <h2>3. 问题描述</h2>
            <label className="form-field">
              <span>售后原因</span>
              <select className="form-field__control" value={reasonCode} onChange={(event) => setReasonCode(event.target.value as AfterSalesReasonCode)} required>
                <option value="" disabled>请选择原因</option>
                {reasons.map((reason) => <option key={reason.code} value={reason.code}>{reason.label}</option>)}
              </select>
            </label>
            <label className="form-field">
              <span>补充说明 <small>{description.length}/{DESCRIPTION_LIMIT}</small></span>
              <textarea
                className="form-field__control"
                rows={4}
                maxLength={DESCRIPTION_LIMIT}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="描述商品问题，便于商家更快处理"
              />
            </label>
            <div className="evidence-placeholder">
              <Icon name="plus" />
              <div>
                <strong>上传凭证</strong>
                <span>图片凭证上传将随售后服务一同开放，最多 6 张。</span>
              </div>
            </div>
          </section>

          <aside className="order-summary after-sales-summary">
            <h2>退款预估</h2>
            <dl className="summary-totals">
              <div><dt>售后商品</dt><dd>{chosenLines.reduce((total, line) => total + (quantities[line.sku] ?? 0), 0)} 件</dd></div>
              <div className="summary-total">
                <dt>{effectiveType === 'Exchange' ? '换货不退款' : '预计退款'}</dt>
                <dd>{formatMoney(effectiveType === 'Exchange' ? 0 : refund, order.currency)}</dd>
              </div>
            </dl>
            <p className="checkout-contract-note">按商品实付金额折算，最终以商家审核结果为准，退款原路返回。</p>
            {error ? <p className="form-message form-message--error" role="alert">{error}</p> : null}
            <Button type="submit" className="submit-order" loading={submitting}>提交申请</Button>
          </aside>
        </form>
      </section>
    </CommerceShell>
  );
}
