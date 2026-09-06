import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router-dom';
import HomePage from '../pages/HomePage';
import { RequireAuth } from './auth/RequireAuth';

const LoginPage = lazy(() => import('../pages/LoginPage'));
const AccountPage = lazy(() => import('../pages/AccountPage'));
const FutureFeaturePage = lazy(() => import('../pages/FutureFeaturePage'));
const NotFoundPage = lazy(() => import('../pages/NotFoundPage'));

function RouteFallback() {
  return <div className="route-fallback" role="status"><span className="page-spinner" />正在打开页面…</div>;
}

export default function App() {
  return (
    <Suspense fallback={<RouteFallback />}>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/account"
          element={<RequireAuth><AccountPage /></RequireAuth>}
        />
        <Route path="/products" element={<FutureFeaturePage />} />
        <Route path="/cart" element={<FutureFeaturePage />} />
        <Route path="/orders" element={<RequireAuth><FutureFeaturePage /></RequireAuth>} />
        <Route path="/register" element={<FutureFeaturePage />} />
        <Route path="/forgot-password" element={<FutureFeaturePage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
  );
}
