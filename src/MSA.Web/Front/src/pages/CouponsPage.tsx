import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { listMyCoupons, redeemCoupon } from '../features/coupons/coupons-api';
import { demoCoupons } from '../features/coupons/demo-coupons';
import type { CouponStatus, CustomerCoupon } from '../features/coupons/types';
import { AccountSidebar, type AccountSection } from '../features/profile/AccountSidebar';
import { demoAccountSnapshot } from '../features/profile/demo-account';
import { getProfile } from '../features/profile/profile-api';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatDateTime } from '../shared/utils/format';

const filters: ReadonlyArray<{ value: CouponStatus; label: string }> = [
  { value: 'available', label: '可使用' },
  { value: 'used', label: '已使用' },
  { value: 'expired', label: '已过期' },
];

function createRedeemedCoupon(code: string): CustomerCoupon {
  return {
    id: `local-${Date.now()}`,
    title: '兑换优惠券',
    valueLabel: '¥15',
    conditionLabel: '满 159 可用',
    scopeLabel: '全场商品（特定商品除外）',
    code: code.toLocaleUpperCase('zh-CN'),
    status: 'available',
    validFromUtc: new Date().toISOString(),
    validUntilUtc: '2026-12-31T23:59:59+08:00',
    productId: 'editorial-pour-over',
  };
}

export default function CouponsPage() {
  const demoMode = isDemoMode();
  const [activeFilter, setActiveFilter] = useState<CouponStatus>('available');
  const [localCoupons, setLocalCoupons] = useState<CustomerCoupon[]>(demoCoupons);
  const [redeemCode, setRedeemCode] = useState('');
  const [notice, setNotice] = useState('');
  const [redeeming, setRedeeming] = useState(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { logout } = useAuth();
  const profileQuery = useQuery({
    queryKey: ['identity', 'profile'],
    queryFn: getProfile,
    enabled: !demoMode,
    staleTime: 60_000,
  });
  const couponsQuery = useQuery({
    queryKey: ['catalog', 'customer-coupons'],
    queryFn: () => listMyCoupons(),
    enabled: !demoMode,
    staleTime: 30_000,
    retry: 1,
  });

  const usingFallback = demoMode || couponsQuery.isError;
  const coupons = usingFallback ? localCoupons : couponsQuery.data ?? [];
  const visibleCoupons = coupons.filter((coupon) => coupon.status === activeFilter);
  const counts = useMemo(() => Object.fromEntries(
    filters.map((filter) => [filter.value, coupons.filter((coupon) => coupon.status === filter.value).length]),
  ) as Record<CouponStatus, number>, [coupons]);
  const profile = profileQuery.data ?? demoAccountSnapshot.profile;
  const demoSuffix = demoMode ? '?demo=1' : '';

  const goToAccountSection = (section: AccountSection) => {
    if (section === 'coupons') return;
    navigate(`/account${demoSuffix}${section === 'overview' ? '' : `#${section}`}`);
  };

  const submitRedemption = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const code = redeemCode.trim();
    if (!code) {
      setNotice('请输入优惠码。');
      return;
    }
    setRedeeming(true);
    setNotice('');
    try {
      if (usingFallback) {
        setLocalCoupons((current) => [createRedeemedCoupon(code), ...current]);
        setNotice('兑换成功，优惠券已加入当前券包。');
      } else {
        await redeemCoupon(code);
        await queryClient.invalidateQueries({ queryKey: ['catalog', 'customer-coupons'] });
        setNotice('兑换成功，优惠券已加入券包。');
      }
      setRedeemCode('');
      setActiveFilter('available');
    } catch (error) {
      setNotice(getErrorMessage(error));
    } finally {
      setRedeeming(false);
    }
  };

  const copyCode = async (code: string) => {
    try {
      await navigator.clipboard.writeText(code);
      setNotice(`优惠码 ${code} 已复制。`);
    } catch {
      setNotice(`优惠码：${code}`);
    }
  };

  const handleLogout = async () => {
    if (demoMode) {
      navigate('/');
      return;
    }

    setNotice('');
    try {
      await logout('/');
    } catch (error) {
      setNotice(getErrorMessage(error, '退出登录失败，请稍后重试。'));
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
              <p>查看可用、已使用与已过期的优惠权益。</p>
              <strong>可用 <span>{counts.available}</span> 张</strong>
            </div>
            <form className="coupon-redeem" onSubmit={(event) => void submitRedemption(event)}>
              <label className="sr-only" htmlFor="coupon-code">输入优惠码</label>
              <input
                id="coupon-code"
                value={redeemCode}
                onChange={(event) => setRedeemCode(event.target.value)}
                placeholder="输入优惠码"
                maxLength={80}
              />
              <Button type="submit" variant="secondary" loading={redeeming}>兑换</Button>
            </form>
          </div>

          {usingFallback ? (
            <p className="demo-notice" role="status">
              {demoMode ? '开发预览数据 · 筛选、复制与兑换可正常操作' : '用户券包接口待后端接入，当前展示可操作的预览数据'}
            </p>
          ) : null}
          {notice ? <p className="coupon-notice" role="status">{notice}</p> : null}

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

          {visibleCoupons.length ? (
            <div className="coupon-list">
              {visibleCoupons.map((coupon) => (
                <article className={`coupon-row coupon-row--${coupon.status}`} key={coupon.id}>
                  <div className="coupon-row__value">
                    <strong>{coupon.valueLabel}</strong>
                    <span>{coupon.conditionLabel}</span>
                  </div>
                  <div className="coupon-row__details">
                    <h2>{coupon.title}</h2>
                    <dl>
                      <div><dt>有效期</dt><dd>{formatDateTime(coupon.validFromUtc)} – {formatDateTime(coupon.validUntilUtc)} {coupon.isExpiringSoon ? <em>即将到期</em> : null}</dd></div>
                      <div><dt>适用范围</dt><dd>{coupon.scopeLabel}</dd></div>
                      <div><dt>优惠码</dt><dd>{coupon.code} <button type="button" onClick={() => void copyCode(coupon.code)}><Icon name="copy" />复制</button></dd></div>
                    </dl>
                  </div>
                  {coupon.status === 'available' ? (
                    <Link className="coupon-row__use" to={`/products/${coupon.productId ?? 'editorial-pour-over'}${demoSuffix}`}>去使用</Link>
                  ) : <span className="coupon-row__status">{coupon.status === 'used' ? '已使用' : '已过期'}</span>}
                </article>
              ))}
            </div>
          ) : (
            <div className="account-empty coupon-empty"><Icon name="ticket" /><p>当前分类下没有优惠券。</p></div>
          )}
        </section>
      </div>
      <ServicePromises />
    </CommerceShell>
  );
}
