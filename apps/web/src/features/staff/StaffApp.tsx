import { useEffect, useState, type FormEvent } from 'react';
import { readToken, session, setUnauthorizedHandler } from './api';
import { CatalogPage } from './CatalogPage';
import { InventoryPage } from './InventoryPage';
import './staff.css';

const staffRoles = ['LAB_STAFF', 'ADMIN'];
type Tab = 'catalog' | 'inventory';

/** Staff feature (M2): catalog + inventory management. Mounted at /staff. */
export function StaffApp() {
  const [token, setToken] = useState(session.get);
  const [expired, setExpired] = useState(false);
  const [tab, setTab] = useState<Tab>(location.hash === '#inventory' ? 'inventory' : 'catalog');
  const [assetType, setAssetType] = useState<string>();
  useEffect(() => setUnauthorizedHandler(() => { session.set(null); setToken(null); setExpired(true); }), []);
  useEffect(() => { history.replaceState(null, '', `#${tab}`); }, [tab]);

  const info = token ? readToken(token) : null;
  if (!token || !info || info.expiresAt <= new Date() || !staffRoles.includes(info.role))
    return <TokenGate expired={expired} onToken={t => { session.set(t); setToken(t); setExpired(false); }} />;

  function signOut() { session.set(null); setToken(null); }
  return <main className="staff">
    <header className="staff-header">
      <div>
        <p className="eyebrow">LABLEND AI · STAFF</p>
        <h1>Quản lý thiết bị</h1>
      </div>
      <div className="who">
        <span>{info.role} · hết hạn {info.expiresAt.toLocaleTimeString('vi-VN', { timeStyle: 'short' })}</span>
        <button type="button" className="ghost" onClick={signOut}>Đăng xuất</button>
      </div>
    </header>
    <nav className="tabs" aria-label="Khu vực quản lý">
      {([['catalog', 'Danh mục & loại thiết bị'], ['inventory', 'Kho thiết bị']] as const).map(([id, label]) =>
        <button key={id} type="button" aria-current={tab === id ? 'page' : undefined} onClick={() => setTab(id)}>{label}</button>)}
    </nav>
    {tab === 'catalog'
      ? <CatalogPage onShowAssets={id => { setAssetType(id); setTab('inventory'); }} />
      : <InventoryPage key={assetType} initialType={assetType} />}
  </main>;
}

function TokenGate({ expired, onToken }: { expired: boolean; onToken: (token: string) => void }) {
  const [error, setError] = useState<string>();
  function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const token = String(new FormData(e.currentTarget).get('token') ?? '').trim().replace(/^Bearer\s+/i, '');
    const info = readToken(token);
    if (!info) return setError('Token không đúng định dạng JWT.');
    if (info.expiresAt <= new Date()) return setError('Token đã hết hạn, hãy tạo token mới.');
    if (!staffRoles.includes(info.role)) return setError(`Role ${info.role || '(trống)'} không được vào trang Staff.`);
    onToken(token);
  }
  return <main className="staff narrow">
    <p className="eyebrow">LABLEND AI · STAFF</p>
    <h1>Đăng nhập Staff</h1>
    <section>
      {expired && <p role="alert" className="error">Phiên đã hết hạn hoặc token không còn hợp lệ.</p>}
      {/* ponytail: dev-token login until Identity issues real tokens (M1) and M3's auth client replaces this gate. */}
      <p>Identity chưa có đăng nhập. Tạo token môi trường dev rồi dán vào đây:</p>
      <pre>./scripts/New-DevToken.ps1 -Role LAB_STAFF</pre>
      <form className="form" onSubmit={submit}>
        <label className="field"><span>JWT</span>
          <textarea name="token" rows={4} required spellCheck={false} autoComplete="off" />
        </label>
        {error && <p role="alert" className="error">{error}</p>}
        <div className="actions"><button>Vào trang Staff</button></div>
      </form>
    </section>
  </main>;
}
