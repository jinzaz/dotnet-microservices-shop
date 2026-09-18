import { useQuery } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { useBasket, useBasketActions } from '../features/basket/basket-store';
import { categoriesQuery } from '../features/catalog/catalog-api';
import { listMyCoupons, myCouponsQueryKey } from '../features/coupons/coupons-api';
import { demoCoupons } from '../features/coupons/demo-coupons';
import type { WalletCoupon } from '../features/coupons/types';
import { AccountSidebar, type AccountSection } from '../features/profile/AccountSidebar';
import { demoAccountSnapshot } from '../features/profile/demo-account';
import { getProfile } from '../features/profile/profile-api';
import { benefitValue, conditionText, describeTargets } from '../features/promotions/describe';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatDateTime } from '../shared/utils/format';

type Filter = 'available' | 'used' | 'expired';

const filters: ReadonlyArray<{ value: Filter; label: string }> = [
  { value: 'available', label: '可使用' },
  { value: 'used', label: '已使用' },
  { value: 'expired', label: '已失效' },
];

function bucketOf(coupon: WalletCoupon): Filter {
  if (coupon.status === 'Available' || coupon.status === 'NotStarted') return 'available';
  return coupon.status === 'Used' ? 'used' : 'expired';
}

const EXPIRING_SOON_MS = 3 * 24 * 60 * 60 * 1000;

