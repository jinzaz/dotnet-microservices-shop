import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../app/auth/AuthProvider';
import { Icon } from '../ui/Icon';

const navigation = [
  { label: '首页', to: '/' },
  { label: '精选商品', to: '/#featured' },
  { label: '新品', to: '/#new' },
  { label: '我的账户', to: '/account' },
] as const;

export function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated } = useAuth();

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const query = search.trim();
    navigate(query ? `/?q=${encodeURIComponent(query)}#featured` : '/#featured');
    setSearchOpen(false);
    setMenuOpen(false);
  };

  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="brand" to="/" aria-label="MSA Store 首页">MSA Store</Link>

        <nav className={`primary-nav ${menuOpen ? 'is-open' : ''}`} aria-label="主导航">
          {navigation.map((item) => {
            const itemHash = item.to.includes('#') ? item.to.slice(item.to.indexOf('#')) : '';
            const isActive = item.to === '/'
              ? location.pathname === '/' && !location.hash
              : itemHash
                ? location.pathname === '/' && location.hash === itemHash
                : location.pathname.startsWith(item.to);

            return (
              <Link
                key={item.label}
                to={item.to}
                onClick={() => setMenuOpen(false)}
                className={isActive ? 'is-active' : undefined}
              >
                {item.label}
              </Link>
            );
          })}
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
          <Link className="header-action" to={isAuthenticated ? '/account' : '/login'}>
            <Icon name="user" />
            <span>{isAuthenticated ? '我的账户' : '登录'}</span>
          </Link>
          <Link className="header-action" to="/cart">
            <Icon name="bag" />
            <span>购物袋</span>
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
              placeholder="搜索商品名称或关键词"
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
