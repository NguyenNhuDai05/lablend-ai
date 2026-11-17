import { useState, type FormEvent } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { Pager, useResource } from '../../components/Workflow';
import { catalogApi } from './api';

export function PublicCatalogPage() {
  const [search, setSearch] = useSearchParams(); const [keyword, setKeyword] = useState(search.get('keyword') ?? '');
  const page = Math.max(1, Number(search.get('page')) || 1); const categoryId = search.get('categoryId') ?? '';
  const sortBy = search.get('sortBy') === 'createdAt' ? 'createdAt' : 'name'; const direction = search.get('direction') === 'desc' ? 'desc' : 'asc';
  const categories = useResource('categories', s => catalogApi.categories(s));
  const data = useResource(search.toString(), async s => {
    const result = await catalogApi.list({ keyword: search.get('keyword') ?? '', categoryId, sortBy, direction, page, pageSize: 9 }, s);
    const availability = await catalogApi.availability(result.items.map(i => i.id), s);
    return { ...result, availability };
  });
  function update(changes: Record<string, string>) { const next = new URLSearchParams(search); for (const [k, v] of Object.entries(changes)) v ? next.set(k, v) : next.delete(k); setSearch(next); }
  function submit(e: FormEvent) { e.preventDefault(); update({ keyword, page: '1' }); }
  return <section><div className="section-title"><div><p className="eyebrow">KHÁM PHÁ PHÒNG LAB</p><h1>Danh mục thiết bị</h1></div><p>Tra cứu và chọn thiết bị cho buổi thực hành.</p></div>
    <form className="filters" onSubmit={submit}><label>Tìm thiết bị<input value={keyword} onChange={e => setKeyword(e.target.value)} maxLength={100} placeholder="Tên hoặc mô tả" /></label><label>Danh mục<select value={categoryId} onChange={e => update({ categoryId: e.target.value, page: '1' })}><option value="">Tất cả</option>{categories.data?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><label>Sắp xếp<select value={`${sortBy}:${direction}`} onChange={e => { const [sortBy, direction] = e.target.value.split(':'); update({ sortBy, direction, page: '1' }); }}><option value="name:asc">Tên A–Z</option><option value="name:desc">Tên Z–A</option><option value="createdAt:desc">Mới nhất</option></select></label><button>Tìm kiếm</button></form>
    {categories.error && <p role="alert">Không tải được bộ lọc danh mục. <button className="button-quiet" onClick={categories.reload}>Thử lại</button></p>}
    {data.loading ? <PageLoading /> : data.error ? <ErrorState message={data.error} retry={data.reload} /> : !data.data?.items.length ? <EmptyState message="Không có thiết bị phù hợp. Hãy thay đổi bộ lọc." /> : <><div className="equipment-grid">{data.data.items.map(item => { const stock = data.data!.availability.find(a => a.equipmentTypeId === item.id); return <article className="surface equipment-card" key={item.id}>{item.imageUrl ? <img className="equipment-image" src={item.imageUrl} alt={item.name} loading="lazy" onError={e => { e.currentTarget.hidden = true; }} /> : <div className="equipment-thumb" aria-hidden="true">{item.categoryName?.trim().charAt(0) || '•'}</div>}<p className="eyebrow">{item.categoryName}</p><h2><Link to={`/equipment/${item.id}`}>{item.name}</Link></h2><p>{item.description || 'Xem thông số và tình trạng thiết bị.'}</p><div className="tags">{item.tags.map(t => <span key={t}>{t}</span>)}</div><p className="stock">{stock ? `${stock.available}/${stock.total} sẵn sàng` : 'Chưa có thông tin tồn kho'}</p>{stock && <StockMeter available={stock.available} total={stock.total} />}<Link to={`/equipment/${item.id}`}>Xem chi tiết →</Link></article>; })}</div><Pager page={page} pageSize={data.data.pageSize} total={data.data.total} onChange={page => update({ page: String(page) })} /></>}
  </section>;
}
export function EquipmentDetailPage() {
  const { id = '' } = useParams(); const { user } = useAuth();
  const state = useResource(id, async s => ({ item: await catalogApi.detail(id, s), stock: (await catalogApi.availability([id], s))[0] }));
  if (state.loading) return <PageLoading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? 'Không tìm thấy thiết bị.'} retry={state.reload} />;
  const { item, stock } = state.data;
  return <section><Link to="/catalog">← Danh mục thiết bị</Link><div className="detail-heading"><p className="eyebrow">{item.categoryName}</p><h1>{item.name}</h1><div className="tags">{item.tags.map(t => <span key={t}>{t}</span>)}</div></div><div className="detail-grid"><article className="surface">{item.imageUrl && <img className="equipment-image" src={item.imageUrl} alt={item.name} onError={e => { e.currentTarget.hidden = true; }} />}<h2>Mô tả</h2><p className="preserve-lines">{item.description || 'Chưa có mô tả.'}</p><h2>Thông số kỹ thuật</h2><p className="preserve-lines">{item.specifications || 'Chưa cập nhật thông số.'}</p></article><aside className="surface"><h2>Tình trạng hiện tại</h2><p className="stock">{stock ? `${stock.available}/${stock.total} sẵn sàng` : 'Chưa có dữ liệu'}</p>{stock && <StockMeter available={stock.available} total={stock.total} />}<p>Tồn kho được cập nhật tại thời điểm tra cứu. Thiết bị được giữ chỗ khi nhân viên duyệt phiếu.</p>{!item.isActive ? <p>Loại thiết bị đã ngừng cho mượn.</p> : user?.role === 'STUDENT' ? <Link className="button-link" to={`/loans/new?equipmentTypeId=${item.id}`}>Tạo phiếu mượn</Link> : !user ? <Link className="button-link" to={`/login?returnTo=${encodeURIComponent(`/loans/new?equipmentTypeId=${item.id}`)}`}>Đăng nhập để mượn</Link> : <p>Tạo phiếu mượn dành cho tài khoản Student.</p>}</aside></div></section>;
}
/** Native meter: browsers colour it from low/optimum, screen readers announce the value. */
function StockMeter({ available, total }: { available: number; total: number }) {
  return <meter className="stock-meter" min={0} max={Math.max(total, 1)} value={available} low={Math.max(total, 1) * 0.34} high={Math.max(total, 1) * 0.67} optimum={Math.max(total, 1)} aria-label={`${available}/${total} sẵn sàng`} />;
}
