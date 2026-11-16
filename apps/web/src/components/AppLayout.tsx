import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export function AppLayout() {
  const { user, logout } = useAuth();
  const staff = user?.role === 'LAB_STAFF' || user?.role === 'ADMIN';
  return <div className="app-shell">
    <header className="app-header">
      <NavLink className="brand" to="/">LabLend <span>AI</span></NavLink>
      <nav aria-label="Điều hướng chính">
        <NavLink to="/">Trang chủ</NavLink>
        {user?.role === 'STUDENT' && <NavLink to="/loans">Phiếu mượn</NavLink>}
        {staff && <NavLink to="/staff/catalog">Danh mục</NavLink>}
        {staff && <NavLink to="/staff/inventory">Kho thiết bị</NavLink>}
        {user && <NavLink to="/notifications">Thông báo</NavLink>}
      </nav>
      <div className="account-actions">
        {user ? <><span>{user.displayName ?? user.email}</span><button className="button-quiet" onClick={() => void logout()}>Đăng xuất</button></>
          : <><NavLink to="/login">Đăng nhập</NavLink><NavLink className="button-link" to="/register">Đăng ký</NavLink></>}
      </div>
    </header>
    <main className="page"><Outlet /></main>
  </div>;
}
