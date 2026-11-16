import { Navigate, Route, Routes } from 'react-router-dom';
import { ProtectedRoute, RoleRoute } from './auth/RouteGuards';
import { AppLayout } from './components/AppLayout';
import { StaffCatalogRoute, StaffInventoryRoute } from './features/staff/StaffRoutes';
import { NotificationsPage } from './features/notifications/NotificationsPage';
import { LoginPage, RegisterPage } from './pages/AuthPages';
import { HomePage } from './pages/HomePage';
import { NotFoundPage } from './pages/PlaceholderPages';
import { PublicCatalogPage, EquipmentDetailPage } from './features/catalog/CatalogPage';
import { CreateLoanPage, LoanDetailPage, LoansPage } from './features/loans/LoanPages';
import { ProfilePage, AdminUsersPage, AdminUserDetailPage } from './features/account/AccountPages';

export function App() {
  return <Routes><Route element={<AppLayout />}>
    <Route index element={<HomePage />} /><Route path="catalog" element={<PublicCatalogPage />} /><Route path="equipment/:id" element={<EquipmentDetailPage />} />
    <Route path="login" element={<LoginPage />} /><Route path="register" element={<RegisterPage />} />
    <Route element={<ProtectedRoute />}>
      <Route path="notifications" element={<NotificationsPage />} /><Route path="profile" element={<ProfilePage />} /><Route path="loans/:id" element={<LoanDetailPage />} />
      <Route element={<RoleRoute allowed={['STUDENT']} />}><Route path="loans" element={<LoansPage />} /><Route path="loans/new" element={<CreateLoanPage />} /></Route>
      <Route element={<RoleRoute allowed={['LAB_STAFF', 'ADMIN']} />}><Route path="staff" element={<Navigate to="/staff/loans" replace />} /><Route path="staff/loans" element={<LoansPage staff />} /><Route path="staff/loans/:id" element={<LoanDetailPage />} /><Route path="staff/catalog" element={<StaffCatalogRoute />} /><Route path="staff/inventory" element={<StaffInventoryRoute />} /></Route>
      <Route element={<RoleRoute allowed={['ADMIN']} />}><Route path="admin/users" element={<AdminUsersPage />} /><Route path="admin/users/:id" element={<AdminUserDetailPage />} /></Route>
    </Route><Route path="*" element={<NotFoundPage />} />
  </Route></Routes>;
}
