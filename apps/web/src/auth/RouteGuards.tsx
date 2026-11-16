import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { PageLoading, UnavailableState } from '../components/States';
import { useAuth } from './AuthContext';
import type { UserRole } from './types';

export function ProtectedRoute() {
  const auth = useAuth();
  const location = useLocation();
  if (auth.status === 'loading') return <PageLoading />;
  if (auth.status === 'anonymous') return <Navigate to="/login" replace state={{ from: location }} />;
  return <Outlet />;
}

export function RoleRoute({ allowed }: { allowed: UserRole[] }) {
  const { user } = useAuth();
  if (!user || !allowed.includes(user.role)) {
    return <UnavailableState title="Không có quyền truy cập" message="Tài khoản của bạn không được phép mở trang này." status="403" />;
  }
  return <Outlet />;
}
