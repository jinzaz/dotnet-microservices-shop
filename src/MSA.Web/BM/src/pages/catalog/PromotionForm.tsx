import { useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { SkuPicker } from '../../pickers';
import { useSession } from '../../session';
import { asLocalInput, Field, Icon, SubmitBar } from '../../ui';
import { durationText, offerText, scopeText } from './promotion-text';

export type PromotionComponent = { ruleType?: string; benefitType?: string; schemaVersion: number; configurationJson: string; sequence: number };
export type PromotionTarget = { targetType: string; targetKey?: string | null; isExcluded: boolean };
export type PromotionDetail = {
  id: string; code: string; name: string; triggerType: string; status: string; startsOnUtc: string; endsOnUtc: string;
  priority: number; stackPolicy: string; exclusiveGroup?: string | null; revision: number; version: number;
  rules: PromotionComponent[]; benefits: PromotionComponent[]; targets: PromotionTarget[];
};
export type CategoryOption = { id: string; name: string; code: string; status: string };

type TargetScope = 'All' | 'Category' | 'Sku';
type BenefitType = 'PercentageOff' | 'FixedAmountOff';

type Draft = {
  code: string; name: string; triggerType: string; stackPolicy: string; startsOn: string; endsOn: string;
  priority: string; exclusiveGroup: string; minimumAmount: string; minimumQuantity: string;
  benefitType: BenefitType; benefitValue: string; maximumDiscount: string;
  scope: TargetScope; categoryIds: string[]; skus: string;
};

const templates: Array<{ id: string; label: string; hint: string; apply: Partial<Draft> }> = [
  { id: 'threshold', label: '满额立减', hint: '满 ¥199 减 ¥30', apply: { triggerType: 'Automatic', minimumAmount: '199', minimumQuantity: '', benefitType: 'FixedAmountOff', benefitValue: '30', maximumDiscount: '' } },
  { id: 'quantity', label: '多件折扣', hint: '满 2 件 9 折，最高减 ¥50', apply: { triggerType: 'Automatic', minimumAmount: '', minimumQuantity: '2', benefitType: 'PercentageOff', benefitValue: '10', maximumDiscount: '50' } },
  { id: 'instant', label: '无门槛立减', hint: '立减 ¥10', apply: { triggerType: 'Automatic', minimumAmount: '', minimumQuantity: '', benefitType: 'FixedAmountOff', benefitValue: '10', maximumDiscount: '' } },
  { id: 'coupon', label: '优惠券活动', hint: '凭券满 ¥99 减 ¥20', apply: { triggerType: 'Coupon', minimumAmount: '99', minimumQuantity: '', benefitType: 'FixedAmountOff', benefitValue: '20', maximumDiscount: '' } },
];

const durations = [
  { label: '3 天', days: 3 },
  { label: '7 天', days: 7 },
  { label: '14 天', days: 14 },
  { label: '30 天', days: 30 },
];

function readNumber(json: string | undefined, property: string) {
  try {
    const value = (JSON.parse(json ?? '{}') as Record<string, unknown>)[property];
    return typeof value === 'number' ? String(value) : '';
  } catch {
    return '';
  }
}

function suggestCode() {
  const date = new Date();
  const stamp = `${String(date.getFullYear()).slice(2)}${String(date.getMonth() + 1).padStart(2, '0')}${String(date.getDate()).padStart(2, '0')}`;
  const random = Array.from(crypto.getRandomValues(new Uint8Array(3)), (value) => 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'[value % 31]).join('');
  return `PROMO-${stamp}-${random}`;
}

function initialDraft(source?: PromotionDetail, duplicate = false): Draft {
  const benefit = source?.benefits[0];
  const rule = (type: string) => source?.rules.find((value) => value.ruleType === type)?.configurationJson;
  const included = source?.targets.filter((target) => !target.isExcluded) ?? [];
  const now = new Date();
  // A duplicate keeps the definition but starts now with the original duration.
  const length = source ? new Date(source.endsOnUtc).getTime() - new Date(source.startsOnUtc).getTime() : 7 * 86_400_000;
  return {
    code: duplicate ? suggestCode() : source?.code ?? suggestCode(),
    name: duplicate && source ? `${source.name}（副本）` : source?.name ?? '',
    triggerType: source?.triggerType ?? 'Automatic',
    stackPolicy: source?.stackPolicy ?? 'Stackable',
    startsOn: source && !duplicate ? asLocalInput(new Date(source.startsOnUtc)) : asLocalInput(now),
    endsOn: source && !duplicate ? asLocalInput(new Date(source.endsOnUtc)) : asLocalInput(new Date(now.getTime() + Math.max(length, 3_600_000))),
    priority: String(source?.priority ?? 0),
    exclusiveGroup: source?.exclusiveGroup ?? '',
    minimumAmount: readNumber(rule('MinimumOrderAmount'), 'amount'),
    minimumQuantity: readNumber(rule('MinimumItemQuantity'), 'quantity'),
    benefitType: (benefit?.benefitType as BenefitType | undefined) ?? 'FixedAmountOff',
    benefitValue: readNumber(benefit?.configurationJson, benefit?.benefitType === 'PercentageOff' ? 'percentage' : 'amount'),
    maximumDiscount: readNumber(benefit?.configurationJson, 'maximumDiscount'),
    scope: included.some((target) => target.targetType === 'Category') ? 'Category' : included.some((target) => target.targetType === 'Sku') ? 'Sku' : 'All',
    categoryIds: included.filter((target) => target.targetType === 'Category').map((target) => target.targetKey ?? ''),
    skus: included.filter((target) => target.targetType === 'Sku').map((target) => target.targetKey).join('\n'),
  };
}

function toPayload(draft: Draft) {
  const amount = Number(draft.minimumAmount);
  const quantity = Number(draft.minimumQuantity);
  const value = Number(draft.benefitValue);
  const cap = Number(draft.maximumDiscount);
  const rules = [
    ...(amount > 0 ? [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: JSON.stringify({ amount }), sequence: 0 }] : []),
    ...(quantity > 0 ? [{ ruleType: 'MinimumItemQuantity', schemaVersion: 1, configurationJson: JSON.stringify({ quantity }), sequence: 1 }] : []),
  ];
  const benefits = [{
    benefitType: draft.benefitType,
    schemaVersion: 1,
    configurationJson: JSON.stringify(draft.benefitType === 'PercentageOff' ? { percentage: value, ...(cap > 0 ? { maximumDiscount: cap } : {}) } : { amount: value }),
    sequence: 0,
  }];
  const skus = draft.skus.split(/[\s,;，；]+/).map((sku) => sku.trim().toUpperCase()).filter(Boolean);
  const targets = draft.scope === 'Category'
    ? draft.categoryIds.map((id) => ({ targetType: 'Category', targetKey: id, isExcluded: false }))
    : draft.scope === 'Sku'
      ? Array.from(new Set(skus)).map((sku) => ({ targetType: 'Sku', targetKey: sku, isExcluded: false }))
      : [{ targetType: 'All', targetKey: null, isExcluded: false }];
  return { rules, benefits, targets };
}

function validate(draft: Draft, targetsCount: number) {
  const errors: string[] = [];
  const warnings: string[] = [];
  const start = new Date(draft.startsOn).getTime();
  const end = new Date(draft.endsOn).getTime();
  const value = Number(draft.benefitValue);
  const amount = Number(draft.minimumAmount);
  if (!draft.name.trim()) errors.push('请填写活动名称。');
  if (!(end > start)) errors.push('结束时间必须晚于开始时间。');
  if (end <= Date.now()) errors.push('结束时间已过，活动发布后不会生效。');
  if (!(value > 0)) errors.push('请填写优惠力度。');
  if (draft.benefitType === 'PercentageOff' && value > 100) errors.push('折扣比例不能超过 100%。');
  if (!targetsCount) errors.push(draft.scope === 'Category' ? '请至少选择一个分类。' : '请至少填写一个 SKU。');
  if (draft.benefitType === 'FixedAmountOff' && amount > 0 && value >= amount) warnings.push('减免金额不低于门槛金额，订单可能接近免费。');
  if (draft.benefitType === 'FixedAmountOff' && !(amount > 0) && value >= 50) warnings.push('无门槛大额立减，请确认是否需要设置门槛。');
  if (draft.benefitType === 'PercentageOff' && value >= 30 && !(Number(draft.maximumDiscount) > 0)) warnings.push('折扣力度较大且未设置最高优惠金额，大额订单让利可能过高。');
  if (draft.triggerType === 'Automatic' && draft.stackPolicy === 'Exclusive') warnings.push('独占的自动活动命中后，优先级更低的活动与优惠券都不会再计算；优先级更高的活动仍会先于它叠加。');
  return { errors, warnings };
}

/**
 * Create, edit and duplicate share one editor so every path builds identical rule, benefit and target payloads.
 * `duplicateOf` prefills a new draft from an existing promotion (new code, window starting now).
 */
export function PromotionForm({ item, duplicateOf, categories, busy, error, onSubmit, onCancel }: {
  item?: PromotionDetail;
  duplicateOf?: PromotionDetail;
  categories: CategoryOption[];
  busy: boolean;
  error?: ReactNode;
  onSubmit: (body: unknown) => void;
  onCancel: () => void;
}) {
  const { can } = useSession();
  const editing = Boolean(item);
  const [draft, setDraft] = useState<Draft>(() => initialDraft(item ?? duplicateOf, Boolean(duplicateOf)));
  const [template, setTemplate] = useState('');
  const [touched, setTouched] = useState(false);
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const payload = useMemo(() => toPayload(draft), [draft]);
  const { errors, warnings } = validate(draft, payload.targets.length);
  const categoryNames = useMemo(() => new Map(categories.map((category) => [category.id, category.name])), [categories]);
  const startsOn = new Date(draft.startsOn);
  const endsOn = new Date(draft.endsOn);
  const windowText = Number.isNaN(startsOn.getTime()) || Number.isNaN(endsOn.getTime()) ? '时间未填写' : durationText(startsOn.toISOString(), endsOn.toISOString());

  const applyTemplate = (id: string) => {
    const chosen = templates.find((entry) => entry.id === id);
    if (!chosen) return;
    setTemplate(id);
    setDraft((current) => ({ ...current, ...chosen.apply, name: current.name || chosen.label }));
  };

  const setDuration = (days: number) => {
    const start = new Date(draft.startsOn);
    const base = Number.isNaN(start.getTime()) ? new Date() : start;
    set('endsOn', asLocalInput(new Date(base.getTime() + days * 86_400_000)));
  };

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setTouched(true);
    if (errors.length) return;
    onSubmit({
      code: item?.code ?? draft.code.trim().toUpperCase(),
      name: draft.name.trim(),
      triggerType: item?.triggerType ?? draft.triggerType,
      startsOnUtc: new Date(draft.startsOn).toISOString(),
      endsOnUtc: new Date(draft.endsOn).toISOString(),
      priority: Number(draft.priority || 0),
      stackPolicy: draft.stackPolicy,
      exclusiveGroup: draft.exclusiveGroup.trim() || null,
      ...payload,
      expectedVersion: item?.version ?? null,
    });
  };

  const preview = offerText(payload.rules, payload.benefits);
  const selectedCategories = new Set(draft.categoryIds);

  return <form className="modal-form promotion-editor" onSubmit={submit} noValidate>
    {!editing ? <div className="template-picker" role="radiogroup" aria-label="快速模板">
      {templates.map((entry) => <button key={entry.id} type="button" role="radio" aria-checked={template === entry.id} className={template === entry.id ? 'is-active' : undefined} onClick={() => applyTemplate(entry.id)}>
        <strong>{entry.label}</strong><small>{entry.hint}</small>
      </button>)}
    </div> : null}

    <aside className="promotion-preview" aria-live="polite">
      <span className="promotion-preview__badge">{draft.triggerType === 'Coupon' ? '凭券' : '自动'}</span>
      <div>
        <strong>{preview}</strong>
        <small>{draft.scope !== 'All' && !payload.targets.length ? '未选择适用范围' : scopeText(payload.targets, categoryNames)} · {windowText} · {draft.stackPolicy === 'Exclusive' ? '独占' : '可叠加'}</small>
      </div>
    </aside>

    <div className="form-section-title"><span>01</span><div><strong>活动信息</strong><small>{editing ? '活动代码与触发方式创建后不可修改' : '代码用于订单优惠快照与对账，可直接使用建议值'}</small></div></div>
    <div className="form-row">
      <Field label="活动名称"><input value={draft.name} onChange={(event) => set('name', event.target.value)} required maxLength={200} autoFocus placeholder="例如 中秋满减" /></Field>
      <Field label="活动代码">
        <div className="input-group">
          <input value={draft.code} onChange={(event) => set('code', event.target.value.toUpperCase())} required maxLength={60} disabled={editing} className="mono" />
          {!editing ? <button className="button button--quiet" type="button" onClick={() => set('code', suggestCode())} title="重新生成"><Icon name="refresh" /></button> : null}
        </div>
      </Field>
    </div>
    <div className="form-row">
      <Field label="触发方式" hint={draft.triggerType === 'Coupon' ? '发布后到「优惠券」发放券码，用户凭券享受' : '满足条件自动计算，无需领券'}>
        <select value={draft.triggerType} onChange={(event) => set('triggerType', event.target.value)} disabled={editing}><option value="Automatic">自动生效</option><option value="Coupon">凭券使用</option></select>
      </Field>
      <Field label="叠加策略"><select value={draft.stackPolicy} onChange={(event) => set('stackPolicy', event.target.value)}><option value="Stackable">可叠加</option><option value="Exclusive">独占（命中后，优先级更低的活动与优惠券不再计算）</option></select></Field>
    </div>
    <div className="form-row">
      <Field label="开始时间"><input type="datetime-local" value={draft.startsOn} onChange={(event) => set('startsOn', event.target.value)} required /></Field>
      <Field label="结束时间"><input type="datetime-local" value={draft.endsOn} onChange={(event) => set('endsOn', event.target.value)} required /></Field>
    </div>
    <div className="preset-row" aria-label="活动时长">
      <span>时长</span>
      {durations.map((entry) => <button key={entry.days} type="button" onClick={() => setDuration(entry.days)}>{entry.label}</button>)}
      <button type="button" onClick={() => { const start = new Date(draft.startsOn); set('endsOn', asLocalInput(new Date(start.getFullYear(), start.getMonth() + 1, 0, 23, 59))); }}>至月底</button>
    </div>
    <details className="advanced-fields" open={Boolean(draft.exclusiveGroup) || draft.priority !== '0'}>
      <summary>高级：优先级与互斥组</summary>
      <div className="form-row">
        <Field label="优先级" hint="数值越大越先计算"><input type="number" min={-10000} max={10000} value={draft.priority} onChange={(event) => set('priority', event.target.value)} /></Field>
        <Field label="互斥组" hint="同组活动只命中一个"><input value={draft.exclusiveGroup} onChange={(event) => set('exclusiveGroup', event.target.value)} maxLength={80} placeholder="可选" /></Field>
      </div>
    </details>

    <div className="form-section-title"><span>02</span><div><strong>门槛与优惠</strong><small>门槛留空表示无门槛</small></div></div>
    <div className="form-row">
      <Field label="最低订单金额"><input type="number" min="0" step="0.01" value={draft.minimumAmount} onChange={(event) => set('minimumAmount', event.target.value)} placeholder="例如 199" /></Field>
      <Field label="最低购买件数"><input type="number" min="0" step="1" value={draft.minimumQuantity} onChange={(event) => set('minimumQuantity', event.target.value)} placeholder="例如 2" /></Field>
    </div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="优惠类型">
      {([['FixedAmountOff', '固定金额减免'], ['PercentageOff', '百分比折扣']] as const).map(([value, label]) => <button key={value} type="button" role="radio" aria-checked={draft.benefitType === value} aria-selected={draft.benefitType === value} onClick={() => set('benefitType', value)}>{label}</button>)}
    </div>
    <div className="form-row">
      <Field label={draft.benefitType === 'PercentageOff' ? '折扣比例 (%)' : '减免金额'} hint={draft.benefitType === 'PercentageOff' && Number(draft.benefitValue) > 0 ? `即 ${offerText([], payload.benefits).replace(/^无门槛 /, '')}` : undefined}>
        <input type="number" min="0.01" max={draft.benefitType === 'PercentageOff' ? 100 : undefined} step="0.01" value={draft.benefitValue} onChange={(event) => set('benefitValue', event.target.value)} required />
      </Field>
      {draft.benefitType === 'PercentageOff'
        ? <Field label="最高优惠金额" hint="可选，防止大额订单优惠过高"><input type="number" min="0" step="0.01" value={draft.maximumDiscount} onChange={(event) => set('maximumDiscount', event.target.value)} /></Field>
        : <span />}
    </div>

    <div className="form-section-title"><span>03</span><div><strong>适用范围</strong><small>决定哪些商品参与优惠计算</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="适用范围">
      {([['All', '全部商品'], ['Category', '指定分类'], ['Sku', '指定 SKU']] as const).map(([value, label]) => <button key={value} type="button" role="radio" aria-selected={draft.scope === value} aria-checked={draft.scope === value} onClick={() => set('scope', value)}>{label}</button>)}
    </div>
    {draft.scope === 'Category' ? <fieldset className="choice-grid choice-grid--columns"><legend>选择分类（已选 {selectedCategories.size}）</legend>
      {categories.filter((category) => category.status !== 'Archived').map((category) => <label key={category.id}>
        <input type="checkbox" checked={selectedCategories.has(category.id)} onChange={(event) => set('categoryIds', event.target.checked ? [...draft.categoryIds, category.id] : draft.categoryIds.filter((id) => id !== category.id))} />
        <span><strong>{category.name}</strong><small>{category.code}</small></span>
      </label>)}
      {!categories.length ? <small>需要 catalog.categories.query 权限读取分类</small> : null}
    </fieldset> : null}
    {draft.scope === 'Sku' ? <Field label={`选择 SKU（已选 ${payload.targets.length} 个）`} hint="勾选商品可一次选中它的全部 SKU">
      <SkuPicker value={payload.targets.map((target) => target.targetKey!)} onChange={(skus) => set('skus', skus.join('\n'))} canSearch={can('catalog.products.query')} />
    </Field> : null}
    {draft.scope === 'All' ? <p className="muted-copy"><Icon name="check" />订单中的全部商品参与计算。</p> : null}

    {warnings.length ? <div className="notice notice--warning" role="status"><Icon name="alert" /><span>{warnings.join(' ')}</span></div> : null}
    {touched && errors.length ? <div className="notice notice--error" role="alert"><Icon name="alert" /><span>{errors.join(' ')}</span></div> : null}
    {error}
    <SubmitBar busy={busy} label={editing ? '保存草稿' : duplicateOf ? '创建副本' : '创建草稿'} onCancel={onCancel} />
  </form>;
}
