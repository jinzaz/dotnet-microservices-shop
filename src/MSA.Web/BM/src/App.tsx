import { lazy, Suspense, useState } from 'react';
import { Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { useSession } from './session';
import { Icon, Loading, type IconName } from './ui';

const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const CatalogPage = lazy(() => import('./pages/CatalogPage'));
const InventoryPage = lazy(() => import('./pages/InventoryPage'));
const CustomersPage = lazy(() => import('./pages/CustomersPage'));
const OrdersPage = lazy(() => import('./pages/OrdersPage'));
const PaymentsPage = lazy(() => import('./pages/PaymentsPage'));
const NotificationsPage = lazy(() => import('./pages/NotificationsPage'));
const AccessPage = lazy(() => import('./pages/AccessPage'));

const nav: Array<{ path: string; label: string; icon: IconName; modules?: string[] }> = [
  { path: '/', label: '运营概览', icon: 'overview' },
  { path: '/orders', label: '订单履约', icon: 'order', modules: ['order.fulfillment'] },
  { path: '/catalog', label: '商品目录', icon: 'catalog', modules: ['catalog.categories', 'catalog.products', 'catalog.promotions', 'catalog.coupons'] },
  { path: '/inventory', label: '库存', icon: 'inventory', modules: ['inventory.stock', 'inventory.flash-sales'] },
  { path: '/customers', label: '用户', icon: 'user', modules: ['identity.users', 'user.loyalty-points'] },
  { path: '/payments', label: '支付', icon: 'payment', modules: ['payment.transactions'] },
  { path: '/notifications', label: '通知', icon: 'notification', modules: ['notification.messages'] },
  { path: '/access', label: '权限', icon: 'shield', modules: ['system.authorization'] },
];

function AppShell() {
  const { session, canAccess, logout } = useSession();
  const { pathname } = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const visibleNav = nav.filter((item) => !item.modules || item.modules.some(canAccess));
  const current = nav.find((item) => item.path === pathname)?.label ?? '商城中枢';

  return <div className={`app-shell${menuOpen ? ' is-menu-open' : ''}`}>
    <aside className="sidebar">
      <NavLink className="brand" to="/" onClick={() => setMenuOpen(false)}>
        <span className="brand-mark" aria-hidden="true"><span /><span /><span /></span>
        <span><strong>Morrow</strong><small>商城中枢</small></span>
      </NavLink>
      <nav aria-label="后台主导航">{visibleNav.map((item) => <NavLink key={item.path} to={item.path} end={item.path === '/'} onClick={() => setMenuOpen(false)}><Icon name={item.icon} /><span>{item.label}</span></NavLink>)}</nav>
      <div className="sidebar__foot"><span className="health-dot" />策略引擎已连接<small>PBAC · DENY DEFAULT</small></div>
    </aside>
    <button className="sidebar-scrim" type="button" aria-label="关闭导航" onClick={() => setMenuOpen(false)} />
    <div className="workspace">
      <header className="topbar">
        <button className="menu-button" type="button" onClick={() => setMenuOpen(true)} aria-label="打开导航"><Icon name="menu" /></button>
        <div className="topbar__context"><span>MSA / OPERATIONS</span><strong>{current}</strong></div>
        <label className="command-search"><Icon name="search" /><input type="search" placeholder="搜索订单 / 商品 / 用户 / SKU…" aria-label="全局搜索（即将开放）" disabled /><kbd>⌘ K</kbd></label>
        <div className="operator"><span>{(session.name ?? session.email ?? 'BM').slice(0, 1).toUpperCase()}</span><div><strong>{session.name ?? '后台操作员'}</strong><small>{session.email ?? session.subject}</small></div><button className="icon-button" type="button" onClick={() => void logout()} aria-label="退出登录"><Icon name="logout" /></button></div>
      </header>
      <main className="content"><Suspense fallback={<Loading label="正在装载工作台" />}><Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/catalog" element={<CatalogPage />} />
        <Route path="/inventory" element={<InventoryPage />} />
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/payments" element={<PaymentsPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/access" element={<AccessPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes></Suspense></main>
    </div>
  </div>;
}

export default function App() { return <AppShell />; }
