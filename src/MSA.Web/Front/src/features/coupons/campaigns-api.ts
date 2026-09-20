import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../app/auth/AuthProvider';
import { ApiError, getErrorMessage } from '../../shared/api/api-error';
import { apiRequest } from '../../shared/api/http';
import { isDemoMode, serviceRoutes } from '../../shared/config/api';
import { useToast } from '../../shared/ui/Toast';
import { myCouponsQueryKey } from './coupons-api';
import type { ClaimableCampaign, ClaimResult, ClaimState } from './types';

export const claimableCampaignsQueryKey = ['catalog', 'coupon-campaigns'] as const;

export function listClaimableCampaigns(): Promise<ClaimableCampaign[]> {
  return apiRequest<ClaimableCampaign[]>(`${serviceRoutes.catalog}/coupon-campaigns`);
}

export function getClaimableCampaign(id: string): Promise<ClaimableCampaign> {
  return apiRequest<ClaimableCampaign>(`${serviceRoutes.catalog}/coupon-campaigns/${encodeURIComponent(id)}`);
}

export function claimCoupon(id: string): Promise<ClaimResult> {
  return apiRequest<ClaimResult>(`${serviceRoutes.catalog}/coupon-campaigns/${encodeURIComponent(id)}/claim`, { method: 'POST' });
}

const rejectionMessages: Record<string, string> = {
  SoldOut: '手慢了，券已经被抢光',
  DailySoldOut: '今天的券已抢光，明天再来',
  UserLimitReached: '你已经领过这张券了',
  UserDailyLimitReached: '今天已经领过了，明天再来',
  NotStarted: '还没到开抢时间',
  Ended: '领券活动已结束',
  Unavailable: '这张券暂时不可领取',
};

/** Claims a coupon; anonymous shoppers are sent to login and come back to the same page. */
export function useClaimCoupon() {
  const client = useQueryClient();
  const toast = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated } = useAuth();
  const demoMode = isDemoMode(location.search);

  const mutation = useMutation({
    mutationFn: (campaign: ClaimableCampaign) => demoMode
      ? Promise.resolve<ClaimResult>({ couponId: 'demo', maskedCode: '****DEMO', validFromUtc: new Date().toISOString(), validUntilUtc: new Date(Date.now() + 7 * 86_400_000).toISOString(), remainingQuantity: null, myClaimedCount: campaign.myClaimedCount + 1, perUserLimit: campaign.perUserLimit })
      : claimCoupon(campaign.id),
    onSuccess: (result) => {
      toast(result.myClaimedCount < result.perUserLimit ? `领取成功，还可再领 ${result.perUserLimit - result.myClaimedCount} 张` : '领取成功，已放入我的优惠券', {
        action: { label: '查看', onClick: () => navigate(`/coupons${demoMode ? '?demo=1' : ''}`) },
      });
    },
    onError: (error) => {
      const reason = error instanceof ApiError ? error.problem?.reason : undefined;
      toast(reason ? rejectionMessages[reason] ?? getErrorMessage(error) : getErrorMessage(error, '领取失败，请稍后重试。'), { tone: reason ? 'info' : 'error' });
    },
    // Success or rejection, counts changed; refresh so every card shows the real remaining stock.
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: claimableCampaignsQueryKey }),
      client.invalidateQueries({ queryKey: myCouponsQueryKey }),
    ]),
  });

  const claim = (campaign: ClaimableCampaign) => {
    if (!isAuthenticated && !demoMode) {
      const redirect = `${location.pathname}${location.search}${location.hash}`;
      toast('登录后即可领取', { tone: 'info' });
      navigate(`/login?${new URLSearchParams({ redirect })}`);
      return;
    }
    mutation.mutate(campaign);
  };

  return { claim, pendingId: mutation.isPending ? mutation.variables?.id : undefined };
}

export function claimStateLabel(state: ClaimState): string {
  switch (state) {
    case 'Claimable': return '立即领取';
    case 'NotStarted': return '即将开抢';
    case 'SoldOut': return '已抢光';
    case 'DailySoldOut': return '今日已抢光';
    case 'LimitReached': return '已领取';
    case 'DailyLimitReached': return '今日已领';
    default: return '已结束';
  }
}
