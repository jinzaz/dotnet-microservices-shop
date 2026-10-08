import { useQuery } from '@tanstack/react-query';
import { api, query, type Paged } from '../../api';
import { CopyButton, formatDate, Icon, Status } from '../../ui';
import { discountLabel, offerText, ruleValue, scopeText, windowPhase } from './promotion-text';
import type { PromotionDetail } from './PromotionForm';

export type Promotion = PromotionDetail & { updatedOnUtc: string };
type ActionOptions = { name: string; reason?: boolean; danger?: boolean; detail?: string };
export type PromotionActionHandler = (row: Promotion, action: string, label: string, options: ActionOptions) => void;

/** One place decides which lifecycle actions a status allows, so table and detail never disagree. */
export function promotionActions(row: Promotion) {
  const actions: Array<{ action: string; label: string; options: ActionOptions; primary?: boolean }> = [];
  if (row.status === 'Draft') actions.push({ action: 'publish', label: '发布', primary: true, options: { name: row.name, detail: '发布后定义不可修改，订单服务将立即按活动周期计算优惠。' } });
  if (row.status === 'Active') actions.push({ action: 'pause', label: '暂停', options: { name: row.name, detail: '暂停期间不参与计算，可随时恢复。' } });
  if (row.status === 'Paused') actions.push({ action: 'resume', label: '恢复', primary: true, options: { name: row.name } });
  if (['Paused', 'Ended'].includes(row.status)) actions.push({ action: 'revise', label: '重新编辑', options: { name: row.name, detail: '活动回到草稿并立即停止参与计算，修改后需要重新发布。' } });
  if (['Active', 'Paused'].includes(row.status)) actions.push({ action: 'end', label: '结束', options: { name: row.name, danger: true, detail: '结束后活动不再参与计算；之后可以重新编辑或归档。' } });
  if (['Draft', 'Ended'].includes(row.status)) actions.push({ action: 'archive', label: '归档', options: { name: row.name, danger: true, detail: '归档是终态，活动将不能再编辑或发布。' } });
  return actions;
}

function PhaseBadge({ row }: { row: Promotion }) {
  const phase = windowPhase(row.startsOnUtc, row.endsOnUtc);
  return <span className={`phase phase--${phase.tone}`} title={phase.detail}>{phase.label}<small>{phase.detail}</small></span>;
}

export function PromotionTable({ rows, canEdit, categoryNames, onOpen, onAction }: {
  rows: Promotion[];
  canEdit: boolean;
  categoryNames: Map<string, string>;
  onOpen: (id: string) => void;
  onAction: PromotionActionHandler;
}) {
  return <table><thead><tr><th>促销</th><th>优惠内容</th><th>触发</th><th>活动周期</th><th>状态</th><th>操作</th></tr></thead><tbody>{rows.map((row) => {
    const actions = canEdit ? promotionActions(row).filter((item) => item.action !== 'archive') : [];
    return <tr key={row.id} className="is-clickable" onClick={() => onOpen(row.id)}>
      <td><strong>{row.name}</strong><small className="mono">{row.code}</small></td>
      <td><strong className="offer-text">{offerText(row.rules, row.benefits)}</strong><small>{scopeText(row.targets, categoryNames)}</small></td>
      <td>{row.triggerType === 'Coupon' ? '凭券使用' : '自动生效'}<small>{row.stackPolicy === 'Exclusive' ? '独占' : '可叠加'} · 优先级 {row.priority}</small></td>
      <td>{formatDate(row.startsOnUtc)}<small>至 {formatDate(row.endsOnUtc)}</small><PhaseBadge row={row} /></td>
      <td><Status>{row.status}</Status></td>
      <td onClick={(event) => event.stopPropagation()}><div className="row-actions">
        <button type="button" onClick={() => onOpen(row.id)}>{row.status === 'Draft' && canEdit ? '编辑' : '详情'}</button>
        {actions.slice(0, 2).map((item) => <button key={item.action} type="button" className={item.primary ? 'row-action-primary' : item.options.danger ? 'danger-link' : undefined} onClick={() => onAction(row, item.action, item.label, item.options)}>{item.label}</button>)}
      </div></td>
    </tr>;
  })}</tbody></table>;
}

