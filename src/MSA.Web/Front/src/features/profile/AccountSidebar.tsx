import { Link } from 'react-router-dom';
import { Icon, type IconName } from '../../shared/ui/Icon';
import type { UserProfile } from './types';

type AccountSection = 'overview' | 'profile' | 'security' | 'addresses' | 'points' | 'coupons';

type AccountSidebarProps = {
  profile: UserProfile;
  activeSection: AccountSection;
  onSectionChange: (section: AccountSection) => void;
  onLogout: () => void;
  ordersPath?: string;
  couponsPath?: string;
};

const sections: ReadonlyArray<{ id: AccountSection; label: string; icon: IconName }> = [
  { id: 'overview', label: '账户概览', icon: 'home' },
  { id: 'profile', label: '个人资料', icon: 'user' },
  { id: 'security', label: '账户安全', icon: 'lock' },
  { id: 'addresses', label: '收货地址', icon: 'map-pin' },
  { id: 'points', label: '我的积分', icon: 'star' },
];

function initials(name: string): string {
  return name.trim().slice(0, 1).toLocaleUpperCase('zh-CN') || 'M';
}

export function AccountSidebar({
  profile,
  activeSection,
  onSectionChange,
  onLogout,
  ordersPath = '/orders',
  couponsPath = '/coupons',
}: AccountSidebarProps) {
  return (
    <aside className="account-sidebar">
      <div className="account-identity">
        <span className="account-avatar">{initials(profile.displayName)}</span>
        <strong>你好，{profile.displayName}</strong>
        <span>{profile.email}</span>
      </div>
      <nav aria-label="账户导航">
        {sections.map((section) => (
          <a
            key={section.id}
            href={section.id === 'overview' ? '#account-overview' : `#${section.id}`}
            className={activeSection === section.id ? 'is-active' : undefined}
            onClick={() => onSectionChange(section.id)}
          >
            <Icon name={section.icon} />
            {section.label}
          </a>
        ))}
        <Link to={couponsPath} className={activeSection === 'coupons' ? 'is-active' : undefined}>
          <Icon name="ticket" />
          我的优惠券
        </Link>
        <Link to={ordersPath}>
          <Icon name="clipboard" />
          我的订单
        </Link>
        <Link to={ordersPath.replace('/orders', '/after-sales')}>
          <Icon name="refresh" />
          退换 / 售后
        </Link>
      </nav>
      <button className="account-logout" type="button" onClick={onLogout}>
        <Icon name="logout" />
        退出登录
      </button>
    </aside>
  );
}

export type { AccountSection };
