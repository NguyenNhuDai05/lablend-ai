import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useEffect } from 'react';
import { useResource } from './Workflow';
import { notificationApi } from '../features/notifications/api';

export function AppLayout() {
  const { user, logout } = useAuth();
  const staff = user?.role === 'LAB_STAFF' || user?.role === 'ADMIN';
  const unread = useResource(`unread:${user?.id ?? ''}`, s => user ? notificationApi.unreadCount(s) : Promise.resolve({ count: 0 }), user ? 30000 : 0);
  useEffect(() => { window.addEventListener('lablend:notifications-changed', unread.reload); return () => window.removeEventListener('lablend:notifications-changed', unread.reload); }, [unread.reload]);
  return <div className="app-shell">
    <header className="app-header">
      <NavLink className="brand" to="/">LabLend <span>AI</span></NavLink>
      <nav aria-label="Điều hướng chính">
        <NavLink to="/">Trang chủ</NavLink>
        <NavLink to="/catalog">Thiết bị</NavLink>
        {user?.role === 'STUDENT' && <NavLink to="/loans">Phiếu mượn</NavLink>}
        {staff && <NavLink to="/staff/catalog">Danh mục</NavLink>}
        {staff && <NavLink to="/staff/loans">Phiếu mượn</NavLink>}
        {staff && <NavLink to="/staff/inventory">Kho thiết bị</NavLink>}
        {user?.role === 'ADMIN' && <NavLink to="/admin/users">Tài khoản</NavLink>}
        {user && <NavLink to="/notifications">Thông báo{unread.data?.count ? ` (${unread.data.count})` : ''}</NavLink>}
        {user && <NavLink to="/profile">Hồ sơ</NavLink>}
      </nav>
      <div className="account-actions">
        {user ? <><span>{user.displayName ?? user.email}</span><button className="button-quiet" onClick={() => void logout()}>Đăng xuất</button></>
          : <><NavLink to="/login">Đăng nhập</NavLink><NavLink className="button-link" to="/register">Đăng ký</NavLink></>}
      </div>
    </header>
    <main className="page"><Outlet /></main>
  </div>;
}
