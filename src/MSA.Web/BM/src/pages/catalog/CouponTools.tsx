import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../../api';
import { asLocalInput, CopyButton, ErrorNotice, Field, formatDate, Icon, statusLabel, SubmitBar } from '../../ui';
import type { PromotionDetail } from './PromotionForm';

export type IssuedCoupon = { id: string; code: string; maskedCode?: string; assignedUserId?: string | null };
export type CouponUsage = {
  couponId: string;
  reservedCount: number;
  consumedCount: number;
  releasedCount: number;
  consumedDiscountTotal: number;
  remainingQuota: number | null;
  lastRedeemedOnUtc: string | null;
};

const MAX_BATCH = 500;

/** Redemption counters live in Ordering; the coupon screen reads them with the same query permission. */
export function useCouponUsage(couponIds: string[], enabled: boolean) {
  const ids = [...couponIds].sort();
  return useQuery({
    queryKey: ['order', 'coupon-usage', ids],
    queryFn: ({ signal }) => api<CouponUsage[]>(`/api/order/admin/coupons/usage?${ids.map((id) => `couponIds=${encodeURIComponent(id)}`).join('&')}`, { signal }),
    enabled: enabled && ids.length > 0,
    staleTime: 30_000,
    select: (rows) => new Map(rows.map((row) => [row.couponId, row])),
  });
}

function parseUserIds(value: string) {
  return Array.from(new Set(value.split(/[\s,;，；]+/).map((item) => item.trim()).filter(Boolean)));
}

export function CouponBatchForm({ promotions, busy, error, onSubmit, onCancel }: {
  promotions: PromotionDetail[];
  busy: boolean;
  error: unknown;
  onSubmit: (body: unknown) => void;
  onCancel: () => void;
}) {
  const couponPromotions = promotions.filter((item) => item.triggerType === 'Coupon' && !['Ended', 'Archived'].includes(item.status));
  const [promotionId, setPromotionId] = useState('');
  const [mode, setMode] = useState<'users' | 'codes'>('users');
  const [userText, setUserText] = useState('');
  const [localError, setLocalError] = useState('');
  const selected = couponPromotions.find((item) => item.id === promotionId);
  const users = parseUserIds(userText);

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    if (mode === 'users' && (users.length === 0 || users.length > MAX_BATCH)) {
      setLocalError(`请填写 1–${MAX_BATCH} 个用户 ID。`);
      return;
    }
    setLocalError('');
    onSubmit({
      promotionId,
      count: mode === 'codes' ? Number(value('count')) : 0,
      assignedUserIds: mode === 'users' ? users : null,
      codePrefix: value('codePrefix') || null,
      validFromUtc: new Date(value('validFrom')).toISOString(),
      validUntilUtc: new Date(value('validUntil')).toISOString(),
      totalLimitPerCoupon: value('totalLimit') ? Number(value('totalLimit')) : null,
      perUserLimit: Number(value('perUserLimit') || 1),
      activate: data.get('activate') === 'on',
    });
  };

  return <form className="modal-form" onSubmit={submit}>
    <div className="form-section-title"><span>01</span><div><strong>发放对象</strong><small>定向券进入用户「我的优惠券」，结算时直接勾选；通用码需自行分发</small></div></div>
    <Field label="促销活动" hint="仅显示「凭券使用」的活动；立即启用要求活动已发布">
      <select required value={promotionId} onChange={(event) => setPromotionId(event.target.value)}>
        <option value="">选择促销活动</option>
        {couponPromotions.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.code} · {statusLabel(item.status)}</option>)}
      </select>
    </Field>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="发放方式">
      <button type="button" role="radio" aria-checked={mode === 'users'} aria-selected={mode === 'users'} onClick={() => setMode('users')}>发给指定用户</button>
      <button type="button" role="radio" aria-checked={mode === 'codes'} aria-selected={mode === 'codes'} onClick={() => setMode('codes')}>生成通用券码</button>
    </div>
    {mode === 'users'
      ? <Field label={`用户 ID（已识别 ${users.length} 个）`} hint="换行、空格或逗号分隔，可从「用户」页面复制；每个用户一张券">
        <textarea rows={5} value={userText} onChange={(event) => setUserText(event.target.value)} className="mono" placeholder={'user-id-1\nuser-id-2'} />
      </Field>
      : <Field label="生成数量" hint={`一次最多 ${MAX_BATCH} 张，每张券码不同`}><input name="count" type="number" min={1} max={MAX_BATCH} defaultValue={100} required /></Field>}

    <div className="form-section-title"><span>02</span><div><strong>券规则</strong><small>有效期必须落在活动周期内</small></div></div>
    <div className="form-row" key={selected?.id ?? 'none'}>
      <Field label="生效时间"><input name="validFrom" type="datetime-local" defaultValue={asLocalInput(selected ? new Date(Math.max(Date.now(), new Date(selected.startsOnUtc).getTime())) : new Date())} min={selected ? asLocalInput(new Date(selected.startsOnUtc)) : undefined} required /></Field>
      <Field label="失效时间"><input name="validUntil" type="datetime-local" defaultValue={asLocalInput(selected ? new Date(selected.endsOnUtc) : new Date(Date.now() + 7 * 86_400_000))} max={selected ? asLocalInput(new Date(selected.endsOnUtc)) : undefined} required /></Field>
    </div>
    <div className="form-row">
      <Field label="每张券总次数" hint="留空表示不限（通用码常用）"><input name="totalLimit" type="number" min={1} /></Field>
      <Field label="每用户次数"><input name="perUserLimit" type="number" min={1} defaultValue={1} required /></Field>
    </div>
    {/* Keyed on the promotion so the activation default follows the newly selected promotion's status. */}
    <div className="form-row" key={`activate-${selected?.id ?? 'none'}`}>
      <Field label="券码前缀" hint="可选，字母数字或短横线，最多 20 位"><input name="codePrefix" maxLength={20} pattern="[A-Za-z0-9\-]*" className="mono" placeholder="例如 VIP" /></Field>
      <Field label="发放后状态" hint={selected && selected.status !== 'Active' ? '活动未发布，只能先创建草稿券' : '启用后用户即可使用'}>
        <label className="switch-row"><input type="checkbox" name="activate" defaultChecked={selected?.status === 'Active'} disabled={selected ? selected.status !== 'Active' : false} /><span />立即启用</label>
      </Field>
    </div>
    {localError ? <p className="field-error" role="alert">{localError}</p> : null}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label={mode === 'users' ? `发放 ${users.length || ''} 张定向券` : '生成券码'} onCancel={onCancel} />
  </form>;
}

