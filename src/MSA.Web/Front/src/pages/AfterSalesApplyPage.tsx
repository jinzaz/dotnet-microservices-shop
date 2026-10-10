import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { createAfterSales, evidencePolicy, uploadEvidence, validateEvidence } from '../features/after-sales/after-sales-api';
import {
  afterSalesReasons,
  afterSalesTypeLabels,
  estimateRefund,
  getAfterSalesEligibility,
  isRefundType,
} from '../features/after-sales/after-sales-rules';
import type { AfterSalesReasonCode, AfterSalesType } from '../features/after-sales/types';
import { imageForSku } from '../features/catalog/product-view';
import { createDemoOrderDetails } from '../features/orders/demo-orders';
import { getOrder } from '../features/orders/orders-api';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatDateTime, formatMoney } from '../shared/utils/format';

const DESCRIPTION_LIMIT = 500;

type EvidenceItem = { id: string; name: string; previewUrl: string; status: 'uploading' | 'done' | 'error'; objectKey?: string };

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
  const [demoSubmitted, setDemoSubmitted] = useState(false);
  const [evidence, setEvidence] = useState<EvidenceItem[]>([]);
  const idempotencyKey = useRef(crypto.randomUUID());
  const uploading = evidence.some((item) => item.status === 'uploading');

  const updateEvidence = (id: string, patch: Partial<EvidenceItem>) =>
    setEvidence((items) => items.map((item) => item.id === id ? { ...item, ...patch } : item));

  // Upload as soon as images are picked so submitting only sends the object keys.
  const pickEvidence = (event: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []).slice(0, evidencePolicy.maxCount - evidence.length);
    event.target.value = '';
    for (const file of files) {
      const invalid = validateEvidence(file);
      if (invalid) {
        toast(invalid, { tone: 'error' });
        continue;
      }
      const item: EvidenceItem = { id: crypto.randomUUID(), name: file.name, previewUrl: URL.createObjectURL(file), status: 'uploading' };
      setEvidence((items) => [...items, item]);
      if (demoMode) {
        updateEvidence(item.id, { status: 'done', objectKey: `demo/${item.id}` });
        continue;
      }
      uploadEvidence(file).then(
        (objectKey) => updateEvidence(item.id, { status: 'done', objectKey }),
        (uploadError: unknown) => {
          updateEvidence(item.id, { status: 'error' });
          toast(getErrorMessage(uploadError), { tone: 'error' });
        },
      );
    }
  };

  const removeEvidence = (id: string) => setEvidence((items) => items.filter((item) => {
    if (item.id === id) URL.revokeObjectURL(item.previewUrl);
    return item.id !== id;
  }));

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
    if (uploading) return setError('图片还在上传，请稍候再提交。');
    if (evidence.some((item) => item.status === 'error')) return setError('有图片上传失败，请移除后重新上传。');

    setSubmitting(true);
    setError('');
    try {
      if (demoMode) {
        setDemoSubmitted(true);
        toast('演示模式不会真正提交申请', { tone: 'info' });
        return;
      }
      const created = await createAfterSales(order.id, {
        type: effectiveType,
        reasonCode,
        description: description.trim(),
        lines: chosenLines.map((line) => ({ sku: line.sku, quantity: quantities[line.sku] ?? 0 })),
        evidenceObjectKeys: evidence.flatMap((item) => item.objectKey ? [item.objectKey] : []),
      }, idempotencyKey.current);
      void queryClient.invalidateQueries({ queryKey: ['after-sales'] });
      toast('售后申请已提交，商家会尽快处理');
      navigate(`/after-sales/${created.id}`, { replace: true });
    } catch (submitError) {
      setError(getErrorMessage(submitError));
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

        {demoSubmitted ? (
          <div className="inline-alert inline-alert--info after-sales-offline" role="status">
            <Icon name="clock" />
            <div>
              <strong>演示模式</strong>
              <span>申请内容已校验通过，但演示数据不会真正提交。登录后在真实订单上即可提交售后。</span>
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
            <div className="evidence-uploader">
              <span className="evidence-uploader__label">上传凭证 <small>{evidence.length}/{evidencePolicy.maxCount}</small></span>
              <ul className="evidence-grid evidence-grid--editable">
                {evidence.map((item) => (
                  <li key={item.id} className={`evidence-tile is-${item.status}`}>
                    <img src={item.previewUrl} alt={`凭证 ${item.name}`} />
                    {item.status === 'uploading' ? <span className="evidence-tile__state" role="status"><span className="page-spinner" />上传中</span> : null}
                    {item.status === 'error' ? <span className="evidence-tile__state">上传失败</span> : null}
                    <button type="button" className="evidence-tile__remove" aria-label={`移除 ${item.name}`} onClick={() => removeEvidence(item.id)}><Icon name="close" /></button>
                  </li>
                ))}
                {evidence.length < evidencePolicy.maxCount ? (
                  <li>
                    <label className="evidence-add">
                      <input type="file" className="sr-only" accept={evidencePolicy.accept} multiple onChange={pickEvidence} />
                      <Icon name="plus" />
                      <span>添加图片</span>
                    </label>
                  </li>
                ) : null}
              </ul>
              <small className="evidence-uploader__hint">支持 JPG、PNG、WebP，单张不超过 10 MB。图片仅商家可见。</small>
            </div>
          </section>

          <aside className="order-summary after-sales-summary">
            <h2>退款预估</h2>
            <dl className="summary-totals">
              <div><dt>售后商品</dt><dd>{chosenLines.reduce((total, line) => total + (quantities[line.sku] ?? 0), 0)} 件</dd></div>
              <div className="summary-total">
                <dt>{effectiveType && !isRefundType(effectiveType) ? `${afterSalesTypeLabels[effectiveType].label}不退款` : '预计退款'}</dt>
                <dd>{formatMoney(effectiveType && !isRefundType(effectiveType) ? 0 : refund, order.currency)}</dd>
              </div>
            </dl>
            <p className="checkout-contract-note">{effectiveType && !isRefundType(effectiveType)
              ? '商家审核通过后发出新商品，可在售后详情查看物流。'
              : '按商品实付金额折算，最终以商家审核结果为准，退款原路返回。'}</p>
            {error ? <p className="form-message form-message--error" role="alert">{error}</p> : null}
            <Button type="submit" className="submit-order" loading={submitting} disabled={uploading}>{uploading ? '图片上传中…' : '提交申请'}</Button>
          </aside>
        </form>
      </section>
    </CommerceShell>
  );
}
