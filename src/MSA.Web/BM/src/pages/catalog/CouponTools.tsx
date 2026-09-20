import { useQuery } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api } from '../../api';
import { asLocalInput, CopyButton, Empty, ErrorNotice, Field, formatDate, formatMoney, Icon, Status, statusLabel, SubmitBar } from '../../ui';
import { UserPicker } from '../../pickers';
import { offerText, windowPhase } from './promotion-text';
import type { PromotionDetail } from './PromotionForm';

export type Coupon = {
  id: string; promotionId: string; maskedCode: string; status: string; validFromUtc: string; validUntilUtc: string;
  assignedUserId?: string | null; totalLimit?: number | null; perUserLimit: number; quotaStrategy: string; version: number; campaignId?: string | null;
};
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
export type CouponBulkAction = 'activate' | 'disable';
type CouponActionOptions = { name: string; reason?: boolean; danger?: boolean; detail?: string };

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

const couponEnded = (row: Coupon) => Date.now() >= new Date(row.validUntilUtc).getTime();
export const canActivateCoupon = (row: Coupon) => row.status !== 'Active' && row.status !== 'Expired' && !couponEnded(row);
export const canDisableCoupon = (row: Coupon) => row.status !== 'Disabled' && row.status !== 'Expired' && !couponEnded(row);

/* ---------- Shared pieces ---------- */

export function PromotionSummaryCard({ promotion }: { promotion?: PromotionDetail }) {
  if (!promotion) return null;
  const phase = windowPhase(promotion.startsOnUtc, promotion.endsOnUtc);
  return <div className="promotion-preview promotion-preview--compact">
    <span className="promotion-preview__badge">凭券</span>
    <div>
      <strong>{offerText(promotion.rules, promotion.benefits)}</strong>
      <small>{formatDate(promotion.startsOnUtc)} 至 {formatDate(promotion.endsOnUtc)} · <span className={`phase phase--${phase.tone}`}>{phase.label}</span> · {statusLabel(promotion.status)}</small>
    </div>
  </div>;
}

function usableCouponPromotions(promotions: PromotionDetail[]) {
  return promotions.filter((item) => item.triggerType === 'Coupon' && !['Ended', 'Archived'].includes(item.status));
}

function NoCouponPromotion({ onCancel }: { onCancel: () => void }) {
  return <div className="modal-form">
    <Empty title="还没有可发券的活动" detail="优惠券必须挂在「凭券使用」的促销活动下。已结束或归档的活动不能再发券。请先在「促销」中用「优惠券活动」模板创建一个活动。" />
    <div className="submit-bar"><button className="button button--quiet" type="button" onClick={onCancel}>知道了</button></div>
  </div>;
}

/** Validity defaults to the promotion window; presets are clamped to it because the service rejects anything wider. */
function ValidityFields({ promotion, from, until, onFrom, onUntil }: { promotion?: PromotionDetail; from: string; until: string; onFrom: (value: string) => void; onUntil: (value: string) => void }) {
  const promotionEnd = promotion ? new Date(promotion.endsOnUtc) : undefined;
  const preset = (days: number | 'promotion') => {
    const start = new Date(from);
    const target = days === 'promotion' || !promotionEnd ? promotionEnd : new Date(Math.min(start.getTime() + days * 86_400_000, promotionEnd.getTime()));
    if (target) onUntil(asLocalInput(target));
  };
  return <>
    <div className="form-row">
      <Field label="生效时间"><input type="datetime-local" value={from} min={promotion ? asLocalInput(new Date(promotion.startsOnUtc)) : undefined} onChange={(event) => onFrom(event.target.value)} required /></Field>
      <Field label="失效时间"><input type="datetime-local" value={until} max={promotionEnd ? asLocalInput(promotionEnd) : undefined} onChange={(event) => onUntil(event.target.value)} required /></Field>
    </div>
    {promotion ? <div className="preset-row" aria-label="有效期">
      <span>有效期</span>
      <button type="button" onClick={() => preset('promotion')}>与活动同步</button>
      <button type="button" onClick={() => preset(7)}>7 天</button>
      <button type="button" onClick={() => preset(30)}>30 天</button>
    </div> : null}
  </>;
}

