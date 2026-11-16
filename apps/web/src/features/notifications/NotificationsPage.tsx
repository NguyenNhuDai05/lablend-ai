import { useState } from 'react';
import { Link } from 'react-router-dom';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { InlineError, Pager, useCommand, useResource } from '../../components/Workflow';
import { notificationApi } from './api';

export function NotificationsPage() {
  const [page, setPage] = useState(1); const command = useCommand();
  const state = useResource(`notifications:${page}`, async signal => ({ page: await notificationApi.list(page, 20, signal), unread: await notificationApi.unreadCount(signal) }), 15000);
  async function mark(id: string) { await command.run(async () => { await notificationApi.markRead(id); state.reload(); window.dispatchEvent(new Event('lablend:notifications-changed')); }); }
  if (state.loading) return <PageLoading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? 'Không tải được thông báo.'} retry={state.reload} />;
  const data = state.data.page;
  return <section className="notifications"><div className="section-title"><div><p className="eyebrow">HỘP THƯ</p><h1>Thông báo</h1></div><div><p>{state.data.unread.count} chưa đọc · {data.total} mục</p><button className="button-quiet" onClick={state.reload}>Tải lại</button></div></div><p className="muted">Tự cập nhật mỗi 15 giây khi mở trang.</p><InlineError message={command.error} />{!data.items.length ? <EmptyState message="Chưa có thông báo nào cho tài khoản này." /> : <div className="notification-list">{data.items.map(item => <article key={item.id} className={item.readAt ? 'read' : 'unread'}><div><h2>{item.title}</h2><p>{item.message}</p><time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString('vi-VN')}</time>{/^[0-9a-f-]{36}$/i.test(item.aggregateId) && <p><Link to={`/loans/${encodeURIComponent(item.aggregateId)}`}>Xem phiếu mượn</Link></p>}</div>{item.readAt ? <span>Đã đọc</span> : <button className="button-quiet" disabled={command.busy} onClick={() => void mark(item.id)}>Đánh dấu đã đọc</button>}</article>)}</div>}<Pager page={page} total={data.total} pageSize={data.pageSize} onChange={setPage} /></section>;
}
