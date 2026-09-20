import { useQuery } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api, query, type Paged } from '../../api';
import { asLocalInput, CopyButton, Empty, ErrorNotice, Field, formatDate, formatRelative, Icon, Loading, Sparkline, Status } from '../../ui';
import { PromotionSummaryCard } from './CouponTools';
import { benefitHeadline, conditionText, offerText, scopeText } from './promotion-text';
import type { PromotionDetail } from './PromotionForm';

export type Campaign = {
  id: string; promotionId: string; title: string; description?: string | null; status: string;
  claimStartsOnUtc: string; claimEndsOnUtc: string; totalQuantity: number | null; claimedQuantity: number;
  dailyQuantity: number | null; perUserLimit: number; perUserDailyLimit: number | null;
  validityMode: 'Fixed' | 'RelativeToClaim'; validFromUtc: string | null; validUntilUtc: string | null; validDays: number | null;
  usesPerCoupon: number; isListed: boolean; createdOnUtc: string; updatedOnUtc: string; createdBy: string; updatedBy: string; version: number;
};
type CampaignStats = { claimedQuantity: number; claimedToday: number; claimUsers: number; daily: Array<{ day: string; count: number }> };
type CampaignClaim = { id: string; userId: string; couponId: string; claimedOnUtc: string };
type ActionOptions = { name: string; reason?: boolean; danger?: boolean; detail?: string };
export type CampaignActionHandler = (row: Campaign, action: string, label: string, options: ActionOptions) => void;

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const storefrontBase = (import.meta.env.VITE_STOREFRONT_URL as string | undefined)?.replace(/\/$/, '') || 'http://localhost:5173';

/** Shareable storefront link; unlisted campaigns can only be claimed through it. */
export const campaignLink = (id: string) => `${storefrontBase}/coupon-center/${id}`;

const usableCouponPromotions = (promotions: PromotionDetail[]) =>
  promotions.filter((item) => item.triggerType === 'Coupon' && !['Ended', 'Archived'].includes(item.status) && new Date(item.endsOnUtc).getTime() > Date.now());

function claimPhase(row: Pick<Campaign, 'status' | 'claimStartsOnUtc' | 'claimEndsOnUtc' | 'totalQuantity' | 'claimedQuantity'>, now = Date.now()) {
  const start = new Date(row.claimStartsOnUtc).getTime();
  const end = new Date(row.claimEndsOnUtc).getTime();
  if (row.status === 'Ended' || now >= end) return { label: '已结束', tone: 'bad' as const };
  if (row.totalQuantity !== null && row.claimedQuantity >= row.totalQuantity) return { label: '已抢光', tone: 'bad' as const };
  if (now < start) return { label: start - now < DAY ? `${Math.ceil((start - now) / HOUR)} 小时后开抢` : `${formatDate(row.claimStartsOnUtc)} 开抢`, tone: 'warn' as const };
  const left = end - now;
  return { label: `领取中 · 还剩 ${left >= DAY ? `${Math.ceil(left / DAY)} 天` : `${Math.ceil(left / HOUR)} 小时`}`, tone: left < DAY ? 'warn' as const : 'good' as const };
}

function validityText(row: Pick<Campaign, 'validityMode' | 'validDays' | 'validFromUtc' | 'validUntilUtc'>) {
  return row.validityMode === 'RelativeToClaim'
    ? `领取后 ${row.validDays} 天内有效`
    : `${formatDate(row.validFromUtc)} 至 ${formatDate(row.validUntilUtc)}`;
}

function limitText(row: Pick<Campaign, 'perUserLimit' | 'perUserDailyLimit' | 'dailyQuantity' | 'usesPerCoupon'>) {
  return [
    `每人限领 ${row.perUserLimit} 张`,
    row.perUserDailyLimit ? `每天最多 ${row.perUserDailyLimit} 张` : '',
    row.dailyQuantity ? `每日发放 ${row.dailyQuantity} 张` : '',
    row.usesPerCoupon > 1 ? `每张可用 ${row.usesPerCoupon} 次` : '',
  ].filter(Boolean).join(' · ');
}

function QuantityBar({ claimed, total }: { claimed: number; total: number | null }) {
  const ratio = total ? Math.min(1, claimed / total) : 0;
  return <div className="quantity-bar">
    <span className="progress"><i style={{ width: `${total ? ratio * 100 : 0}%` }} /></span>
    <small>{total === null ? `已领 ${claimed} 张 · 不限量` : `${claimed} / ${total} · 剩余 ${Math.max(0, total - claimed)}`}</small>
  </div>;
}

