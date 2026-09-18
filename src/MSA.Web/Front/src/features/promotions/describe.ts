import { formatMoney } from '../../shared/utils/format';
import type { PromotionBenefit, PromotionRule, PromotionTarget } from './types';

function read(json: string, property: string): number | undefined {
  try {
    const value = (JSON.parse(json) as Record<string, unknown>)[property];
    return typeof value === 'number' ? value : undefined;
  } catch {
    return undefined;
  }
}

function discountLabel(percentage: number): string {
  // 10% off reads as "9折", 15% off as "8.5折".
  const rate = Math.round((100 - percentage) * 10) / 100;
  return `${rate % 1 === 0 ? rate.toFixed(0) : rate}折`;
}

export function minimumAmount(rules: PromotionRule[]): number | undefined {
  const rule = rules.find((item) => item.ruleType === 'MinimumOrderAmount');
  return rule ? read(rule.configurationJson, 'amount') : undefined;
}

export function minimumQuantity(rules: PromotionRule[]): number | undefined {
  const rule = rules.find((item) => item.ruleType === 'MinimumItemQuantity');
  return rule ? read(rule.configurationJson, 'quantity') : undefined;
}

/** Short value shown on coupon stubs, e.g. "¥30" or "9折". */
export function benefitValue(benefits: PromotionBenefit[], currency = 'CNY'): string {
  const benefit = benefits[0];
  if (!benefit) return '优惠';
  if (benefit.benefitType === 'FixedAmountOff') {
    const amount = read(benefit.configurationJson, 'amount');
    return amount ? formatMoney(amount, currency).replace('.00', '') : '立减';
  }
  if (benefit.benefitType === 'PercentageOff') {
    const percentage = read(benefit.configurationJson, 'percentage');
    return percentage ? discountLabel(percentage) : '折扣';
  }
  return '优惠';
}

/** Full sentence such as "满 ¥300 减 ¥30" or "满 2 件享 9折，最高减 ¥50". */
export function describePromotion(rules: PromotionRule[], benefits: PromotionBenefit[], currency = 'CNY'): string {
  const amount = minimumAmount(rules);
  const quantity = minimumQuantity(rules);
  const threshold = [
    amount ? `满 ${formatMoney(amount, currency).replace('.00', '')}` : '',
    quantity ? `满 ${quantity} 件` : '',
  ].filter(Boolean).join(' 且 ');
  const parts = benefits.map((benefit) => {
    if (benefit.benefitType === 'FixedAmountOff') {
      const value = read(benefit.configurationJson, 'amount');
      return value ? `减 ${formatMoney(value, currency).replace('.00', '')}` : '立减';
    }
    if (benefit.benefitType === 'PercentageOff') {
      const percentage = read(benefit.configurationJson, 'percentage');
      const cap = read(benefit.configurationJson, 'maximumDiscount');
      return `${percentage ? `享 ${discountLabel(percentage)}` : '享折扣'}${cap ? `，最高减 ${formatMoney(cap, currency).replace('.00', '')}` : ''}`;
    }
    return '享优惠';
  });
  const benefitText = parts.join('，') || '享优惠';
  if (threshold) return `${threshold} ${benefitText}`;
  // Without a threshold "减 ¥20" reads better as "立减 ¥20".
  return benefitText.startsWith('减') ? `立${benefitText}` : `无门槛${benefitText}`;
}

export function conditionText(rules: PromotionRule[], currency = 'CNY'): string {
  const amount = minimumAmount(rules);
  const quantity = minimumQuantity(rules);
  if (amount && quantity) return `满 ${formatMoney(amount, currency).replace('.00', '')} 且满 ${quantity} 件可用`;
  if (amount) return `满 ${formatMoney(amount, currency).replace('.00', '')} 可用`;
  if (quantity) return `满 ${quantity} 件可用`;
  return '无门槛';
}

export function describeTargets(targets: PromotionTarget[], categoryNames: Map<string, string> = new Map()): string {
  const included = targets.filter((target) => !target.isExcluded);
  const excluded = targets.filter((target) => target.isExcluded);
  const label = (target: PromotionTarget) => {
    switch (target.targetType) {
      case 'Category': return categoryNames.get(target.targetKey ?? '') ?? '指定分类';
      case 'Sku': return target.targetKey ?? '指定商品';
      case 'Product': return '指定商品';
      default: return '全场商品';
    }
  };
  const scope = !included.length || included.some((target) => target.targetType === 'All')
    ? '全场商品'
    : Array.from(new Set(included.map(label))).join('、');
  return excluded.length ? `${scope}（部分商品除外）` : scope;
}

type ProductScope = { id: string; categoryId?: string | null; skus: string[] };

/** Mirrors the ordering engine's target matching so badges never promise a discount checkout would refuse. */
export function appliesToProduct(targets: PromotionTarget[], product: ProductScope): boolean {
  const matches = (target: PromotionTarget) => {
    switch (target.targetType.toUpperCase()) {
      case 'ALL': return true;
      case 'PRODUCT': return target.targetKey?.toLowerCase() === product.id.toLowerCase();
      case 'CATEGORY': return Boolean(product.categoryId) && target.targetKey?.toLowerCase() === product.categoryId?.toLowerCase();
      case 'SKU': return product.skus.some((sku) => sku.toUpperCase() === target.targetKey?.toUpperCase());
      default: return false;
    }
  };
  const included = targets.filter((target) => !target.isExcluded);
  const excluded = targets.filter((target) => target.isExcluded);
  return (included.length === 0 || included.some(matches)) && !excluded.some(matches);
}
