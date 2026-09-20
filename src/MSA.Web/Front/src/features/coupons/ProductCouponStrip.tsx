import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { appliesToProduct } from '../promotions/describe';
import { isDemoMode } from '../../shared/config/api';
import { Icon } from '../../shared/ui/Icon';
import { claimableCampaignsQueryKey, listClaimableCampaigns, useClaimCoupon } from './campaigns-api';
import { ClaimCouponCard } from './ClaimCouponCard';
import { demoCampaigns } from './demo-coupons';

/** Claimable coupons usable on this product, so shoppers can grab them without leaving the page. */
export function ProductCouponStrip({ product }: { product: { id: string; categoryId?: string | null; skus: string[] } }) {
  const demoMode = isDemoMode();
  const { claim, pendingId } = useClaimCoupon();
  const campaigns = useQuery({ queryKey: claimableCampaignsQueryKey, queryFn: listClaimableCampaigns, enabled: !demoMode, staleTime: 30_000 });
  const matching = (demoMode ? demoCampaigns : campaigns.data ?? [])
    .filter((campaign) => campaign.claimState !== 'Ended' && appliesToProduct(campaign.targets, product));
  if (!matching.length) return null;

  return (
    <section className="product-coupons" aria-label="可领优惠券">
      <header>
        <strong><Icon name="ticket" />领券更优惠</strong>
        <Link to={`/coupon-center${demoMode ? '?demo=1' : ''}`}>全部 {matching.length} 张<Icon name="chevron-right" /></Link>
      </header>
      <div className="product-coupons__list">
        {matching.slice(0, 3).map((campaign) => (
          <ClaimCouponCard key={campaign.id} campaign={campaign} compact pending={pendingId === campaign.id} onClaim={claim} onStarted={() => void campaigns.refetch()} />
        ))}
      </div>
    </section>
  );
}