/** What a shopper will see in the coupon center, rendered live while the operator edits. */
function ClaimCardPreview({ promotion, title, description, total, perUser, validity, claimStarts, listed }: {
  promotion?: PromotionDetail; title: string; description: string; total: number | null; perUser: number; validity: string; claimStarts: Date | null; listed: boolean;
}) {
  const upcoming = claimStarts && claimStarts.getTime() > Date.now();
  return <aside className="claim-preview" aria-label="前台领券卡片预览" aria-live="polite">
    <span className="claim-preview__tag">{listed ? '领券中心预览' : '仅链接可领 · 预览'}</span>
    <div className="claim-card">
      <div className="claim-card__value">
        <strong>{promotion ? benefitHeadline(promotion.benefits) : '—'}</strong>
        <small>{promotion ? conditionText(promotion.rules) : '选择活动后显示'}</small>
      </div>
      <div className="claim-card__body">
        <strong>{title.trim() || '领券活动标题'}</strong>
        <small>{description.trim() || (promotion ? offerText(promotion.rules, promotion.benefits) === title.trim() ? promotion.name : offerText(promotion.rules, promotion.benefits) : '副标题（可选）')}</small>
        <small>{validity} · 每人限领 {perUser || 1} 张</small>
        {total !== null ? <span className="progress"><i style={{ width: '0%' }} /></span> : null}
        <small>{total !== null ? `限量 ${total.toLocaleString('zh-CN')} 张` : '数量不限'}</small>
      </div>
      <span className="claim-card__button">{upcoming ? `${claimStarts!.getMonth() + 1}月${claimStarts!.getDate()}日 ${String(claimStarts!.getHours()).padStart(2, '0')}:${String(claimStarts!.getMinutes()).padStart(2, '0')} 开抢` : '立即领取'}</span>
    </div>
  </aside>;
}

/* ---------- Form ---------- */

type Draft = {
  promotionId: string; title: string; titleTouched: boolean; description: string;
  limited: boolean; total: string; dailyCapped: boolean; daily: string;
  perUser: string; perUserDailyCapped: boolean; perUserDaily: string; uses: string;
  startMode: 'now' | 'scheduled'; claimStarts: string; claimEnds: string;
  validityMode: 'RelativeToClaim' | 'Fixed'; validDays: string; validFrom: string; validUntil: string;
  isListed: boolean;
};

const nextAt = (hour: number, dayOffset = 0) => {
  const date = new Date();
  date.setDate(date.getDate() + dayOffset);
  date.setHours(hour, 0, 0, 0);
  if (dayOffset === 0 && date.getTime() <= Date.now()) date.setDate(date.getDate() + 1);
  return date;
};
const local = (value?: string | null) => value ? asLocalInput(new Date(value)) : '';
const toIso = (value: string) => new Date(value).toISOString();
const num = (value: string) => value.trim() === '' ? NaN : Number(value);

function initialDraft(promotions: PromotionDetail[], source?: Campaign, duplicate = false, initialPromotionId?: string): Draft {
  const promotion = promotions.find((item) => item.id === (source?.promotionId ?? initialPromotionId)) ?? (promotions.length === 1 ? promotions[0] : undefined);
  const end = promotion ? new Date(Math.min(new Date(promotion.endsOnUtc).getTime(), Date.now() + 7 * DAY)) : new Date(Date.now() + 7 * DAY);
  const reuseWindow = source && !duplicate;
  return {
    promotionId: promotion?.id ?? '',
    title: source ? `${source.title}${duplicate ? '（副本）' : ''}` : promotion ? offerText(promotion.rules, promotion.benefits) : '',
    titleTouched: Boolean(source),
    description: source?.description ?? '',
    limited: source ? source.totalQuantity !== null : true,
    total: String(source?.totalQuantity ?? 1000),
    dailyCapped: Boolean(source?.dailyQuantity),
    daily: String(source?.dailyQuantity ?? 200),
    perUser: String(source?.perUserLimit ?? 1),
    perUserDailyCapped: Boolean(source?.perUserDailyLimit),
    perUserDaily: String(source?.perUserDailyLimit ?? 1),
    uses: String(source?.usesPerCoupon ?? 1),
    startMode: reuseWindow && new Date(source.claimStartsOnUtc).getTime() > Date.now() ? 'scheduled' : 'now',
    claimStarts: reuseWindow ? local(source.claimStartsOnUtc) : asLocalInput(nextAt(20)),
    claimEnds: reuseWindow ? local(source.claimEndsOnUtc) : asLocalInput(end),
    validityMode: source?.validityMode ?? 'RelativeToClaim',
    validDays: String(source?.validDays ?? 7),
    validFrom: source?.validFromUtc ? local(source.validFromUtc) : promotion ? asLocalInput(new Date(Math.max(Date.now(), new Date(promotion.startsOnUtc).getTime()))) : asLocalInput(new Date()),
    validUntil: source?.validUntilUtc ? local(source.validUntilUtc) : promotion ? local(promotion.endsOnUtc) : asLocalInput(end),
    isListed: source?.isListed ?? true,
  };
}