function defaultWindow(promotion?: PromotionDetail) {
  if (!promotion) return { from: asLocalInput(new Date()), until: asLocalInput(new Date(Date.now() + 7 * 86_400_000)) };
  return {
    from: asLocalInput(new Date(Math.max(Date.now(), new Date(promotion.startsOnUtc).getTime()))),
    until: asLocalInput(new Date(promotion.endsOnUtc)),
  };
}

/* ---------- Single coupon ---------- */

export function CouponCreateForm({ promotions, canReadPromotions, canSearchUsers, initialPromotionId, busy, error, onSubmit, onCancel }: {
  promotions: PromotionDetail[];
  canReadPromotions: boolean;
  canSearchUsers: boolean;
  initialPromotionId?: string;
  busy: boolean;
  error: unknown;
  onSubmit: (body: unknown) => void;
  onCancel: () => void;
}) {
  const couponPromotions = usableCouponPromotions(promotions);
  const [promotionId, setPromotionId] = useState(initialPromotionId ?? (couponPromotions.length === 1 ? couponPromotions[0]!.id : ''));
  const selected = couponPromotions.find((item) => item.id === promotionId);
  const [window, setWindow] = useState(() => defaultWindow(selected));
  const [codeMode, setCodeMode] = useState<'auto' | 'custom'>('auto');
  const [code, setCode] = useState('');
  const [audience, setAudience] = useState<'anyone' | 'user'>('anyone');
  const [users, setUsers] = useState<string[]>([]);
  const [strategy, setStrategy] = useState('SingleRow');
  const [localError, setLocalError] = useState('');

  if (canReadPromotions && !couponPromotions.length) return <NoCouponPromotion onCancel={onCancel} />;

  const choosePromotion = (id: string) => {
    setPromotionId(id);
    setWindow(defaultWindow(couponPromotions.find((item) => item.id === id)));
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    if (codeMode === 'custom' && code.trim().length < 6) return setLocalError('自定义券码至少 6 位。');
    if (audience === 'user' && !users.length) return setLocalError('请选择要发放的用户。');
    if (new Date(window.until) <= new Date(window.from)) return setLocalError('失效时间必须晚于生效时间。');
    setLocalError('');
    onSubmit({
      promotionId,
      // An empty code lets the service generate one; it is shown once after creation.
      code: codeMode === 'custom' ? code.trim().toUpperCase() : null,
      codePrefix: codeMode === 'auto' ? value('codePrefix') || null : null,
      validFromUtc: new Date(window.from).toISOString(),
      validUntilUtc: new Date(window.until).toISOString(),
      assignedUserId: audience === 'user' ? users[0] : null,
      totalLimit: value('totalLimit') ? Number(value('totalLimit')) : null,
      perUserLimit: Number(value('perUserLimit') || 1),
      quotaStrategy: strategy,
      bucketCount: strategy === 'Bucketed' ? Number(value('bucketCount')) : null,
    });
  };

  return <form className="modal-form" onSubmit={submit}>
    <Field label="促销活动" hint={!canReadPromotions ? '需要 catalog.promotions.query 才能选择活动' : '仅显示「凭券使用」且未结束的活动'}>
      <select required disabled={!canReadPromotions} value={promotionId} onChange={(event) => choosePromotion(event.target.value)}><option value="">选择促销活动</option>{couponPromotions.map((item) => <option key={item.id} value={item.id}>{item.name} · {offerText(item.rules, item.benefits)} · {statusLabel(item.status)}</option>)}</select>
    </Field>
    <PromotionSummaryCard promotion={selected} />
    {selected && selected.status !== 'Active' ? <p className="notice notice--warning"><Icon name="alert" />活动尚未发布，券会以草稿创建；活动发布后再到列表中启用。</p> : null}

    <div className="form-section-title"><span>01</span><div><strong>券码</strong><small>系统只保存摘要，创建后会展示一次完整券码</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="券码方式">
      <button type="button" role="radio" aria-checked={codeMode === 'auto'} aria-selected={codeMode === 'auto'} onClick={() => setCodeMode('auto')}>自动生成</button>
      <button type="button" role="radio" aria-checked={codeMode === 'custom'} aria-selected={codeMode === 'custom'} onClick={() => setCodeMode('custom')}>自定义</button>
    </div>
    {codeMode === 'auto'
      ? <Field label="券码前缀" hint="可选，便于识别渠道，例如 VIP、WECHAT"><input name="codePrefix" maxLength={20} pattern="[A-Za-z0-9\-]*" className="mono" placeholder="可选" /></Field>
      : <Field label="自定义券码" hint="至少 6 位，字母数字组合，大小写不敏感"><input value={code} onChange={(event) => setCode(event.target.value.toUpperCase())} minLength={6} maxLength={80} className="mono" placeholder="例如 AUTUMN2026" /></Field>}

    <div className="form-section-title"><span>02</span><div><strong>发放对象与额度</strong><small>定向券进入用户「我的优惠券」，结算时可直接勾选</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="发放对象">
      <button type="button" role="radio" aria-checked={audience === 'anyone'} aria-selected={audience === 'anyone'} onClick={() => setAudience('anyone')}>通用（凭码使用）</button>
      <button type="button" role="radio" aria-checked={audience === 'user'} aria-selected={audience === 'user'} onClick={() => setAudience('user')}>指定用户</button>
    </div>
    {audience === 'user' ? <UserPicker value={users} onChange={setUsers} canSearch={canSearchUsers} single /> : null}
    <div className="form-row">
      <Field label="总额度" hint="留空表示不限量"><input name="totalLimit" min="1" type="number" placeholder="不限" /></Field>
      <Field label="每用户次数"><input name="perUserLimit" min="1" type="number" defaultValue="1" required /></Field>
    </div>
    <ValidityFields promotion={selected} from={window.from} until={window.until} onFrom={(from) => setWindow((current) => ({ ...current, from }))} onUntil={(until) => setWindow((current) => ({ ...current, until }))} />
    <details className="advanced-fields">
      <summary>高级：配额策略</summary>
      <div className="form-row"><Field label="配额策略" hint="高并发抢券建议分桶，需填写总额度"><select value={strategy} onChange={(event) => setStrategy(event.target.value)}><option value="SingleRow">单行（默认）</option><option value="Bucketed">分桶</option></select></Field>{strategy === 'Bucketed' ? <Field label="分桶数量"><input name="bucketCount" type="number" min="2" max="128" defaultValue="8" required /></Field> : <span />}</div>
    </details>
    {localError ? <p className="field-error" role="alert">{localError}</p> : null}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label="创建优惠券" onCancel={onCancel} />
  </form>;
}

