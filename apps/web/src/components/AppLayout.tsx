import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import type { CurrentUser, UserRole } from '../auth/types';
import { useEffect, useRef, useState } from 'react';
import { useResource } from './Workflow';
import { Icon } from './Icon';
import { notificationApi } from '../features/notifications/api';

const roleLabels: Record<UserRole, string> = { STUDENT: 'Sinh viên', LAB_STAFF: 'Nhân viên phòng lab', ADMIN: 'Quản trị viên' };

export function AppLayout() {
  const { user, logout } = useAuth();
  const staff = user?.role === 'LAB_STAFF' || user?.role === 'ADMIN';
  const unread = useResource(`unread:${user?.id ?? ''}`, s => user ? notificationApi.unreadCount(s) : Promise.resolve({ count: 0 }), user ? 30000 : 0);
  useEffect(() => { window.addEventListener('lablend:notifications-changed', unread.reload); return () => window.removeEventListener('lablend:notifications-changed', unread.reload); }, [unread.reload]);
  // Staff/Admin get a left sidebar (management first); guests and students keep the top bar. The logo is the home link.
  return <div className={staff ? 'app-shell with-sidebar' : 'app-shell'}>
    <header className="app-header">
      <NavLink className="brand" to="/" title="Trang chủ">LabLend <span>AI</span></NavLink>
      <nav aria-label="Điều hướng chính">
        {staff && <>
          <p className="nav-label">Quản lý phòng lab</p>
          <NavLink to="/staff/loans"><Icon name="loans" />Phiếu mượn</NavLink>
          <NavLink to="/staff/catalog"><Icon name="catalog" />Danh mục</NavLink>
          <NavLink to="/staff/inventory"><Icon name="inventory" />Kho thiết bị</NavLink>
        </>}
        <NavLink to="/catalog"><Icon name="equipment" />Thiết bị</NavLink>
        {user?.role === 'ADMIN' && <NavLink to="/admin/users"><Icon name="users" />Tài khoản</NavLink>}
        {user?.role === 'STUDENT' && <NavLink to="/loans"><Icon name="loans" />Phiếu mượn</NavLink>}
        {user?.role === 'STUDENT' && <NavLink to="/recommendations"><Icon name="sparkles" />AI tư vấn thiết bị</NavLink>}
      </nav>
      <div className="account-actions">
        {user ? <><UserMenu user={user} unread={unread.data?.count ?? 0} /><button className="logout" onClick={() => void logout()}><Icon name="logout" />Đăng xuất</button></>
          : <><ThemeToggle /><NavLink to="/login">Đăng nhập</NavLink><NavLink className="button-link" to="/register">Đăng ký</NavLink></>}
      </div>
    </header>
    <main className="page"><Outlet /></main>
  </div>;
}

/** Avatar + name + role; opens Thông báo, Hồ sơ and the theme switch. Native <details>, closed on navigation, outside click and Esc. */
function UserMenu({ user, unread }: { user: CurrentUser; unread: number }) {
  const menu = useRef<HTMLDetailsElement>(null);
  const { pathname } = useLocation();
  useEffect(() => { if (menu.current) menu.current.open = false; }, [pathname]);
  useEffect(() => {
    const close = (e: PointerEvent) => { if (menu.current?.open && !menu.current.contains(e.target as Node)) menu.current.open = false; };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, []);
  return <details ref={menu} className="user-menu" onKeyDown={e => { if (e.key === 'Escape' && menu.current?.open) { menu.current.open = false; menu.current.querySelector('summary')?.focus(); } }}>
    <summary title={user.email}>
      {unread > 0 && <span className="bubble" aria-hidden="true" />}
      <span className="avatar"><Icon name="user" /></span>
      <span className="who"><strong>{user.displayName ?? user.email}</strong><small>{roleLabels[user.role]}</small></span>
      {unread > 0 && <span className="sr-only">, {unread} thông báo chưa đọc</span>}
    </summary>
    <div className="user-menu-panel">
      <NavLink to="/notifications"><span className="bell"><Icon name="bell" />{unread > 0 && <span className="bubble" aria-hidden="true" />}</span>Thông báo{unread > 0 && <span className="sr-only"> ({unread} chưa đọc)</span>}</NavLink>
      <NavLink to="/profile"><Icon name="user" />Hồ sơ</NavLink>
      <ThemeToggle withText />
    </div>
  </details>;
}

/** Overrides the system colour scheme; the choice is a per-browser convenience (main.tsx re-applies it on load). */
function ThemeToggle({ withText = false }: { withText?: boolean }) {
  const root = document.documentElement;
  const [dark, setDark] = useState(() => root.dataset.theme ? root.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches);
  function toggle() {
    const next = dark ? 'light' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem('lablend-theme', next); } catch { /* storage blocked: the choice lasts for this page only */ }
    setDark(!dark);
  }
  const label = dark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối';
  return <button type="button" className={withText ? 'theme-toggle with-text' : 'theme-toggle'} onClick={toggle} aria-label={withText ? undefined : label} title={label}>
    <Icon name={dark ? 'sun' : 'moon'} />{withText && (dark ? 'Giao diện sáng' : 'Giao diện tối')}
  </button>;
}
