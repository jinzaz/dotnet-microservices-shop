import { formatMoney } from '../../ui';
import type { PromotionComponent, PromotionTarget } from './PromotionForm';

function read(json: string | undefined, property: string): number | undefined {
  try {
    const value = (JSON.parse(json ?? '{}') as Record<string, unknown>)[property];
    return typeof value === 'number' ? value : undefined;
  } catch {
    return undefined;
  }
}

const money = (value: number) => formatMoney(value).replace(/\.00$/, '');

/** 15 (% off) → "8.5 折". */
export function discountLabel(percentage: number) {
  const rate = Math.round((100 - percentage) * 10) / 100;
  return `${rate % 1 === 0 ? rate.toFixed(0) : rate} 折`;
}

export function ruleValue(rules: PromotionComponent[], type: 'MinimumOrderAmount' | 'MinimumItemQuantity') {
  const rule = rules.find((item) => item.ruleType === type);
  return read(rule?.configurationJson, type === 'MinimumOrderAmount' ? 'amount' : 'quantity');
}

/** Operator-facing sentence, e.g. "满 ¥199 减 ¥30" or "满 2 件 8.5 折，最高减 ¥50". */
export function offerText(rules: PromotionComponent[], benefits: PromotionComponent[]) {
  const amount = ruleValue(rules, 'MinimumOrderAmount');
  const quantity = ruleValue(rules, 'MinimumItemQuantity');
  const threshold = [amount ? `满 ${money(amount)}` : '', quantity ? `满 ${quantity} 件` : ''].filter(Boolean).join(' 且 ');
  const benefit = benefits.map((item) => {
    if (item.benefitType === 'FixedAmountOff') {
      const value = read(item.configurationJson, 'amount');
      return value ? `减 ${money(value)}` : '减免';
    }
    if (item.benefitType === 'PercentageOff') {
      const percentage = read(item.configurationJson, 'percentage');
      const cap = read(item.configurationJson, 'maximumDiscount');
      return `${percentage ? discountLabel(percentage) : '折扣'}${cap ? `，最高减 ${money(cap)}` : ''}`;
    }
    return item.benefitType ?? '优惠';
  }).join('，') || '未设置优惠';
  if (threshold) return `${threshold} ${benefit}`;
  return benefit.startsWith('减') ? `立${benefit}` : `无门槛 ${benefit}`;
}

export function scopeText(targets: PromotionTarget[], categoryNames: Map<string, string>) {
  const included = targets.filter((target) => !target.isExcluded);
  if (!included.length || included.some((target) => target.targetType === 'All')) return '全部商品';
  const categories = included.filter((target) => target.targetType === 'Category').map((target) => categoryNames.get(target.targetKey ?? '') ?? '未知分类');
  const skus = included.filter((target) => target.targetType === 'Sku').length;
  return [categories.length ? `分类：${categories.join('、')}` : '', skus ? `${skus} 个 SKU` : ''].filter(Boolean).join(' · ');
}

const DAY = 86_400_000;

function span(ms: number) {
  if (ms >= DAY) return `${Math.ceil(ms / DAY)} 天`;
  if (ms >= 3_600_000) return `${Math.ceil(ms / 3_600_000)} 小时`;
  return `${Math.max(1, Math.ceil(ms / 60_000))} 分钟`;
}

/** Where "now" sits in a validity window, with a tone for badges. */
export function windowPhase(startsOnUtc: string, endsOnUtc: string, now = Date.now()) {
  const start = new Date(startsOnUtc).getTime();
  const end = new Date(endsOnUtc).getTime();
  if (now < start) return { label: '未开始', detail: `${span(start - now)}后开始`, tone: 'warn' as const };
  if (now >= end) return { label: '已过期', detail: `已结束 ${span(now - end)}`, tone: 'bad' as const };
  const left = end - now;
  return { label: '进行中', detail: `还剩 ${span(left)}`, tone: left < 3 * DAY ? 'warn' as const : 'good' as const };
}

export function durationText(startsOnUtc: string, endsOnUtc: string) {
  const ms = new Date(endsOnUtc).getTime() - new Date(startsOnUtc).getTime();
  return ms > 0 ? span(ms) : '—';
}

/** Big number on a coupon stub: "¥20" or "8.5 折". */
export function benefitHeadline(benefits: PromotionComponent[]) {
  const benefit = benefits[0];
  if (benefit?.benefitType === 'FixedAmountOff') {
    const amount = read(benefit.configurationJson, 'amount');
    return amount ? money(amount) : '立减';
  }
  if (benefit?.benefitType === 'PercentageOff') {
    const percentage = read(benefit.configurationJson, 'percentage');
    return percentage ? discountLabel(percentage) : '折扣';
  }
  return '优惠';
}

/** Usage condition under the stub: "满 ¥99 可用" or "无门槛". */
export function conditionText(rules: PromotionComponent[]) {
  const amount = ruleValue(rules, 'MinimumOrderAmount');
  const quantity = ruleValue(rules, 'MinimumItemQuantity');
  const parts = [amount ? `满 ${money(amount)}` : '', quantity ? `满 ${quantity} 件` : ''].filter(Boolean);
  return parts.length ? `${parts.join(' 且 ')} 可用` : '无门槛';
}