/** Read-only view with every lifecycle action plus shortcuts to duplicate the promotion or issue its coupons. */
export function PromotionDetailView({ promotion, categoryNames, canEdit, canCreate, canQueryCoupons, canIssueCoupons, onAction, onDuplicate, onIssueCoupons, onShowCoupons, onCreateCampaign }: {
  promotion: Promotion;
  categoryNames: Map<string, string>;
  canEdit: boolean;
  canCreate: boolean;
  canQueryCoupons: boolean;
  canIssueCoupons: boolean;
  onAction: PromotionActionHandler;
  onDuplicate: () => void;
  onIssueCoupons: () => void;
  onShowCoupons: () => void;
  onCreateCampaign?: () => void;
}) {
  const isCoupon = promotion.triggerType === 'Coupon';
  const couponCount = useQuery({
    queryKey: ['catalog', 'coupons', 'count', promotion.id],
    queryFn: () => api<Paged<unknown>>(`/api/catalog/admin/coupons?${query({ promotionId: promotion.id, skip: 0, take: 1 })}`),
    enabled: isCoupon && canQueryCoupons,
    select: (data) => data.total,
  });
  const amount = ruleValue(promotion.rules, 'MinimumOrderAmount');
  const quantity = ruleValue(promotion.rules, 'MinimumItemQuantity');
  const phase = windowPhase(promotion.startsOnUtc, promotion.endsOnUtc);
  const actions = canEdit ? promotionActions(promotion) : [];

  return <div className="order-detail promotion-detail">
    <div className="promotion-hero">
      <span className="promotion-preview__badge">{isCoupon ? '凭券' : '自动'}</span>
      <div>
        <strong>{offerText(promotion.rules, promotion.benefits)}</strong>
        <small>{scopeText(promotion.targets, categoryNames)} · <span className={`phase phase--${phase.tone}`}>{phase.label} · {phase.detail}</span></small>
      </div>
    </div>

    {actions.length || canCreate ? <div className="detail-actions">
      {actions.map((item) => <button key={item.action} type="button" className={`button ${item.primary ? 'button--dark' : item.options.danger ? 'button--danger-quiet' : 'button--quiet'}`} onClick={() => onAction(promotion, item.action, item.label, item.options)}>{item.label}</button>)}
      {canCreate ? <button type="button" className="button button--quiet" onClick={onDuplicate}><Icon name="copy" />复制为新活动</button> : null}
    </div> : null}

    <div className="order-detail__summary">
      <div><span>活动周期</span><strong className="summary-date">{formatDate(promotion.startsOnUtc)}</strong><small>至 {formatDate(promotion.endsOnUtc)}</small></div>
      <div><span>执行策略</span><p><Status>{promotion.status}</Status></p><small>{isCoupon ? '凭券使用' : '自动生效'} · {promotion.stackPolicy === 'Exclusive' ? '独占' : '可叠加'} · 优先级 {promotion.priority}{promotion.exclusiveGroup ? ` · 互斥组 ${promotion.exclusiveGroup}` : ''}</small></div>
    </div>

    <section><h3>门槛</h3><div className="compact-list">
      {amount ? <article><div><strong>订单金额满 ¥{amount}</strong><small>按订单商品总额（优惠前）判断</small></div></article> : null}
      {quantity ? <article><div><strong>购买件数满 {quantity} 件</strong><small>按适用范围内的商品件数判断</small></div></article> : null}
      {!amount && !quantity ? <p className="muted-copy">无门槛</p> : null}
    </div></section>
    <section><h3>优惠</h3><div className="compact-list">{promotion.benefits.map((benefit) => {
      const config = JSON.parse(benefit.configurationJson) as { amount?: number; percentage?: number; maximumDiscount?: number };
      return <article key={benefit.sequence}><div>
        <strong>{benefit.benefitType === 'PercentageOff' ? `${discountLabel(config.percentage ?? 0)}（减 ${config.percentage}%）` : `立减 ¥${config.amount}`}</strong>
        <small>{benefit.benefitType === 'PercentageOff' ? config.maximumDiscount ? `单笔最高减 ¥${config.maximumDiscount}` : '未设置最高优惠金额' : '按适用商品金额封顶，订单至少保留 ¥0.01'}</small>
      </div></article>;
    })}</div></section>
    <section><h3>适用范围</h3><div className="tag-list">{promotion.targets.length ? promotion.targets.map((target, index) => <span key={index}>{target.isExcluded ? '排除 ' : ''}{target.targetType === 'All' ? '全部商品' : target.targetType === 'Category' ? `分类 ${categoryNames.get(target.targetKey ?? '') ?? target.targetKey}` : `SKU ${target.targetKey}`}</span>) : <span>全部商品</span>}</div></section>

    {isCoupon ? <section className="coupon-shortcut">
      <h3>优惠券</h3>
      <div>
        <p>{canQueryCoupons ? couponCount.isPending ? '正在统计券数量…' : `该活动下已有 ${couponCount.data ?? 0} 张券。` : '凭券活动需要发放券码或投放领券活动，用户才能使用。'}{promotion.status !== 'Active' ? ' 活动发布并生效后，券才能启用。' : ''}</p>
        <div className="detail-actions">
          {canQueryCoupons ? <button type="button" className="button button--quiet" onClick={onShowCoupons}>查看券列表</button> : null}
          {canIssueCoupons && !['Ended', 'Archived'].includes(promotion.status) ? <button type="button" className="button button--dark" onClick={onIssueCoupons}><Icon name="ticket" />发放优惠券</button> : null}
          {onCreateCampaign && !['Ended', 'Archived'].includes(promotion.status) ? <button type="button" className="button button--quiet" onClick={onCreateCampaign}>投放领券活动</button> : null}
        </div>
      </div>
    </section> : null}

    <p className="muted-copy"><Icon name="alert" />已发布的活动定义不可修改；暂停或结束后可「重新编辑」，或「复制为新活动」保留原记录。<CopyButton value={promotion.id} label="复制活动 ID" /></p>
  </div>;
}