function downloadCsv(coupons: IssuedCoupon[], name: string) {
  const escape = (value: string) => `"${value.replaceAll('"', '""')}"`;
  const rows = [['券码', '指定用户', '券 ID'], ...coupons.map((coupon) => [coupon.code, coupon.assignedUserId ?? '', coupon.id])];
  // BOM keeps Excel from mis-decoding the Chinese header.
  const blob = new Blob([String.fromCharCode(0xfeff) + rows.map((row) => row.map(escape).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = Object.assign(document.createElement('a'), { href: url, download: name });
  link.click();
  URL.revokeObjectURL(url);
}

/** Plaintext codes exist only in this response: the service stores digests, so this is the one chance to export them. */
export function IssuedCouponsView({ coupons, onClose }: { coupons: IssuedCoupon[]; onClose: () => void }) {
  const [exported, setExported] = useState(false);
  const allCodes = coupons.map((coupon) => coupon.code).join('\n');
  return <div className="modal-form">
    <p className="notice notice--warning"><Icon name="alert" />完整券码只显示这一次，关闭后系统无法再查看。请复制或下载保存。</p>
    <div className="issued-codes">
      {coupons.slice(0, 200).map((coupon) => <div key={coupon.id}>
        <code>{coupon.code}</code>
        <small>{coupon.assignedUserId ? `→ ${coupon.assignedUserId}` : '通用券码'}</small>
        <CopyButton value={coupon.code} label="复制券码" />
      </div>)}
      {coupons.length > 200 ? <p className="muted-copy">另有 {coupons.length - 200} 张，请下载 CSV 查看。</p> : null}
    </div>
    <div className="submit-bar">
      <CopyButton value={allCodes} label={`复制全部 ${coupons.length} 个券码`} />
      <button className="button button--quiet" type="button" onClick={() => { downloadCsv(coupons, `coupons-${formatDate(new Date().toISOString()).replace(/[^\d]/g, '')}.csv`); setExported(true); }}><Icon name="download" />下载 CSV</button>
      <button className="button button--dark" type="button" onClick={onClose}>{exported ? '完成' : '我已保存'}</button>
    </div>
  </div>;
}
