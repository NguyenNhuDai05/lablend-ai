import { Link } from 'react-router-dom';
export function PageLoading() { return <div className="state-card" role="status"><span className="spinner" />Đang tải…</div>; }
export function EmptyState({ title = 'Chưa có dữ liệu', message }: { title?: string; message: string }) { return <div className="state-card"><h2>{title}</h2><p>{message}</p></div>; }
export function ErrorState({ message, retry }: { message: string; retry?: () => void }) { return <div className="state-card state-error" role="alert"><h2>Không thể hoàn tất</h2><p>{message}</p>{retry && <button onClick={retry}>Thử lại</button>}</div>; }
export function UnavailableState({ title, message, status }: { title: string; message: string; status?: string }) { return <div className="state-card"><p className="status-code">{status}</p><h1>{title}</h1><p>{message}</p><Link className="button-link" to="/">Về trang chủ</Link></div>; }
