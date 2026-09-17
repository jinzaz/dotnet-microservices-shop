import { useState, type FormEvent, type ReactNode } from 'react';
import { asLocalInput, Field, Icon, SubmitBar } from '../../ui';

export type PromotionComponent = { ruleType?: string; benefitType?: string; schemaVersion: number; configurationJson: string; sequence: number };
export type PromotionTarget = { targetType: string; targetKey?: string | null; isExcluded: boolean };
export type PromotionDetail = {
  id: string; code: string; name: string; triggerType: string; status: string; startsOnUtc: string; endsOnUtc: string;
  priority: number; stackPolicy: string; exclusiveGroup?: string | null; revision: number; version: number;
  rules: PromotionComponent[]; benefits: PromotionComponent[]; targets: PromotionTarget[];
};
export type CategoryOption = { id: string; name: string; code: string; status: string };

type TargetScope = 'All' | 'Category' | 'Sku';

function readNumber(json: string | undefined, property: string) {
  try {
    const value = (JSON.parse(json ?? '{}') as Record<string, unknown>)[property];
    return typeof value === 'number' ? String(value) : '';
  } catch {
    return '';
  }
}

/** Create and edit share one editor so both paths build identical rule, benefit and target payloads. */
export function PromotionForm({ item, categories, busy, error, onSubmit, onCancel }: {
  item?: PromotionDetail;
  categories: CategoryOption[];
  busy: boolean;
  error?: ReactNode;
  onSubmit: (body: unknown) => void;
  onCancel: () => void;
}) {
  const benefit = item?.benefits[0];
  const initialScope: TargetScope = item?.targets.find((target) => !target.isExcluded)?.targetType === 'Category' ? 'Category'
    : item?.targets.some((target) => target.targetType === 'Sku') ? 'Sku' : 'All';
  const [scope, setScope] = useState<TargetScope>(initialScope);
  const [benefitType, setBenefitType] = useState(benefit?.benefitType ?? 'PercentageOff');
  const [scopeError, setScopeError] = useState('');

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (name: string) => String(data.get(name) ?? '').trim();
    const minimumAmount = Number(value('minimumAmount'));
    const minimumQuantity = Number(value('minimumQuantity'));
    const benefitValue = Number(value('benefitValue'));
    const maximumDiscount = Number(value('maximumDiscount'));
    const rules = [
      ...(minimumAmount > 0 ? [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: JSON.stringify({ amount: minimumAmount }), sequence: 0 }] : []),
      ...(minimumQuantity > 0 ? [{ ruleType: 'MinimumItemQuantity', schemaVersion: 1, configurationJson: JSON.stringify({ quantity: minimumQuantity }), sequence: 1 }] : []),
    ];
    const benefitConfiguration = benefitType === 'PercentageOff'
      ? { percentage: benefitValue, ...(maximumDiscount > 0 ? { maximumDiscount } : {}) }
      : { amount: benefitValue };
    const targets = scope === 'Category'
      ? data.getAll('categoryIds').map((id) => ({ targetType: 'Category', targetKey: String(id), isExcluded: false }))
      : scope === 'Sku'
        ? value('skus').split(/[\s,;]+/).filter(Boolean).map((sku) => ({ targetType: 'Sku', targetKey: sku.toUpperCase(), isExcluded: false }))
        : [{ targetType: 'All', targetKey: null, isExcluded: false }];
    if (!targets.length) {
      setScopeError(scope === 'Category' ? '请至少选择一个分类。' : '请至少填写一个 SKU。');
      return;
    }
    setScopeError('');
    onSubmit({
      code: item?.code ?? value('code'),
      name: value('name'),
      triggerType: item?.triggerType ?? value('triggerType'),
      startsOnUtc: new Date(value('startsOnUtc')).toISOString(),
      endsOnUtc: new Date(value('endsOnUtc')).toISOString(),
      priority: Number(value('priority') || 0),
      stackPolicy: value('stackPolicy'),
      exclusiveGroup: value('exclusiveGroup') || null,
      rules,
      benefits: [{ benefitType, schemaVersion: 1, configurationJson: JSON.stringify(benefitConfiguration), sequence: 0 }],
      targets,
      expectedVersion: item?.version ?? null,
    });
  };

  const selectedCategories = new Set(item?.targets.filter((target) => target.targetType === 'Category').map((target) => target.targetKey));
  const rule = (type: string) => item?.rules.find((value) => value.ruleType === type)?.configurationJson;

  return <form className="modal-form" onSubmit={submit}>
    <div className="form-section-title"><span>01</span><div><strong>活动信息</strong><small>{item ? '活动代码与触发方式创建后不可修改' : '代码用于订单优惠快照与对账'}</small></div></div>
    <div className="form-row">
      <Field label="活动代码"><input name="code" defaultValue={item?.code} required maxLength={60} disabled={Boolean(item)} placeholder="例如 MID-AUTUMN-2026" /></Field>
      <Field label="活动名称"><input name="name" defaultValue={item?.name} required maxLength={200} autoFocus /></Field>
    </div>
    <div className="form-row">
      <Field label="触发方式" hint="优惠券活动需要再创建券码"><select name="triggerType" defaultValue={item?.triggerType ?? 'Automatic'} disabled={Boolean(item)}><option value="Automatic">自动生效</option><option value="Coupon">凭券使用</option></select></Field>
      <Field label="叠加策略"><select name="stackPolicy" defaultValue={item?.stackPolicy ?? 'Stackable'}><option value="Stackable">可叠加</option><option value="Exclusive">独占（命中后不再计算后续活动）</option></select></Field>
    </div>
    <div className="form-row">
      <Field label="开始时间"><input name="startsOnUtc" type="datetime-local" defaultValue={item ? asLocalInput(new Date(item.startsOnUtc)) : asLocalInput()} required /></Field>
      <Field label="结束时间"><input name="endsOnUtc" type="datetime-local" defaultValue={item ? asLocalInput(new Date(item.endsOnUtc)) : asLocalInput(new Date(Date.now() + 7 * 86_400_000))} required /></Field>
    </div>
    <div className="form-row">
      <Field label="优先级" hint="数值越大越先计算"><input name="priority" type="number" min={-10000} max={10000} defaultValue={item?.priority ?? 0} /></Field>
      <Field label="互斥组" hint="同组活动只命中一个"><input name="exclusiveGroup" defaultValue={item?.exclusiveGroup ?? ''} maxLength={80} placeholder="可选" /></Field>
    </div>

    <div className="form-section-title"><span>02</span><div><strong>门槛与优惠</strong><small>留空表示无门槛</small></div></div>
    <div className="form-row">
      <Field label="最低订单金额"><input name="minimumAmount" type="number" min="0" step="0.01" defaultValue={readNumber(rule('MinimumOrderAmount'), 'amount')} placeholder="例如 199" /></Field>
      <Field label="最低购买件数"><input name="minimumQuantity" type="number" min="0" step="1" defaultValue={readNumber(rule('MinimumItemQuantity'), 'quantity')} placeholder="例如 2" /></Field>
    </div>
    <div className="form-row">
      <Field label="优惠类型"><select value={benefitType} onChange={(event) => setBenefitType(event.target.value)}><option value="PercentageOff">百分比折扣</option><option value="FixedAmountOff">固定金额减免</option></select></Field>
      <Field label={benefitType === 'PercentageOff' ? '折扣比例 (%)' : '减免金额'}><input name="benefitValue" type="number" min="0.01" max={benefitType === 'PercentageOff' ? 100 : undefined} step="0.01" required defaultValue={readNumber(benefit?.configurationJson, benefitType === 'PercentageOff' ? 'percentage' : 'amount')} /></Field>
    </div>
    {benefitType === 'PercentageOff' ? <Field label="最高优惠金额" hint="可选，防止大额订单优惠过高"><input name="maximumDiscount" type="number" min="0" step="0.01" defaultValue={readNumber(benefit?.configurationJson, 'maximumDiscount')} /></Field> : null}

    <div className="form-section-title"><span>03</span><div><strong>适用范围</strong><small>决定哪些商品参与优惠计算</small></div></div>
    <div className="segmented segmented--inline" role="radiogroup" aria-label="适用范围">
      {([['All', '全部商品'], ['Category', '指定分类'], ['Sku', '指定 SKU']] as const).map(([value, label]) => <button key={value} type="button" role="radio" aria-selected={scope === value} aria-checked={scope === value} onClick={() => setScope(value)}>{label}</button>)}
    </div>
    {scope === 'Category' ? <fieldset className="choice-grid choice-grid--columns"><legend>选择分类</legend>{categories.filter((category) => category.status !== 'Archived').map((category) => <label key={category.id}><input type="checkbox" name="categoryIds" value={category.id} defaultChecked={selectedCategories.has(category.id)} /><span><strong>{category.name}</strong><small>{category.code}</small></span></label>)}{!categories.length ? <small>需要 catalog.categories.query 权限读取分类</small> : null}</fieldset> : null}
    {scope === 'Sku' ? <Field label="SKU 列表" hint="换行、空格或逗号分隔"><textarea name="skus" rows={3} required defaultValue={item?.targets.filter((target) => target.targetType === 'Sku').map((target) => target.targetKey).join('\n')} /></Field> : null}
    {scope === 'All' ? <p className="muted-copy"><Icon name="check" />订单中的全部商品参与计算。</p> : null}
    {scopeError ? <p className="field-error" role="alert">{scopeError}</p> : null}

    {error}
    <SubmitBar busy={busy} label={item ? '保存草稿' : '创建活动'} onCancel={onCancel} />
  </form>;
}