export default function CouponsPage() {
  const demoMode = isDemoMode();
  const [activeFilter, setActiveFilter] = useState<Filter>('available');
  const [redeemCode, setRedeemCode] = useState('');
  const [redeeming, setRedeeming] = useState(false);
  const navigate = useNavigate();
  const toast = useToast();
  const { logout } = useAuth();
  const basket = useBasket();
  const basketActions = useBasketActions();
  const profileQuery = useQuery({ queryKey: ['identity', 'profile'], queryFn: getProfile, enabled: !demoMode, staleTime: 60_000 });
  const couponsQuery = useQuery({ queryKey: myCouponsQueryKey, queryFn: listMyCoupons, enabled: !demoMode, staleTime: 30_000 });
  const categories = useQuery({ ...categoriesQuery, enabled: !demoMode });
  const categoryNames = useMemo(() => new Map((categories.data ?? []).map((category) => [category.id, category.name])), [categories.data]);

  const coupons = demoMode ? demoCoupons : couponsQuery.data ?? [];
  const counts = useMemo(() => {
    const result: Record<Filter, number> = { available: 0, used: 0, expired: 0 };
    coupons.forEach((coupon) => { result[bucketOf(coupon)] += 1; });
    return result;
  }, [coupons]);
  const visibleCoupons = coupons.filter((coupon) => bucketOf(coupon) === activeFilter);
  const profile = profileQuery.data ?? demoAccountSnapshot.profile;
  const demoSuffix = demoMode ? '?demo=1' : '';

  const goToAccountSection = (section: AccountSection) => {
    if (section === 'coupons') return;
    navigate(`/account${demoSuffix}${section === 'overview' ? '' : `#${section}`}`);
  };

  // Typed codes live in the basket; Ordering validates them against the actual items at checkout.
  const submitCode = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const code = redeemCode.trim().toUpperCase();
    if (!code) return;
    if (basket.couponCodes.includes(code)) {
      toast('该优惠码已在购物袋中', { tone: 'info' });
      return;
    }
    setRedeeming(true);
    try {
      await basketActions.setCoupons([...basket.couponCodes, code]);
      setRedeemCode('');
      toast('优惠码已加入购物袋，结算时自动计算优惠', { action: { label: '去购物袋', onClick: () => navigate(`/cart${demoSuffix}`) } });
    } catch (error) {
      toast(getErrorMessage(error), { tone: 'error' });
    } finally {
      setRedeeming(false);
    }
  };

  const handleLogout = async () => {
    if (demoMode) {
      navigate('/');
      return;
    }
    try {
      await logout('/');
    } catch (error) {
      toast(getErrorMessage(error, '退出登录失败，请稍后重试。'), { tone: 'error' });
    }
  };

  return (
    <CommerceShell>
      <div className="account-page coupons-account-page">
        <AccountSidebar
          profile={profile}
          activeSection="coupons"
          onSectionChange={goToAccountSection}
          onLogout={() => void handleLogout()}
          couponsPath={`/coupons${demoSuffix}`}
          ordersPath={`/orders${demoSuffix}`}
        />
        <section className="coupon-wallet">
          <div className="coupon-wallet__heading">
            <div>
              <h1>我的优惠券</h1>
              <p>发放到账户的券可在结算时直接勾选；收到的优惠码请在下方输入。</p>
              <strong>可用 <span>{counts.available}</span> 张</strong>
            </div>
            <form className="coupon-redeem" onSubmit={(event) => void submitCode(event)}>
              <label className="sr-only" htmlFor="coupon-code">输入优惠码</label>
              <input
                id="coupon-code"
                value={redeemCode}
                onChange={(event) => setRedeemCode(event.target.value)}
                placeholder="输入优惠码"
                maxLength={80}
                autoComplete="off"
              />
              <Button type="submit" variant="secondary" loading={redeeming}>使用</Button>
            </form>
          </div>

          {demoMode ? <p className="demo-notice" role="status">开发预览数据 · 筛选与使用可正常操作</p> : null}

          <nav className="coupon-filters" aria-label="优惠券分类">
            {filters.map((filter) => (
              <button
                key={filter.value}
                type="button"
                className={activeFilter === filter.value ? 'is-active' : undefined}
                aria-pressed={activeFilter === filter.value}
                onClick={() => setActiveFilter(filter.value)}
              >
                {filter.label} {counts[filter.value]}
              </button>
            ))}
          </nav>

          {!demoMode && couponsQuery.isPending ? <div className="orders-state" role="status"><span className="page-spinner" />正在加载优惠券…</div> : null}
          {!demoMode && couponsQuery.isError ? (
            <div className="orders-state orders-state--error" role="alert">
              <Icon name="alert-circle" />
              <h2>优惠券加载失败</h2>
              <p>{getErrorMessage(couponsQuery.error)}</p>
              <Button type="button" size="small" onClick={() => void couponsQuery.refetch()}>重新加载</Button>
            </div>
          ) : null}

          {visibleCoupons.length ? (
            <div className="coupon-list">
              {visibleCoupons.map((coupon) => {
                const bucket = bucketOf(coupon);
                const expiringSoon = coupon.status === 'Available' && Date.parse(coupon.validUntilUtc) - Date.now() < EXPIRING_SOON_MS;
                return (
                  <article className={`coupon-row coupon-row--${bucket}`} key={coupon.couponId}>
                    <div className="coupon-row__value">
                      <strong>{benefitValue(coupon.benefits)}</strong>
                      <span>{conditionText(coupon.rules)}</span>
                    </div>
                    <div className="coupon-row__details">
                      <h2>{coupon.promotionName}</h2>
                      <dl>
                        <div><dt>有效期</dt><dd>{formatDateTime(coupon.validFromUtc)} – {formatDateTime(coupon.validUntilUtc)} {expiringSoon ? <em>即将到期</em> : null}</dd></div>
                        <div><dt>适用范围</dt><dd>{describeTargets(coupon.targets, categoryNames)}</dd></div>
                        <div><dt>{coupon.isAssigned ? '券号' : '优惠码'}</dt><dd>{coupon.maskedCode} · 已用 {coupon.usedCount}/{coupon.perUserLimit} 次{coupon.isAssigned ? ' · 结算时直接勾选' : ''}</dd></div>
                      </dl>
                    </div>
                    {coupon.status === 'Available' ? (
                      <Link className="coupon-row__use" to={`/products${demoSuffix}`}>去使用</Link>
                    ) : (
                      <span className="coupon-row__status">
                        {coupon.status === 'NotStarted' ? '未开始' : coupon.status === 'Used' ? '已使用' : coupon.status === 'Unavailable' ? '已停用' : '已过期'}
                      </span>
                    )}
                  </article>
                );
              })}
            </div>
          ) : (demoMode || couponsQuery.isSuccess) ? (
            <div className="account-empty coupon-empty">
              <Icon name="ticket" />
              <p>{activeFilter === 'available' ? '暂无可用优惠券，参与活动或输入优惠码即可享受优惠。' : '当前分类下没有优惠券。'}</p>
            </div>
          ) : null}
        </section>
      </div>
      <ServicePromises />
    </CommerceShell>
  );
}
