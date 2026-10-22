import { useLanguage } from '../shared/i18n/LanguageSelector';
import { SeoMetadata } from './seo/SeoMetadata';
import LandingPage from '../features/marketing/LandingPage';
import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { Spin } from 'antd';
import { useAuth } from '../features/auth/session';
import { LoadError } from '../shared/ui/Ui';
import ErrorBoundary from '../shared/ui/ErrorBoundary';
import AppShell from './layout/AppShell';

const PasswordPage = lazy(() => import('../features/auth/PasswordPage'));
const ChangePasswordModal = lazy(() => import('../features/auth/ChangePasswordModal'));
const ForgotPasswordPage = lazy(() => import('../features/auth/ForgotPasswordPage'));
const LoginPage = lazy(() => import('../features/auth/LoginPage'));
const RegisterPage = lazy(() => import('../features/auth/RegisterPage'));
const AccountStatusPage = lazy(() => import('../features/subscription/AccountStatusPage'));
const DashboardPage = lazy(() => import('../features/dashboard/DashboardPage'));
const BuildingsPage = lazy(() => import('../features/buildings/BuildingsPage'));
const RoomsPage = lazy(() => import('../features/rooms/RoomsPage'));
const RoomTenantPage = lazy(() => import('../features/rooms/RoomTenantPage'));
const RoomDocumentsPage = lazy(() => import('../features/rooms/RoomDocumentsPage'));
const ReadingsPage = lazy(() => import('../features/readings/ReadingsPage'));
const InvoicesPage = lazy(() => import('../features/invoices/InvoicesPage'));
const ReportsPage = lazy(() => import('../features/reports/ReportsPage'));
const ZaloSettingsPage = lazy(() => import('../features/zalo/ZaloSettingsPage'));
const PaymentSettingsPage = lazy(() => import('../features/payments/PaymentSettingsPage'));
const AdminOwnersPage = lazy(() => import('../features/admin/AdminOwnersPage'));
const AdminMonitoringPage = lazy(() => import('../features/admin/monitoring/AdminMonitoringPage'));
const AdminPromotionsPage = lazy(() => import('../features/admin/AdminPromotionsPage'));
const TenantPage = lazy(() => import('../features/tenant/TenantPage'));
const TenantCheckoutPage = lazy(() => import('../features/tenant/TenantCheckoutPage'));
const NotFoundPage = lazy(() => import('./NotFoundPage'));


function Gate({ children, admin = false }: { children: React.ReactNode; admin?: boolean }) {
  const { user, ready, error, refresh } = useAuth();
  if (error) return <LoadError error={error} onRetry={() => void refresh()} />;
  if (!ready) return <div className="screen-center"><Spin size="large" /></div>;
  if (!user) return <Navigate to="/login" replace />;
  if (user.status && user.status !== 'Approved') return <Navigate to="/account-status" replace />;
  if (admin && user.role !== 'Admin') return <Navigate to="/dashboard" replace />;
  if (!admin && user.role === 'Admin') return <Navigate to="/admin/owners" replace />;
  return children;
}

function HomeRedirect() {
  const { user, ready, error, refresh } = useAuth();
  if (error) return <LoadError error={error} onRetry={() => void refresh()} />;
  if (!ready || !user) return <LandingPage />;
  if (user.status && user.status !== 'Approved') return <Navigate to="/account-status" replace />;
  return <Navigate to={user.role === 'Admin' ? '/admin/owners' : '/dashboard'} replace />;
}

function AppFallback() {
  return <div className="screen-center"><Spin size="large" /></div>;
}

function ChangePasswordRoute() {
  const { user, ready } = useAuth();
  if (!ready) return <div className="screen-center"><Spin size="large" /></div>;
  if (!user) return <Navigate to="/login" replace />;
  if (user.role === 'Owner' && user.status !== 'Approved') {
    return <div style={{ maxWidth: 600, margin: '40px auto', padding: '0 16px' }}><PasswordPage /></div>;
  }
  return <PasswordPage layout={AppShell} />;
}

export default function App() {
  useLanguage();
  const location = useLocation();
  return <><SeoMetadata /><ErrorBoundary><Suspense fallback={<AppFallback />}><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/reset-password" element={<ForgotPasswordPage />} />
    <Route path="/change-password" element={<ChangePasswordRoute />} />
    <Route path="/register" element={<RegisterPage />} />
    <Route path="/account-status" element={<AccountStatusPage layout={AppShell} passwordModal={ChangePasswordModal} />} />
    <Route path="/welcome" element={<Navigate to={`/${location.search}${location.hash}`} replace />} />
    <Route path="/t/:token" element={<TenantPage />} />
    <Route path="/t/:token/invoices/:invoiceId/checkout" element={<TenantCheckoutPage />} />
    <Route path="/tenant/:token" element={<TenantPage />} />
    <Route path="/tenant/:token/invoices/:invoiceId/checkout" element={<TenantCheckoutPage />} />
    <Route path="/" element={<HomeRedirect />} />
    <Route path="/features" element={<LandingPage page="features" />} />
    <Route path="/pricing" element={<LandingPage page="pricing" />} />
    <Route path="/huong-dan" element={<LandingPage page="guides" />} />
    <Route path="/dashboard" element={<Gate><AppShell><DashboardPage /></AppShell></Gate>} />
    <Route path="/buildings" element={<Gate><AppShell><BuildingsPage /></AppShell></Gate>} />
    <Route path="/rooms" element={<Gate><AppShell><RoomsPage /></AppShell></Gate>} />
    <Route path="/rooms/:id/tenant" element={<Gate><AppShell><RoomTenantPage /></AppShell></Gate>} />
    <Route path="/rooms/:id/documents" element={<Gate><AppShell><RoomDocumentsPage /></AppShell></Gate>} />
    <Route path="/readings" element={<Gate><AppShell><ReadingsPage /></AppShell></Gate>} />
    <Route path="/invoices" element={<Gate><AppShell><InvoicesPage /></AppShell></Gate>} />
    <Route path="/reports" element={<Gate><AppShell><ReportsPage /></AppShell></Gate>} />
    <Route path="/settings/zalo" element={<Gate><AppShell><ZaloSettingsPage /></AppShell></Gate>} />
    <Route path="/settings/payment" element={<Gate><AppShell><PaymentSettingsPage /></AppShell></Gate>} />
    <Route path="/admin/owners" element={<Gate admin><AppShell><AdminOwnersPage /></AppShell></Gate>} />
    <Route path="/admin/monitoring" element={<Gate admin><AppShell><AdminMonitoringPage /></AppShell></Gate>} />
    <Route path="/admin/promotions" element={<Gate admin><AppShell><AdminPromotionsPage /></AppShell></Gate>} />
    <Route path="*" element={<NotFoundPage />} />
  </Routes></Suspense></ErrorBoundary></>;
}
