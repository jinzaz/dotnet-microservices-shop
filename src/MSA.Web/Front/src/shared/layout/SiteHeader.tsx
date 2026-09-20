import { useEffect, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../app/auth/AuthProvider';
import { useBasket } from '../../features/basket/basket-store';
import { isDemoMode } from '../config/api';
import { Icon } from '../ui/Icon';

const navigation = [
  { label: '首页', to: '/', match: (pathname: string) => pathname === '/' },
  { label: '全部商品', to: '/products', match: (pathname: string) => pathname.startsWith('/products') },
  { label: '领券中心', to: '/coupon-center', match: (pathname: string) => pathname.startsWith('/coupon-center') },
  { label: '我的订单', to: '/orders', match: (pathname: string) => pathname.startsWith('/orders') || pathname.startsWith('/after-sales') || pathname.startsWith('/payment') },
  { label: '我的账户', to: '/account', match: (pathname: string) => ['/account', '/coupons'].some((path) => pathname.startsWith(path)) },
] as const;

export function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated, user, login } = useAuth();
  const { itemCount } = useBasket();
  const demoMode = isDemoMode(location.search);
  const hasAccountAccess = isAuthenticated || demoMode;
  const suffix = demoMode ? '?demo=1' : '';

  // Close transient menus whenever the route changes.
  useEffect(() => {
    setMenuOpen(false);
    setSearchOpen(false);
  }, [location.pathname]);

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const query = search.trim();
    const params = new URLSearchParams();
    if (query) params.set('q', query);
    if (demoMode) params.set('demo', '1');
    navigate(`/products${params.size ? `?${params}` : ''}`);
    setSearchOpen(false);
  };

  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="brand" to={`/${suffix}`} aria-label="MSA Store 首页">MSA Store</Link>

        <nav className={`primary-nav ${menuOpen ? 'is-open' : ''}`} aria-label="主导航">
          {navigation.map((item) => (
            <Link
              key={item.label}
              to={`${item.to}${suffix}`}
              onClick={() => setMenuOpen(false)}
              className={item.match(location.pathname) ? 'is-active' : undefined}
              aria-current={item.match(location.pathname) ? 'page' : undefined}
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="site-header__actions">
          <button
            className="header-action"
            type="button"
            onClick={() => setSearchOpen((current) => !current)}
            aria-expanded={searchOpen}
            aria-controls="site-search"
          >
            <Icon name="search" />
            <span>搜索</span>
          </button>
          {hasAccountAccess ? (
            <Link className="header-action" to={`/account${suffix}`} title={user?.email ?? undefined}>
              <Icon name="user" />
              <span>{user?.name ? user.name.slice(0, 8) : '我的账户'}</span>
            </Link>
          ) : (
            <button className="header-action" type="button" onClick={() => login(`${location.pathname}${location.search}`)}>
              <Icon name="user" />
              <span>登录</span>
            </button>
          )}
          <Link className="header-action header-cart" to={`/cart${suffix}`} aria-label={`购物袋，共 ${itemCount} 件商品`}>
            <Icon name="bag" />
            <span>购物袋</span>
            {itemCount ? <strong className="cart-count" key={itemCount}>{itemCount > 99 ? '99+' : itemCount}</strong> : null}
          </Link>
          <button
            className="menu-toggle"
            type="button"
            onClick={() => setMenuOpen((current) => !current)}
            aria-expanded={menuOpen}
            aria-label={menuOpen ? '关闭菜单' : '打开菜单'}
          >
            <Icon name={menuOpen ? 'close' : 'menu'} />
          </button>
        </div>
      </div>

      {searchOpen ? (
        <div className="site-search" id="site-search">
          <form onSubmit={submitSearch} role="search">
            <Icon name="search" />
            <label className="sr-only" htmlFor="global-search">搜索商品</label>
            <input
              id="global-search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => { if (event.key === 'Escape') setSearchOpen(false); }}
              placeholder="搜索商品名称或 SKU"
              maxLength={80}
              autoFocus
            />
            <button type="submit">搜索</button>
          </form>
        </div>
      ) : null}
    </header>
  );
}

export function MinimalHeader() {
  return (
    <header className="site-header site-header--minimal">
      <div className="site-header__inner">
        <Link className="brand" to="/">MSA Store</Link>
        <Link className="back-link" to="/">
          <Icon name="arrow-right" />
          返回首页
        </Link>
      </div>
    </header>
  );
}
