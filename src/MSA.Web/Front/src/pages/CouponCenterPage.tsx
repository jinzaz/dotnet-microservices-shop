import { useQuery } from '@tanstack/react-query';
import { useCallback, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { categoriesQuery } from '../features/catalog/catalog-api';
import { claimableCampaignsQueryKey, getClaimableCampaign, listClaimableCampaigns, useClaimCoupon } from '../features/coupons/campaigns-api';
import { ClaimCouponCard } from '../features/coupons/ClaimCouponCard';
import { demoCampaigns } from '../features/coupons/demo-coupons';
import type { ClaimableCampaign } from '../features/coupons/types';
import { ApiError, getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';

type Filter = 'all' | 'claimable' | 'upcoming' | 'mine';

const filters: ReadonlyArray<{ value: Filter; label: string; match: (campaign: ClaimableCampaign) => boolean }> = [
  { value: 'all', label: '全部', match: () => true },
  { value: 'claimable', label: '可领取', match: (campaign) => campaign.claimState === 'Claimable' },
  { value: 'upcoming', label: '即将开抢', match: (campaign) => campaign.claimState === 'NotStarted' },
  { value: 'mine', label: '我已领', match: (campaign) => campaign.myClaimedCount > 0 },
];

export default function CouponCenterPage() {
  const { campaignId } = useParams();
  const demoMode = isDemoMode();
  const { isAuthenticated } = useAuth();
  const [filter, setFilter] = useState<Filter>('all');
  const { claim, pendingId } = useClaimCoupon();
  const listQuery = useQuery({ queryKey: claimableCampaignsQueryKey, queryFn: listClaimableCampaigns, enabled: !demoMode, staleTime: 15_000 });
  // A shared link may point at an unlisted campaign, so it is fetched on its own.
  const linkedQuery = useQuery({
    queryKey: [...claimableCampaignsQueryKey, campaignId],
    queryFn: () => getClaimableCampaign(campaignId!),
    enabled: Boolean(campaignId) && !demoMode,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  });
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const categoryNames = useMemo(() => new Map((categories.data ?? []).map((category) => [category.id, category.name])), [categories.data]);
  const refresh = useCallback(() => { void listQuery.refetch(); if (campaignId) void linkedQuery.refetch(); }, [listQuery, linkedQuery, campaignId]);

  const campaigns = demoMode ? demoCampaigns : listQuery.data ?? [];
  const linked = demoMode ? demoCampaigns.find((item) => item.id === campaignId) : linkedQuery.data;
  const others = campaigns.filter((item) => item.id !== linked?.id);
  const current = filters.find((item) => item.value === filter)!;
  const visible = others.filter(current.match);
  const claimableCount = campaigns.filter((item) => item.claimState === 'Claimable').length;
  const demoSuffix = demoMode ? '?demo=1' : '';

  const card = (campaign: ClaimableCampaign, highlighted = false) => (
    <ClaimCouponCard
      key={campaign.id}
      campaign={campaign}
      categoryNames={categoryNames}
      highlighted={highlighted}
      pending={pendingId === campaign.id}
      onClaim={claim}
      onStarted={refresh}
    />
  );

  return (
    <CommerceShell>
      <div className="coupon-center">
        <header className="coupon-center__hero">
          <div>
            <p className="coupon-center__eyebrow"><Icon name="gift" />领券中心</p>
            <h1>先领券，再下单</h1>
            <p>{claimableCount ? `当前有 ${claimableCount} 张券可领，领取后自动放入「我的优惠券」，结算时直接勾选。` : '领取后的券自动放入「我的优惠券」，结算时直接勾选。'}</p>
          </div>
          <Link className="coupon-center__wallet" to={isAuthenticated || demoMode ? `/coupons${demoSuffix}` : '/login?redirect=%2Fcoupons'}>
            <Icon name="ticket" />我的优惠券<Icon name="chevron-right" />
          </Link>
        </header>

        {demoMode ? <p className="demo-notice" role="status">开发预览数据 · 领取为模拟操作</p> : null}

        {campaignId ? (
          <section className="coupon-center__linked" aria-label="分享给你的券">
            <h2>分享给你的券</h2>
            {linked ? card(linked, true) : null}
            {!demoMode && linkedQuery.isPending ? <div className="orders-state" role="status"><span className="page-spinner" />正在读取…</div> : null}
            {!demoMode && linkedQuery.isError ? (
              <div className="orders-state orders-state--error" role="alert">
                <Icon name="alert-circle" />
                <p>{linkedQuery.error instanceof ApiError && linkedQuery.error.status === 404 ? '这张券不存在或活动已下线。' : getErrorMessage(linkedQuery.error)}</p>
              </div>
            ) : null}
          </section>
        ) : null}

        <nav className="coupon-filters" aria-label="领券筛选">
          {filters.map((item) => {
            const count = others.filter(item.match).length;
            return (
              <button key={item.value} type="button" className={filter === item.value ? 'is-active' : undefined} aria-pressed={filter === item.value} onClick={() => setFilter(item.value)}>
                {item.label} {count}
              </button>
            );
          })}
        </nav>

        {!demoMode && listQuery.isPending ? <div className="orders-state" role="status"><span className="page-spinner" />正在加载可领的券…</div> : null}
        {!demoMode && listQuery.isError ? (
          <div className="orders-state orders-state--error" role="alert">
            <Icon name="alert-circle" />
            <h2>领券中心加载失败</h2>
            <p>{getErrorMessage(listQuery.error)}</p>
            <Button type="button" size="small" onClick={() => void listQuery.refetch()}>重新加载</Button>
          </div>
        ) : null}

        {visible.length ? (
          <div className="coupon-center__grid">{visible.map((campaign) => card(campaign))}</div>
        ) : (demoMode || listQuery.isSuccess) ? (
          <div className="account-empty coupon-empty">
            <Icon name="ticket" />
            <p>{filter === 'all' ? '暂时没有可领的券，过段时间再来看看。' : filter === 'mine' ? '你还没有在这里领过券。' : '当前分类下没有券。'}</p>
            <Link className="text-link" to={`/products${demoSuffix}`}>先去逛逛 <Icon name="arrow-right" /></Link>
          </div>
        ) : null}
      </div>
      <ServicePromises />
    </CommerceShell>
  );
}