function toBody(draft: Draft, claimStartsNow: Date) {
  const fixed = draft.validityMode === 'Fixed';
  return {
    promotionId: draft.promotionId,
    definition: {
      title: draft.title.trim(),
      description: draft.description.trim() || null,
      claimStartsOnUtc: draft.startMode === 'now' ? claimStartsNow.toISOString() : toIso(draft.claimStarts),
      claimEndsOnUtc: toIso(draft.claimEnds),
      totalQuantity: draft.limited ? num(draft.total) : null,
      dailyQuantity: draft.dailyCapped ? num(draft.daily) : null,
      perUserLimit: num(draft.perUser),
      perUserDailyLimit: draft.perUserDailyCapped ? num(draft.perUserDaily) : null,
      validityMode: draft.validityMode,
      validFromUtc: fixed ? toIso(draft.validFrom) : null,
      validUntilUtc: fixed ? toIso(draft.validUntil) : null,
      validDays: fixed ? null : num(draft.validDays),
      usesPerCoupon: num(draft.uses),
      isListed: draft.isListed,
    },
  };
}

function validate(draft: Draft, promotion: PromotionDetail | undefined) {
  const errors: string[] = [];
  const warnings: string[] = [];
  const whole = (value: number, min: number, max: number) => Number.isInteger(value) && value >= min && value <= max;
  const total = draft.limited ? num(draft.total) : null;
  const perUser = num(draft.perUser);
  const start = draft.startMode === 'now' ? Date.now() : new Date(draft.claimStarts).getTime();
  const end = new Date(draft.claimEnds).getTime();
  const promotionEnd = promotion ? new Date(promotion.endsOnUtc).getTime() : NaN;

  if (!promotion) errors.push('请选择凭券促销活动。');
  if (!draft.title.trim()) errors.push('请填写标题。');
  if (total !== null && !whole(total, 1, 10_000_000)) errors.push('发放总量需为 1–10,000,000 的整数。');
  if (draft.dailyCapped && (!whole(num(draft.daily), 1, 10_000_000) || (total !== null && num(draft.daily) > total))) errors.push('每日限量需为正整数且不超过总量。');
  if (!whole(perUser, 1, 100)) errors.push('每人限领需为 1–100 的整数。');
  else if (total !== null && perUser > total) errors.push('每人限领不能超过发放总量。');
  if (draft.perUserDailyCapped && (!whole(num(draft.perUserDaily), 1, 100) || num(draft.perUserDaily) > perUser)) errors.push('每人每天限领需为正整数且不超过每人限领。');
  if (!whole(num(draft.uses), 1, 100)) errors.push('每张券可用次数需为 1–100 的整数。');
  if (Number.isNaN(start) || Number.isNaN(end)) errors.push('请填写领取时间。');
  else if (end <= start) errors.push('领取截止时间必须晚于开始时间。');
  else if (end <= Date.now()) errors.push('领取截止时间已过。');
  if (promotion && end > promotionEnd) errors.push(`领取截止不能晚于促销结束（${formatDate(promotion.endsOnUtc)}）。`);
  if (draft.validityMode === 'RelativeToClaim') {
    if (!whole(num(draft.validDays), 1, 365)) errors.push('领取后有效天数需为 1–365 的整数。');
  } else {
    const from = new Date(draft.validFrom).getTime();
    const until = new Date(draft.validUntil).getTime();
    if (Number.isNaN(from) || Number.isNaN(until) || until <= from) errors.push('券的失效时间必须晚于生效时间。');
    else if (promotion && (from < new Date(promotion.startsOnUtc).getTime() || until > promotionEnd)) errors.push('固定有效期必须在促销活动时间内。');
    else if (until <= start) errors.push('券在开始领取前就已失效。');
    else if (until < end) warnings.push('券的失效时间早于领取截止，领取窗口后段领到的券可用时间很短。');
  }

  if (promotion && promotion.status !== 'Active') warnings.push('促销活动尚未发布：可以先保存草稿，促销发布后才能开放领取。');
  if (draft.validityMode === 'RelativeToClaim' && promotion && whole(num(draft.validDays), 1, 365) && end + num(draft.validDays) * DAY > promotionEnd)
    warnings.push('临近促销结束时领取的券，有效期会被截断到促销结束。');
  if (draft.startMode === 'scheduled' && start < Date.now()) warnings.push('开抢时间已过，发布后会立即开放领取。');
  if (total === null && !draft.dailyCapped) warnings.push('不限量且没有每日上限，请确认优惠成本可控。');
  return { errors, warnings };
}

