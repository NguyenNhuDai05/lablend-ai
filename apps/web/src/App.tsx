import { Route, Routes } from 'react-router-dom';
import { ProtectedRoute, RoleRoute } from './auth/RouteGuards';
import { AppLayout } from './components/AppLayout';
import { StaffCatalogRoute, StaffInventoryRoute } from './features/staff/StaffRoutes';
import { NotificationsPage } from './features/notifications/NotificationsPage';
import { LoginPage, RegisterPage } from './pages/AuthPages';
import { HomePage } from './pages/HomePage';
import { LoansPage, NotFoundPage } from './pages/PlaceholderPages';

export function App() {
  return <Routes><Route element={<AppLayout />}><Route index element={<HomePage />} /><Route path="login" element={<LoginPage />} /><Route path="register" element={<RegisterPage />} /><Route element={<ProtectedRoute />}><Route path="notifications" element={<NotificationsPage />} /><Route element={<RoleRoute allowed={['STUDENT']} />}><Route path="loans" element={<LoansPage />} /></Route><Route element={<RoleRoute allowed={['LAB_STAFF', 'ADMIN']} />}><Route path="staff/catalog" element={<StaffCatalogRoute />} /><Route path="staff/inventory" element={<StaffInventoryRoute />} /></Route></Route><Route path="*" element={<NotFoundPage />} /></Route></Routes>;
}