/* ---------- Batch issuance ---------- */

export function CouponBatchForm({ promotions, canSearchUsers, initialPromotionId, busy, error, onSubmit, onCancel }: {
  promotions: PromotionDetail[];
  canSearchUsers: boolean;
  initialPromotionId?: string;
  busy: boolean;
  error: unknown;
  onSubmit: (body: unknown) => void;
  onCancel: () => void;
}) {
  const couponPromotions = usableCouponPromotions(promotions);
  const [promotionId, setPromotionId] = useState(initialPromotionId ?? (couponPromotions.length === 1 ? couponPromotions[0]!.id : ''));
  const selected = couponPromotions.find((item) => item.id === promotionId);
  const [window, setWindow] = useState(() => defaultWindow(selected));
  const [mode, setMode] = useState<'users' | 'codes'>('users');
  const [users, setUsers] = useState<string[]>([]);
  const [count, setCount] = useState('100');
  const [activate, setActivate] = useState(selected?.status === 'Active');
  const [perUser, setPerUser] = useState('1');
  const [localError, setLocalError] = useState('');

  if (!couponPromotions.length) return <NoCouponPromotion onCancel={onCancel} />;

  const choosePromotion = (id: string) => {
    const promotion = couponPromotions.find((item) => item.id === id);
    setPromotionId(id);
    setWindow(defaultWindow(promotion));
    setActivate(promotion?.status === 'Active');
  };
  const quantity = mode === 'users' ? users.length : Number(count) || 0;

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    if (quantity < 1 || quantity > MAX_BATCH) return setLocalError(`一次发放 1–${MAX_BATCH} 张。`);
    if (new Date(window.until) <= new Date(window.from)) return setLocalError('失效时间必须晚于生效时间。');
    setLocalError('');
    onSubmit({
      promotionId,
      count: mode === 'codes' ? quantity : 0,
      assignedUserIds: mode === 'users' ? users : null,
      codePrefix: value('codePrefix') || null,
      validFromUtc: new Date(window.from).toISOString(),
      validUntilUtc: new Date(window.until).toISOString(),
      totalLimitPerCoupon: value('totalLimit') ? Number(value('totalLimit')) : null,
      perUserLimit: Number(perUser || 1),
      activate,
    });
  };

  return <form className="modal-form" onSubmit={submit}>
    <Field label="促销活动" hint="仅显示「凭券使用」且未结束的活动">
      <select required value={promotionId} onChange={(event) => choosePromotion(event.target.value)}>
        <option value="">选择促销活动</option>
        {couponPromotions.map((item) => <option key={item.id} value={item.id}>{item.name} · {offerText(item.rules, item.benefits)} · {statusLabel(item.status)}</option>)}
      </select>
    </Field>
    <PromotionSummaryCard promotion={selected} />

    <div className="form-section-title"><span>01</span><div><strong>发放对象</strong><small>定向券进入用户「我的优惠券」；通用码需要自行分发</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="发放方式">
      <button type="button" role="radio" aria-checked={mode === 'users'} aria-selected={mode === 'users'} onClick={() => setMode('users')}>发给指定用户</button>
      <button type="button" role="radio" aria-checked={mode === 'codes'} aria-selected={mode === 'codes'} onClick={() => setMode('codes')}>生成通用券码</button>
    </div>
    {mode === 'users'
      ? <UserPicker value={users} onChange={setUsers} canSearch={canSearchUsers} />
      : <Field label="生成数量" hint={`一次最多 ${MAX_BATCH} 张，每张券码不同`}><input type="number" min={1} max={MAX_BATCH} value={count} onChange={(event) => setCount(event.target.value)} required /></Field>}

    <div className="form-section-title"><span>02</span><div><strong>券规则</strong><small>有效期必须落在活动周期内</small></div></div>
    <ValidityFields promotion={selected} from={window.from} until={window.until} onFrom={(from) => setWindow((current) => ({ ...current, from }))} onUntil={(until) => setWindow((current) => ({ ...current, until }))} />
    <div className="form-row">
      <Field label="每张券总次数" hint="留空表示不限"><input name="totalLimit" type="number" min={1} placeholder="不限" /></Field>
      <Field label="每用户次数"><input type="number" min={1} value={perUser} onChange={(event) => setPerUser(event.target.value)} required /></Field>
    </div>
    <div className="form-row">
      <Field label="券码前缀" hint="可选，字母数字或短横线"><input name="codePrefix" maxLength={20} pattern="[A-Za-z0-9\-]*" className="mono" placeholder="例如 VIP" /></Field>
      <Field label="发放后状态" hint={selected && selected.status !== 'Active' ? '活动未发布，只能先创建草稿券' : '启用后用户立即可用'}>
        <label className="switch-row"><input type="checkbox" checked={activate} disabled={!selected || selected.status !== 'Active'} onChange={(event) => setActivate(event.target.checked)} /><span />立即启用</label>
      </Field>
    </div>

    {selected && quantity > 0 ? <p className="notice notice--success issue-preview"><Icon name="check" /><span>
      将{mode === 'users' ? `为 ${quantity} 位用户各发 1 张` : `生成 ${quantity} 个通用券码`}「{offerText(selected.rules, selected.benefits)}」，
      有效期 {formatDate(new Date(window.from).toISOString())} 至 {formatDate(new Date(window.until).toISOString())}，每人可用 {perUser || 1} 次，{activate ? '发放后立即可用' : '以草稿创建'}。
    </span></p> : null}
    {localError ? <p className="field-error" role="alert">{localError}</p> : null}
    {error ? <ErrorNotice error={error} /> : null}
    <SubmitBar busy={busy} label={quantity ? `发放 ${quantity} 张` : '发放'} onCancel={onCancel} />
  </form>;
}