export function CampaignForm({ item, duplicateOf, promotions, canPublish, initialPromotionId, busy, error, onSubmit, onCancel }: {
  item?: Campaign;
  duplicateOf?: Campaign;
  promotions: PromotionDetail[];
  canPublish: boolean;
  initialPromotionId?: string;
  busy: boolean;
  error?: ReactNode;
  onSubmit: (body: ReturnType<typeof toBody>, publish: boolean) => void;
  onCancel: () => void;
}) {
  const editing = Boolean(item);
  const options = usableCouponPromotions(promotions);
  const [draft, setDraft] = useState<Draft>(() => initialDraft(promotions, item ?? duplicateOf, Boolean(duplicateOf), initialPromotionId));
  const [touched, setTouched] = useState(false);
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const promotion = promotions.find((entry) => entry.id === draft.promotionId);
  const { errors, warnings } = useMemo(() => validate(draft, promotion), [draft, promotion]);
  const promotionEnd = promotion ? new Date(promotion.endsOnUtc) : undefined;
  const total = draft.limited && num(draft.total) > 0 ? num(draft.total) : null;
  const claimStarts = draft.startMode === 'now' ? null : new Date(draft.claimStarts);
  const validity = draft.validityMode === 'RelativeToClaim'
    ? `领取后 ${num(draft.validDays) || '—'} 天内有效`
    : `${draft.validFrom ? formatDate(toIso(draft.validFrom)) : '—'} 至 ${draft.validUntil ? formatDate(toIso(draft.validUntil)) : '—'}`;

  if (!options.length && !editing) return <div className="modal-form">
    <Empty title="还没有可投放的凭券活动" detail="领券活动需要挂在未结束的「凭券使用」促销下。请先到「促销」用「优惠券活动」模板创建一个。" />
    <div className="submit-bar"><button className="button button--quiet" type="button" onClick={onCancel}>知道了</button></div>
  </div>;

  const choosePromotion = (id: string) => {
    const next = promotions.find((entry) => entry.id === id);
    setDraft((current) => ({
      ...current,
      promotionId: id,
      title: current.titleTouched || !next ? current.title : offerText(next.rules, next.benefits),
      claimEnds: next && new Date(current.claimEnds).getTime() > new Date(next.endsOnUtc).getTime() ? local(next.endsOnUtc) : current.claimEnds,
      validUntil: next ? local(next.endsOnUtc) : current.validUntil,
    }));
  };
  const claimEndIn = (days: number | 'promotion') => {
    const base = draft.startMode === 'now' ? Date.now() : new Date(draft.claimStarts).getTime();
    const target = days === 'promotion' ? promotionEnd!.getTime() : Math.min(base + days * DAY, promotionEnd?.getTime() ?? Infinity);
    set('claimEnds', asLocalInput(new Date(target)));
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setTouched(true);
    if (errors.length) return;
    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    onSubmit(toBody(draft, new Date()), submitter?.value === 'publish');
  };

  const quantityPresets = [100, 500, 1000, 5000, 10000];
  return <form className="modal-form campaign-editor" onSubmit={submit} noValidate>
    <ClaimCardPreview promotion={promotion} title={draft.title} description={draft.description} total={total} perUser={num(draft.perUser)} validity={validity} claimStarts={claimStarts} listed={draft.isListed} />

    <div className="form-section-title"><span>01</span><div><strong>发哪张券</strong><small>优惠内容与适用范围来自凭券促销活动</small></div></div>
    <Field label="凭券促销活动" hint={editing ? '草稿阶段可更换活动' : '仅显示「凭券使用」且未结束的活动'}>
      <select value={draft.promotionId} onChange={(event) => choosePromotion(event.target.value)} required>
        <option value="">选择促销活动</option>
        {options.map((entry) => <option key={entry.id} value={entry.id}>{entry.name} · {offerText(entry.rules, entry.benefits)}{entry.status !== 'Active' ? '（未发布）' : ''}</option>)}
      </select>
    </Field>
    <PromotionSummaryCard promotion={promotion} />
    <div className="form-row">
      <Field label="标题" hint="显示在领券中心卡片上">
        <div className="input-group">
          <input value={draft.title} onChange={(event) => setDraft((current) => ({ ...current, title: event.target.value, titleTouched: true }))} maxLength={60} required placeholder="例如 新人专享 满 99 减 20" />
          {promotion ? <button className="button button--quiet" type="button" title="使用优惠内容作为标题" onClick={() => setDraft((current) => ({ ...current, title: offerText(promotion.rules, promotion.benefits), titleTouched: false }))}><Icon name="refresh" /></button> : null}
        </div>
      </Field>
      <Field label="副标题（可选）" hint={`${draft.description.length} / 200`}><input value={draft.description} onChange={(event) => set('description', event.target.value)} maxLength={200} placeholder="例如 限量发放，先到先得" /></Field>
    </div>

    <div className="form-section-title"><span>02</span><div><strong>发放数量</strong><small>发完即止；上线后仍可追加或缩减库存</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="发放数量">
      {([[true, '限量发放'], [false, '不限量']] as const).map(([value, label]) => <button key={label} type="button" role="radio" aria-checked={draft.limited === value} aria-selected={draft.limited === value} onClick={() => set('limited', value)}>{label}</button>)}
    </div>
    {draft.limited ? <>
      <Field label="发放总量（张）"><input type="number" min={1} step={1} value={draft.total} onChange={(event) => set('total', event.target.value)} /></Field>
      <div className="preset-row"><span>常用</span>{quantityPresets.map((value) => <button key={value} type="button" onClick={() => set('total', String(value))}>{value.toLocaleString('zh-CN')}</button>)}</div>
    </> : null}
    <div className="toggle-field">
      <label className="switch-row"><input type="checkbox" checked={draft.dailyCapped} onChange={(event) => set('dailyCapped', event.target.checked)} /><span />每日限量</label>
      {draft.dailyCapped ? <Field label="每天最多发放（张）" hint="每天 0 点（北京时间）重置，适合分天放券"><input type="number" min={1} step={1} value={draft.daily} onChange={(event) => set('daily', event.target.value)} /></Field> : <small>开启后每天发完即止，第二天继续放券</small>}
    </div>

    <div className="form-section-title"><span>03</span><div><strong>领取规则</strong><small>限制每个用户能领几张</small></div></div>
    <div className="form-row">
      <Field label="每人限领（张）">
        <input type="number" min={1} max={100} step={1} value={draft.perUser} onChange={(event) => set('perUser', event.target.value)} />
      </Field>
      <div className="toggle-field">
        <label className="switch-row"><input type="checkbox" checked={draft.perUserDailyCapped} onChange={(event) => set('perUserDailyCapped', event.target.checked)} /><span />每人每天限领</label>
        {draft.perUserDailyCapped ? <input type="number" min={1} max={100} step={1} aria-label="每人每天限领张数" value={draft.perUserDaily} onChange={(event) => set('perUserDaily', event.target.value)} /> : <small>例如每人共 5 张、每天最多 1 张</small>}
      </div>
    </div>
    <div className="preset-row"><span>每人限领</span>{[1, 2, 3, 5].map((value) => <button key={value} type="button" onClick={() => set('perUser', String(value))}>{value} 张</button>)}</div>
    <details className="advanced-fields" open={draft.uses !== '1'}>
      <summary>高级：每张券可使用次数</summary>
      <Field label="每张券可用次数" hint="默认 1 次；大于 1 适合「本月 3 次免邮」这类券"><input type="number" min={1} max={100} step={1} value={draft.uses} onChange={(event) => set('uses', event.target.value)} /></Field>
    </details>

    <div className="form-section-title"><span>04</span><div><strong>领取时间</strong><small>{promotion ? `领取截止不能晚于促销结束（${formatDate(promotion.endsOnUtc)}）` : '先选择促销活动'}</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="开始方式">
      {([['now', '发布后立即可领'], ['scheduled', '定时开抢']] as const).map(([value, label]) => <button key={value} type="button" role="radio" aria-checked={draft.startMode === value} aria-selected={draft.startMode === value} onClick={() => set('startMode', value)}>{label}</button>)}
    </div>
    {draft.startMode === 'scheduled' ? <>
      <Field label="开抢时间"><input type="datetime-local" value={draft.claimStarts} onChange={(event) => set('claimStarts', event.target.value)} /></Field>
      <div className="preset-row"><span>快捷</span>
        <button type="button" onClick={() => set('claimStarts', asLocalInput(nextAt(10)))}>10:00</button>
        <button type="button" onClick={() => set('claimStarts', asLocalInput(nextAt(20)))}>20:00</button>
        <button type="button" onClick={() => set('claimStarts', asLocalInput(nextAt(0, 1)))}>明天 0 点</button>
        <button type="button" onClick={() => set('claimStarts', asLocalInput(nextAt(10, 1)))}>明天 10:00</button>
      </div>
    </> : null}
    <Field label="领取截止"><input type="datetime-local" value={draft.claimEnds} max={promotionEnd ? asLocalInput(promotionEnd) : undefined} onChange={(event) => set('claimEnds', event.target.value)} /></Field>
    <div className="preset-row"><span>持续</span>
      {[1, 3, 7, 14].map((days) => <button key={days} type="button" onClick={() => claimEndIn(days)}>{days} 天</button>)}
      {promotionEnd ? <button type="button" onClick={() => claimEndIn('promotion')}>与促销同时结束</button> : null}
    </div>

    <div className="form-section-title"><span>05</span><div><strong>券有效期</strong><small>领到的券多久内可以使用</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="有效期">
      {([['RelativeToClaim', '领取后 N 天'], ['Fixed', '固定时段']] as const).map(([value, label]) => <button key={value} type="button" role="radio" aria-checked={draft.validityMode === value} aria-selected={draft.validityMode === value} onClick={() => set('validityMode', value)}>{label}</button>)}
    </div>
    {draft.validityMode === 'RelativeToClaim' ? <>
      <Field label="领取后有效天数" hint="从领取时刻起算，最晚到促销结束"><input type="number" min={1} max={365} step={1} value={draft.validDays} onChange={(event) => set('validDays', event.target.value)} /></Field>
      <div className="preset-row"><span>常用</span>{[1, 3, 7, 15, 30].map((days) => <button key={days} type="button" onClick={() => set('validDays', String(days))}>{days} 天</button>)}</div>
    </> : <>
      <div className="form-row">
        <Field label="生效时间"><input type="datetime-local" value={draft.validFrom} min={promotion ? local(promotion.startsOnUtc) : undefined} onChange={(event) => set('validFrom', event.target.value)} /></Field>
        <Field label="失效时间"><input type="datetime-local" value={draft.validUntil} max={promotionEnd ? asLocalInput(promotionEnd) : undefined} onChange={(event) => set('validUntil', event.target.value)} /></Field>
      </div>
      {promotion ? <div className="preset-row"><span>快捷</span>
        <button type="button" onClick={() => setDraft((current) => ({ ...current, validFrom: asLocalInput(new Date(Math.max(Date.now(), new Date(promotion.startsOnUtc).getTime()))), validUntil: local(promotion.endsOnUtc) }))}>与促销同步</button>
        <button type="button" onClick={() => setDraft((current) => ({ ...current, validFrom: current.startMode === 'now' ? asLocalInput(new Date()) : current.claimStarts, validUntil: current.claimEnds }))}>与领取时间一致</button>
      </div> : null}
    </>}

    <div className="form-section-title"><span>06</span><div><strong>投放渠道</strong><small>决定用户从哪里看到这张券</small></div></div>
    <div className="choice-cards" role="radiogroup" aria-label="投放渠道">
      <button type="button" role="radio" aria-checked={draft.isListed} className={draft.isListed ? 'is-active' : undefined} onClick={() => set('isListed', true)}>
        <Icon name="ticket" /><strong>领券中心公开展示</strong><small>所有用户可在领券中心、商品详情页看到并领取</small>
      </button>
      <button type="button" role="radio" aria-checked={!draft.isListed} className={!draft.isListed ? 'is-active' : undefined} onClick={() => set('isListed', false)}>
        <Icon name="link" /><strong>仅凭链接领取</strong><small>不公开展示，把领取链接发到社群、短信或推送里</small>
      </button>
    </div>

    <div className="rule-summary">
      <strong>规则摘要</strong>
      <ul>
        <li>{total === null ? '不限量发放' : `共发放 ${total.toLocaleString('zh-CN')} 张，发完即止`}{draft.dailyCapped ? `，每天最多 ${draft.daily} 张` : ''}</li>
        <li>每人限领 {draft.perUser || '—'} 张{draft.perUserDailyCapped ? `，每天最多 ${draft.perUserDaily} 张` : ''}{draft.uses !== '1' ? `，每张可用 ${draft.uses} 次` : ''}</li>
        <li>{draft.startMode === 'now' ? '发布后立即可领' : `${draft.claimStarts ? formatDate(toIso(draft.claimStarts)) : '—'} 开抢`}，{draft.claimEnds ? formatDate(toIso(draft.claimEnds)) : '—'} 截止</li>
        <li>{validity}</li>
      </ul>
    </div>

    {warnings.length ? <div className="notice notice--warning" role="status"><Icon name="alert" /><span>{warnings.join(' ')}</span></div> : null}
    {touched && errors.length ? <div className="notice notice--error" role="alert"><Icon name="alert" /><span>{errors.join(' ')}</span></div> : null}
    {error}
    <div className="submit-bar">
      <button className="button button--quiet" type="button" onClick={onCancel}>取消</button>
      <button className={`button ${canPublish && !editing ? 'button--quiet' : 'button--dark'}`} type="submit" value="draft" disabled={busy}>{busy ? <span className="spinner" /> : null}{editing ? '保存草稿' : duplicateOf ? '创建副本' : '保存为草稿'}</button>
      {canPublish && !editing ? <button className="button button--dark" type="submit" value="publish" disabled={busy || promotion?.status !== 'Active'} title={promotion?.status !== 'Active' ? '促销发布后才能开放领取' : undefined}>创建并发布</button> : null}
    </div>
  </form>;
}

