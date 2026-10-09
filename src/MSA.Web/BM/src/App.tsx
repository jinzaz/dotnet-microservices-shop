import { useQuery } from '@tanstack/react-query';
import { lazy, Suspense, useEffect, useState } from 'react';
import { Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { CommandPalette, type NavEntry } from './CommandPalette';
import { countOf, ordersSummaryQuery } from './queries';
import { useSession } from './session';
import { Icon, Loading } from './ui';

const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const CatalogPage = lazy(() => import('./pages/CatalogPage'));
const InventoryPage = lazy(() => import('./pages/InventoryPage'));
const CustomersPage = lazy(() => import('./pages/CustomersPage'));
const OrdersPage = lazy(() => import('./pages/OrdersPage'));
const PaymentsPage = lazy(() => import('./pages/PaymentsPage'));
const NotificationsPage = lazy(() => import('./pages/NotificationsPage'));
const AccessPage = lazy(() => import('./pages/AccessPage'));
const MessagingPage = lazy(() => import('./pages/MessagingPage'));
const CertificatesPage = lazy(() => import('./pages/CertificatesPage'));
const SearchPage = lazy(() => import('./pages/SearchPage'));

const nav: NavEntry[] = [
  { path: '/', label: '运营概览', icon: 'overview' },
  { path: '/orders', label: '订单履约', icon: 'order', modules: ['order.fulfillment'] },
  { path: '/catalog', label: '商品目录', icon: 'catalog', modules: ['catalog.categories', 'catalog.products', 'catalog.promotions', 'catalog.coupons'] },
  { path: '/inventory', label: '库存', icon: 'inventory', modules: ['inventory.stock', 'inventory.allocations'] },
  { path: '/customers', label: '用户', icon: 'user', modules: ['identity.users', 'user.loyalty-points'] },
  { path: '/payments', label: '支付', icon: 'payment', modules: ['payment.transactions'] },
  { path: '/notifications', label: '通知', icon: 'notification', modules: ['notification.messages'] },
  { path: '/messaging', label: '消息运维', icon: 'activity', modules: ['system.messaging'] },
  { path: '/search', label: '搜索索引', icon: 'search', modules: ['system.search'] },
  { path: '/certificates', label: '证书', icon: 'key', modules: ['system.certificates'] },
  { path: '/access', label: '权限', icon: 'shield', modules: ['system.authorization'] },
];

function AppShell() {
  const { session, can, canAccess, logout } = useSession();
  const { pathname } = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const visibleNav = nav.filter((item) => !item.modules || item.modules.some(canAccess));
  const current = nav.find((item) => item.path === pathname)?.label ?? '商城中枢';
  const summary = useQuery({ ...ordersSummaryQuery(), enabled: can('order.fulfillment.query') });
  const awaitingShipment = countOf(summary.data?.fulfillmentStatuses, 'AwaitingShipment');

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        // Native modal dialogs sit in the top layer; opening the palette underneath would be unreachable.
        if (document.querySelector('dialog[open]')) return;
        setPaletteOpen((value) => !value);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  useEffect(() => {
    document.title = `${current} · Morrow 商城中枢`;
    setMenuOpen(false);
  }, [current]);

  return <div className={`app-shell${menuOpen ? ' is-menu-open' : ''}`}>
    <aside className="sidebar">
      <NavLink className="brand" to="/">
        <span className="brand-mark" aria-hidden="true"><span /><span /><span /></span>
        <span><strong>Morrow</strong><small>商城中枢</small></span>
      </NavLink>
      <nav aria-label="后台主导航">{visibleNav.map((item) => <NavLink key={item.path} to={item.path} end={item.path === '/'}>
        <Icon name={item.icon} /><span>{item.label}</span>
        {item.path === '/orders' && awaitingShipment > 0 ? <b className="nav-badge" title={`${awaitingShipment} 个订单待发货`}>{awaitingShipment > 99 ? '99+' : awaitingShipment}</b> : null}
      </NavLink>)}</nav>
      <div className="sidebar__foot"><span className="health-dot" />策略引擎已连接<small>PBAC · DENY DEFAULT</small></div>
    </aside>
    <button className="sidebar-scrim" type="button" aria-label="关闭导航" onClick={() => setMenuOpen(false)} />
    <div className="workspace">
      <header className="topbar">
        <button className="menu-button" type="button" onClick={() => setMenuOpen(true)} aria-label="打开导航"><Icon name="menu" /></button>
        <div className="topbar__context"><span>MSA / OPERATIONS</span><strong>{current}</strong></div>
        <button className="command-search" type="button" onClick={() => setPaletteOpen(true)} aria-label="打开全局搜索">
          <Icon name="search" /><span>搜索订单 / 商品 / 用户 / SKU…</span><kbd>{navigator.platform.toLowerCase().includes('mac') ? '⌘' : 'Ctrl'} K</kbd>
        </button>
        <div className="operator"><span>{(session.name ?? session.email ?? 'BM').slice(0, 1).toUpperCase()}</span><div><strong>{session.name ?? '后台操作员'}</strong><small>{session.email ?? session.subject}</small></div><button className="icon-button" type="button" onClick={() => void logout()} aria-label="退出登录" title="退出登录"><Icon name="logout" /></button></div>
      </header>
      <main className="content"><Suspense fallback={<Loading label="正在装载工作台" />}><Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/catalog" element={<CatalogPage />} />
        <Route path="/inventory" element={<InventoryPage />} />
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/payments" element={<PaymentsPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/messaging" element={<MessagingPage />} />
        <Route path="/search" element={<SearchPage />} />
        <Route path="/certificates" element={<CertificatesPage />} />
        <Route path="/access" element={<AccessPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes></Suspense></main>
    </div>
    <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} nav={visibleNav} />
  </div>;
}

export default function App() { return <AppShell />; }
