import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router-dom';
import HomePage from '../pages/HomePage';
import { GuestBasketMerger } from '../features/basket/basket-store';
import { ScrollToTop } from './ScrollToTop';
import { RequireAuth } from './auth/RequireAuth';
import { TestSiteNotice } from '../shared/layout/TestSiteNotice';

const LoginPage = lazy(() => import('../pages/LoginPage'));
const RegisterPage = lazy(() => import('../pages/RegisterPage'));
const AccountPage = lazy(() => import('../pages/AccountPage'));
const OrdersPage = lazy(() => import('../pages/OrdersPage'));
const CouponsPage = lazy(() => import('../pages/CouponsPage'));
const CouponCenterPage = lazy(() => import('../pages/CouponCenterPage'));
const ProductDetailPage = lazy(() => import('../pages/ProductDetailPage'));
const CheckoutPage = lazy(() => import('../pages/CheckoutPage'));
const PaymentPage = lazy(() => import('../pages/PaymentPage'));
const ProductsPage = lazy(() => import('../pages/ProductsPage'));
const CartPage = lazy(() => import('../pages/CartPage'));
const OrderDetailPage = lazy(() => import('../pages/OrderDetailPage'));
const AfterSalesApplyPage = lazy(() => import('../pages/AfterSalesApplyPage'));
const AfterSalesListPage = lazy(() => import('../pages/AfterSalesListPage'));
const AfterSalesDetailPage = lazy(() => import('../pages/AfterSalesDetailPage'));
const NotFoundPage = lazy(() => import('../pages/NotFoundPage'));

function RouteFallback() {
  return <div className="route-fallback" role="status"><span className="page-spinner" />正在打开页面…</div>;
}

export default function App() {
  return (
    <>
      <TestSiteNotice />
      <Suspense fallback={<RouteFallback />}>
        <ScrollToTop />
        <GuestBasketMerger />
        <Routes>
          <Route path="/" element={<HomePage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/account"
            element={<RequireAuth><AccountPage /></RequireAuth>}
          />
          <Route path="/products" element={<ProductsPage />} />
          <Route path="/products/:productId" element={<ProductDetailPage />} />
          <Route path="/cart" element={<CartPage />} />
          <Route path="/checkout" element={<RequireAuth><CheckoutPage /></RequireAuth>} />
          <Route path="/payment/:orderId" element={<RequireAuth><PaymentPage /></RequireAuth>} />
          <Route path="/coupons" element={<RequireAuth><CouponsPage /></RequireAuth>} />
          <Route path="/coupon-center" element={<CouponCenterPage />} />
          <Route path="/coupon-center/:campaignId" element={<CouponCenterPage />} />
          <Route path="/orders" element={<RequireAuth><OrdersPage /></RequireAuth>} />
          <Route path="/orders/:orderId" element={<RequireAuth><OrderDetailPage /></RequireAuth>} />
          <Route path="/orders/:orderId/after-sales/new" element={<RequireAuth><AfterSalesApplyPage /></RequireAuth>} />
          <Route path="/after-sales" element={<RequireAuth><AfterSalesListPage /></RequireAuth>} />
          <Route path="/after-sales/:afterSalesId" element={<RequireAuth><AfterSalesDetailPage /></RequireAuth>} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </Suspense>
    </>
  );
}