/* ---------- List ---------- */

function CouponUsageCell({ row, usage, error }: { row: Coupon; usage?: CouponUsage; error: boolean }) {
  if (!usage) return <>{error ? '—' : '…'}<small>{error ? '使用数据不可用' : '读取中'}</small></>;
  const used = usage.consumedCount + usage.reservedCount;
  const ratio = row.totalLimit ? Math.min(1, used / row.totalLimit) : 0;
  return <div className="coupon-usage">
    <strong>已核销 {usage.consumedCount}{usage.reservedCount ? <small> · 占用 {usage.reservedCount}</small> : null}</strong>
    {row.totalLimit ? <span className="coupon-usage__bar" title={`${used}/${row.totalLimit}`}><span style={{ width: `${ratio * 100}%` }} /></span> : null}
    <small>{usage.remainingQuota !== null ? `剩余 ${usage.remainingQuota}` : '不限量'}{usage.consumedDiscountTotal ? ` · 让利 ${formatMoney(usage.consumedDiscountTotal)}` : ''}</small>
  </div>;
}

/** Coupon table with row selection so draft batches can be enabled or stopped in one go. */
export function CouponList({ rows, canEdit, promotions, usage, usageError, onAction, onBulk }: {
  rows: Coupon[];
  canEdit: boolean;
  promotions: Map<string, PromotionDetail>;
  usage?: Map<string, CouponUsage>;
  usageError: boolean;
  onAction: (row: Coupon, action: string, label: string, options: CouponActionOptions) => void;
  onBulk: (action: CouponBulkAction, rows: Coupon[]) => Promise<void>;
}) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [running, setRunning] = useState(false);
  const selectable = useMemo(() => rows.filter((row) => canActivateCoupon(row) || canDisableCoupon(row)), [rows]);
  const chosen = rows.filter((row) => selected.has(row.id));
  const toggle = (id: string) => setSelected((current) => { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const allChecked = selectable.length > 0 && selectable.every((row) => selected.has(row.id));
  const runBulk = async (action: CouponBulkAction) => {
    const targets = chosen.filter(action === 'activate' ? canActivateCoupon : canDisableCoupon);
    setRunning(true);
    try {
      await onBulk(action, targets);
      setSelected(new Set());
    } finally {
      setRunning(false);
    }
  };
  const activatable = chosen.filter(canActivateCoupon).length;
  const disablable = chosen.filter(canDisableCoupon).length;

  return <>
    {canEdit && chosen.length ? <div className="bulk-bar" role="toolbar" aria-label="批量操作">
      <strong>已选 {chosen.length} 张</strong>
      <button type="button" className="button button--dark" disabled={!activatable || running} onClick={() => void runBulk('activate')}>启用 {activatable || ''}</button>
      <button type="button" className="button button--quiet" disabled={!disablable || running} onClick={() => void runBulk('disable')}>停用 {disablable || ''}</button>
      <button type="button" className="link-button" onClick={() => setSelected(new Set())}>取消选择</button>
      {running ? <span className="spinner" /> : null}
    </div> : null}
    <table><thead><tr>
      {canEdit ? <th className="select-cell"><input type="checkbox" aria-label="全选可操作的券" checked={allChecked} disabled={!selectable.length} onChange={() => setSelected(allChecked ? new Set() : new Set(selectable.map((row) => row.id)))} /></th> : null}
      <th>优惠券</th><th>促销活动</th><th>有效期</th><th>配额</th><th>使用情况</th><th>状态</th><th>操作</th>
    </tr></thead><tbody>{rows.map((row) => {
      const promotion = promotions.get(row.promotionId);
      const ended = couponEnded(row);
      const phase = windowPhase(row.validFromUtc, row.validUntilUtc);
      const rowSelectable = canActivateCoupon(row) || canDisableCoupon(row);
      return <tr key={row.id} className={selected.has(row.id) ? 'is-selected' : undefined}>
        {canEdit ? <td className="select-cell"><input type="checkbox" aria-label={`选择 ${row.maskedCode}`} checked={selected.has(row.id)} disabled={!rowSelectable} onChange={() => toggle(row.id)} /></td> : null}
        <td><strong className="mono">{row.maskedCode}</strong>{row.campaignId ? <span className="tag-inline" title="用户在领券活动中领取">领券</span> : null}<small title={row.assignedUserId ?? undefined}>{row.assignedUserId ? <>定向 {row.assignedUserId.slice(0, 8)}… <CopyButton value={row.assignedUserId} label="复制用户 ID" /></> : '通用券码'}</small></td>
        <td><strong>{promotion?.name ?? '未读取到活动'}</strong><small>{promotion ? offerText(promotion.rules, promotion.benefits) : row.promotionId}</small></td>
        <td>{formatDate(row.validFromUtc)}<small>至 {formatDate(row.validUntilUtc)}</small><span className={`phase phase--${phase.tone}`}>{phase.detail}</span></td>
        <td>{row.totalLimit ?? '不限'}<small>每人 {row.perUserLimit} 次{row.quotaStrategy === 'Bucketed' ? ' · 分桶' : ''}</small></td>
        <td><CouponUsageCell row={row} usage={usage?.get(row.id)} error={usageError} /></td>
        <td><Status>{row.status}</Status></td>
        <td><div className="row-actions">{canEdit && row.status !== 'Expired' ? <>
          {canActivateCoupon(row) ? <button className="row-action-primary" type="button" onClick={() => onAction(row, 'activate', '启用', { name: row.maskedCode, detail: '所属促销活动必须已发布且处于进行中。' })}>启用</button> : null}
          {canDisableCoupon(row) ? <button type="button" onClick={() => onAction(row, 'disable', '停用', { name: row.maskedCode, reason: true, danger: true, detail: '停用后用户无法再使用；已下单占用的额度不受影响。' })}>停用</button> : null}
          {ended ? <button className="danger-link" type="button" onClick={() => onAction(row, 'expire', '标记过期', { name: row.maskedCode, danger: true, detail: '有效期已结束，标记后不可再启用。' })}>标记过期</button> : null}
        </> : '—'}</div></td>
      </tr>;
    })}</tbody></table>
  </>;
}

/* ---------- One-time code reveal ---------- */

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
export function IssuedCouponsView({ coupons, onClose, footer }: { coupons: IssuedCoupon[]; onClose: () => void; footer?: ReactNode }) {
  const [saved, setSaved] = useState(false);
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
    {footer}
    <div className="submit-bar">
      <span onClick={() => setSaved(true)}><CopyButton value={allCodes} label={`复制全部 ${coupons.length} 个券码`} /></span>
      <button className="button button--quiet" type="button" onClick={() => { downloadCsv(coupons, `coupons-${new Date().toISOString().slice(0, 10)}.csv`); setSaved(true); }}><Icon name="download" />下载 CSV</button>
      <button className="button button--dark" type="button" onClick={onClose}>{saved ? '完成' : '我已保存'}</button>
    </div>
  </div>;
}
