import { useCallback, useEffect, useState } from 'react';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { notificationApi, type NotificationPage } from './api';

export function NotificationsPage() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<NotificationPage>();
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setData(await notificationApi.list(page)); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Không tải được thông báo.'); }
    finally { setLoading(false); }
  }, [page]);
  useEffect(() => { void load(); }, [load]);

  async function markRead(id: string) {
    try {
      const updated = await notificationApi.markRead(id);
      setData(current => current && ({ ...current, items: current.items.map(item => item.id === id ? updated : item) }));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Không cập nhật được thông báo.');
    }
  }

  if (loading) return <PageLoading />;
  if (error) return <ErrorState message={error} retry={() => void load()} />;
  if (!data?.items.length) return <EmptyState message="Chưa có thông báo nào cho tài khoản này." />;
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize));
  return <section className="notifications"><div className="section-title"><div><p className="eyebrow">HỘP THƯ</p><h1>Thông báo</h1></div><span>{data.total} mục</span></div>
    <div className="notification-list">{data.items.map(item => <article key={item.id} className={item.readAt ? 'read' : 'unread'}>
      <div><h2>{item.title}</h2><p>{item.message}</p><time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString('vi-VN')}</time></div>
      {!item.readAt && <button className="button-quiet" onClick={() => void markRead(item.id)}>Đánh dấu đã đọc</button>}
    </article>)}</div>
    {pages > 1 && <nav className="pagination" aria-label="Phân trang thông báo"><button disabled={page === 1} onClick={() => setPage(value => value - 1)}>Trước</button><span>Trang {page}/{pages}</span><button disabled={page === pages} onClick={() => setPage(value => value + 1)}>Sau</button></nav>}
  </section>;
}