/* ---------- List ---------- */

export function campaignActions(row: Campaign) {
  const actions: Array<{ action: string; label: string; options: ActionOptions; primary?: boolean }> = [];
  if (row.status === 'Draft') actions.push({ action: 'publish', label: '发布', primary: true, options: { name: row.title, detail: '发布后按领取时间开放领取；发布后规则不可再修改，库存仍可调整。' } });
  if (row.status === 'Active') actions.push({ action: 'pause', label: '暂停', options: { name: row.title, detail: '暂停期间用户无法领取，已领的券不受影响。' } });
  if (row.status === 'Paused') actions.push({ action: 'resume', label: '恢复', primary: true, options: { name: row.title } });
  if (row.status === 'Active' || row.status === 'Paused') actions.push({ action: 'end', label: '结束', options: { name: row.title, danger: true, detail: '结束后不能再领取，也不能恢复；已领的券仍可在有效期内使用。' } });
  return actions;
}

export function CampaignTable({ rows, canEdit, promotions, onOpen, onAction, onCopyLink }: {
  rows: Campaign[];
  canEdit: boolean;
  promotions: Map<string, PromotionDetail>;
  onOpen: (id: string) => void;
  onAction: CampaignActionHandler;
  onCopyLink: (row: Campaign) => void;
}) {
  return <table><thead><tr><th>领券活动</th><th>发放进度</th><th>领取规则</th><th>领取时间</th><th>状态</th><th>操作</th></tr></thead><tbody>{rows.map((row) => {
    const promotion = promotions.get(row.promotionId);
    const phase = claimPhase(row);
    const actions = canEdit ? campaignActions(row) : [];
    return <tr key={row.id} className="is-clickable" onClick={() => onOpen(row.id)}>
      <td><strong>{row.title}</strong><small>{promotion ? `${promotion.name} · ${offerText(promotion.rules, promotion.benefits)}` : row.promotionId.slice(0, 8)}</small>{!row.isListed ? <small className="tag-inline">仅链接</small> : null}</td>
      <td><QuantityBar claimed={row.claimedQuantity} total={row.totalQuantity} /></td>
      <td>{limitText(row)}<small>{validityText(row)}</small></td>
      <td>{formatDate(row.claimStartsOnUtc)}<small>至 {formatDate(row.claimEndsOnUtc)}</small>{row.status === 'Active' ? <span className={`phase phase--${phase.tone}`}>{phase.label}</span> : null}</td>
      <td><Status>{row.status}</Status></td>
      <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
        <button type="button" onClick={() => onOpen(row.id)}>{row.status === 'Draft' && canEdit ? '编辑' : '详情'}</button>
        {actions.slice(0, 1).map((item) => <button key={item.action} type="button" className={item.primary ? 'row-action-primary' : item.options.danger ? 'danger-link' : undefined} onClick={() => onAction(row, item.action, item.label, item.options)}>{item.label}</button>)}
        {row.status !== 'Draft' && row.status !== 'Ended' ? <button type="button" onClick={() => onCopyLink(row)}>复制链接</button> : null}
      </div></td>
    </tr>;
  })}</tbody></table>;
}

