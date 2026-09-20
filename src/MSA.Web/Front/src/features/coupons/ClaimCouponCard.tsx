import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { benefitValue, conditionText, describePromotion, describeTargets } from '../promotions/describe';
import { formatDateTime } from '../../shared/utils/format';
import { claimStateLabel } from './campaigns-api';
import type { ClaimableCampaign } from './types';

const HOUR = 3_600_000;

/** Re-renders every second while a countdown is visible, and stops once there is nothing to count. */
function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return undefined;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active]);
  return now;
}

function countdown(ms: number) {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  const parts = [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60];
  return parts.map((part) => String(part).padStart(2, '0')).join(':');
}

function startLabel(startsOn: number) {
  const date = new Date(startsOn);
  const time = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
  const today = new Date();
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  if (date.toDateString() === today.toDateString()) return `今天 ${time} 开抢`;
  if (date.toDateString() === tomorrow.toDateString()) return `明天 ${time} 开抢`;
  return `${date.getMonth() + 1}月${date.getDate()}日 ${time} 开抢`;
}

export function validityLabel(campaign: ClaimableCampaign) {
  return campaign.validityMode === 'RelativeToClaim'
    ? `领取后 ${campaign.validDays} 天内有效`
    : `${formatDateTime(campaign.validFromUtc!)} – ${formatDateTime(campaign.validUntilUtc!)}`;
}

export function ClaimCouponCard({ campaign, categoryNames, compact = false, highlighted = false, pending, onClaim, onStarted }: {
  campaign: ClaimableCampaign;
  categoryNames?: Map<string, string>;
  compact?: boolean;
  highlighted?: boolean;
  pending: boolean;
  onClaim: (campaign: ClaimableCampaign) => void;
  /** Called when a countdown reaches zero so the list can refresh its state from the server. */
  onStarted?: () => void;
}) {
  const startsOn = Date.parse(campaign.claimStartsOnUtc);
  const upcoming = campaign.claimState === 'NotStarted';
  const now = useNow(upcoming && startsOn - Date.now() < 24 * HOUR);
  const remainingMs = startsOn - now;

  const started = useRef(false);
  useEffect(() => {
    if (!upcoming || remainingMs > 0 || started.current) return;
    started.current = true;
    onStarted?.();
  }, [upcoming, remainingMs, onStarted]);

  const total = campaign.totalQuantity;
  const claimedRatio = total ? Math.min(1, campaign.claimedQuantity / total) : 0;
  const claimable = campaign.claimState === 'Claimable';
  const owned = campaign.myClaimedCount > 0;
  const buttonLabel = upcoming
    ? remainingMs < 24 * HOUR ? `${countdown(remainingMs)} 后开抢` : startLabel(startsOn)
    : claimable && owned ? '再领一张' : claimStateLabel(campaign.claimState);

  return (
    <article className={`claim-coupon${compact ? ' claim-coupon--compact' : ''}${claimable || upcoming ? '' : ' is-muted'}${highlighted ? ' is-highlighted' : ''}`} id={`campaign-${campaign.id}`}>
      <div className="claim-coupon__value">
        <strong>{benefitValue(campaign.benefits)}</strong>
        <span>{conditionText(campaign.rules)}</span>
      </div>
      <div className="claim-coupon__body">
        <h3>{campaign.title}</h3>
        {!compact ? <p>{campaign.description || (describePromotion(campaign.rules, campaign.benefits) === campaign.title ? campaign.promotionName : describePromotion(campaign.rules, campaign.benefits))}</p> : null}
        <small>{validityLabel(campaign)}{!compact ? ` · ${describeTargets(campaign.targets, categoryNames)}` : ''}</small>
        {total !== null && !compact ? (
          <div className="claim-coupon__stock" aria-label={`已抢 ${Math.round(claimedRatio * 100)}%`}>
            <span><i style={{ width: `${claimedRatio * 100}%` }} /></span>
            <small>{campaign.claimState === 'SoldOut' ? '已抢光' : `已抢 ${Math.round(claimedRatio * 100)}%`}</small>
          </div>
        ) : null}
        {!compact && (campaign.perUserLimit > 1 || owned) ? <small className="claim-coupon__mine">每人限领 {campaign.perUserLimit} 张{owned ? ` · 已领 ${campaign.myClaimedCount} 张` : ''}</small> : null}
      </div>
      <div className="claim-coupon__action">
        {campaign.claimState === 'LimitReached' ? (
          <Link className="claim-coupon__use" to="/products">去使用</Link>
        ) : (
          <button type="button" className="claim-coupon__button" disabled={!claimable || pending} aria-busy={pending} onClick={() => onClaim(campaign)}>
            {pending ? '领取中…' : buttonLabel}
          </button>
        )}
      </div>
    </article>
  );
}
