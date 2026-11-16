import { useState, type FormEvent, type ReactNode } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../lib/api';
const messageOf = (error: unknown) => error instanceof ApiError ? error.message : 'Không thể hoàn tất yêu cầu.';

export function LoginPage() {
  const auth = useAuth(); const navigate = useNavigate(); const location = useLocation();
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const destination = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? '/';
  if (auth.status === 'authenticated') return <Navigate to={destination} replace />;
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget);
    try { await auth.login({ email: String(data.get('email')), password: String(data.get('password')) }); navigate(destination, { replace: true }); }
    catch (e) { setError(messageOf(e)); } finally { setBusy(false); }
  }
  return <AuthCard title="Đăng nhập" footer={<>Chưa có tài khoản? <Link to="/register">Đăng ký</Link></>}><form onSubmit={event => void submit(event)}><label>Email<input name="email" type="email" autoComplete="email" required /></label><label>Mật khẩu<input name="password" type="password" autoComplete="current-password" required /></label>{error && <p className="form-error" role="alert">{error}</p>}<button disabled={busy}>{busy ? 'Đang đăng nhập…' : 'Đăng nhập'}</button></form></AuthCard>;
}

export function RegisterPage() {
  const auth = useAuth(); const navigate = useNavigate(); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget);
    try { await auth.register({ displayName: String(data.get('displayName')), email: String(data.get('email')), password: String(data.get('password')), confirmPassword: String(data.get('confirmPassword')) }); navigate('/login', { replace: true }); }
    catch (e) { setError(messageOf(e)); } finally { setBusy(false); }
  }
  return <AuthCard title="Tạo tài khoản" footer={<>Đã có tài khoản? <Link to="/login">Đăng nhập</Link></>}><form onSubmit={event => void submit(event)}><label>Họ và tên<input name="displayName" autoComplete="name" minLength={2} maxLength={100} required /></label><label>Email<input name="email" type="email" autoComplete="email" required /></label><label>Mật khẩu<input name="password" type="password" minLength={12} maxLength={128} autoComplete="new-password" required /></label><p>Từ 12 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.</p><label>Xác nhận mật khẩu<input name="confirmPassword" type="password" minLength={12} maxLength={128} autoComplete="new-password" required /></label>{error && <p className="form-error" role="alert">{error}</p>}<button disabled={busy}>{busy ? 'Đang tạo…' : 'Tạo tài khoản'}</button></form></AuthCard>;
}
function AuthCard({ title, children, footer }: { title: string; children: ReactNode; footer: ReactNode }) { return <section className="auth-card"><p className="eyebrow">LABLEND AI</p><h1>{title}</h1>{children}<p className="auth-footer">{footer}</p></section>; }