/* ---------- Detail ---------- */

export function CampaignDetailView({ campaign, promotion, categoryNames, canEdit, canCreate, onAction, onChangeQuantity, onDuplicate }: {
  campaign: Campaign;
  promotion?: PromotionDetail;
  categoryNames: Map<string, string>;
  canEdit: boolean;
  canCreate: boolean;
  onAction: CampaignActionHandler;
  onChangeQuantity: () => void;
  onDuplicate: () => void;
}) {
  const [claimsTake, setClaimsTake] = useState(10);
  const stats = useQuery({
    queryKey: ['catalog', 'campaign-stats', campaign.id, campaign.claimedQuantity],
    queryFn: () => api<CampaignStats>(`/api/catalog/admin/coupon-campaigns/${campaign.id}/stats?days=14`),
    refetchInterval: campaign.status === 'Active' ? 15_000 : false,
  });
  const claims = useQuery({
    queryKey: ['catalog', 'campaign-claims', campaign.id, claimsTake, stats.data?.claimedQuantity],
    queryFn: () => api<Paged<CampaignClaim>>(`/api/catalog/admin/coupon-campaigns/${campaign.id}/claims?${query({ skip: 0, take: claimsTake })}`),
  });
  const phase = claimPhase({ ...campaign, claimedQuantity: stats.data?.claimedQuantity ?? campaign.claimedQuantity });
  const actions = canEdit ? campaignActions(campaign) : [];
  const claimed = stats.data?.claimedQuantity ?? campaign.claimedQuantity;
  const link = campaignLink(campaign.id);

  return <div className="order-detail campaign-detail">
    <div className="promotion-hero">
      <span className="promotion-preview__badge">{promotion ? benefitHeadline(promotion.benefits) : '券'}</span>
      <div>
        <strong>{campaign.title}</strong>
        <small>{promotion ? `${offerText(promotion.rules, promotion.benefits)} · ${scopeText(promotion.targets, categoryNames)}` : '促销活动读取中'} · <span className={`phase phase--${campaign.status === 'Active' ? phase.tone : 'bad'}`}>{campaign.status === 'Active' ? phase.label : campaign.status === 'Draft' ? '草稿' : campaign.status === 'Paused' ? '已暂停' : '已结束'}</span></small>
      </div>
    </div>

    {actions.length || canCreate ? <div className="detail-actions">
      {actions.map((item) => <button key={item.action} type="button" className={`button ${item.primary ? 'button--dark' : item.options.danger ? 'button--danger-quiet' : 'button--quiet'}`} onClick={() => onAction(campaign, item.action, item.label, item.options)}>{item.label}</button>)}
      {canEdit && campaign.status !== 'Ended' && campaign.status !== 'Draft' ? <button type="button" className="button button--quiet" onClick={onChangeQuantity}><Icon name="plus" />调整库存</button> : null}
      {canCreate ? <button type="button" className="button button--quiet" onClick={onDuplicate}><Icon name="copy" />复制为新活动</button> : null}
    </div> : null}

    <div className="campaign-kpis">
      <article><span>已领取</span><strong>{claimed.toLocaleString('zh-CN')}</strong><QuantityBar claimed={claimed} total={campaign.totalQuantity} /></article>
      <article><span>今日领取</span><strong>{stats.data?.claimedToday ?? '—'}</strong><small>{campaign.dailyQuantity ? `每日上限 ${campaign.dailyQuantity}` : '不限每日'}</small></article>
      <article><span>领取人数</span><strong>{stats.data?.claimUsers ?? '—'}</strong><small>{stats.data && stats.data.claimUsers ? `人均 ${(claimed / stats.data.claimUsers).toFixed(1)} 张` : '暂无领取'}</small></article>
      <article className="campaign-kpis__trend"><span>近 14 天</span>{stats.data ? <Sparkline values={stats.data.daily.map((item) => item.count)} label="近 14 天每日领取量" /> : null}<small>{stats.data ? `峰值 ${Math.max(0, ...stats.data.daily.map((item) => item.count))} 张/天` : '读取中'}</small></article>
    </div>
    {stats.isError ? <ErrorNotice error={stats.error} onRetry={() => void stats.refetch()} /> : null}

    <section><h3>领取规则</h3><div className="compact-list">
      <article><div><strong>{campaign.totalQuantity === null ? '不限量发放' : `共 ${campaign.totalQuantity.toLocaleString('zh-CN')} 张，发完即止`}</strong><small>{campaign.dailyQuantity ? `每天最多发放 ${campaign.dailyQuantity} 张（北京时间 0 点重置）` : '不限每日发放量'}</small></div></article>
      <article><div><strong>{limitText(campaign)}</strong><small>按用户账号计数</small></div></article>
      <article><div><strong>{formatDate(campaign.claimStartsOnUtc)} 至 {formatDate(campaign.claimEndsOnUtc)}</strong><small>领取时间</small></div></article>
      <article><div><strong>{validityText(campaign)}</strong><small>领到的券进入用户「我的优惠券」，结算时直接勾选</small></div></article>
    </div></section>

    <section><h3>投放渠道</h3><div className="campaign-link">
      <p>{campaign.isListed ? '在领券中心和商品详情页公开展示，也可以通过链接直达。' : '不公开展示，仅持有链接的用户可以领取。'}</p>
      <div className="input-group"><input value={link} readOnly className="mono" aria-label="领取链接" onFocus={(event) => event.currentTarget.select()} /><CopyButton value={link} label="复制链接" /></div>
    </div></section>

    <section><h3>最近领取</h3>
      {claims.isPending ? <Loading label="读取领取记录" /> : claims.data?.items.length ? <div className="data-table"><table><thead><tr><th>用户</th><th>领取时间</th><th>券</th></tr></thead><tbody>
        {claims.data.items.map((claim) => <tr key={claim.id}><td className="mono">{claim.userId}</td><td title={formatDate(claim.claimedOnUtc)}>{formatRelative(claim.claimedOnUtc)}</td><td className="mono">{claim.couponId.slice(0, 8)}</td></tr>)}
      </tbody></table></div> : <p className="muted-copy">{campaign.status === 'Draft' ? '发布后这里会实时显示领取记录。' : '还没有人领取。'}</p>}
      {claims.data && claims.data.total > claims.data.items.length ? <button type="button" className="link-button" onClick={() => setClaimsTake((value) => value + 20)}>再看 20 条（共 {claims.data.total} 条）</button> : null}
    </section>
    <p className="muted-copy"><Icon name="alert" />发布后规则不可修改，库存可随时追加；结束后已领取的券仍可在有效期内使用。<CopyButton value={campaign.id} label="复制活动 ID" /></p>
  </div>;
}
