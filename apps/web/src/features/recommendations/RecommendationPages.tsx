import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Icon } from '../../components/Icon';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { InlineError, Pager, useResource } from '../../components/Workflow';
import { formatDate } from '../../lib/workflows';
import { catalogApi } from '../catalog/api';
import { recommendationApi, recommendationError, type Recommendation } from './recommendationApi';
import './recommendations.css';

const examples = [
  'Tôi muốn làm hệ thống tưới cây tự động có thể đo độ ẩm đất, điều khiển máy bơm và hiển thị dữ liệu.',
  'Tôi cần quan sát cấu trúc tế bào thực vật trong buổi thực hành sinh học.',
  'Tôi muốn đo và so sánh tín hiệu điện trong một mạch điện tử cơ bản.',
];
export function RecommendationPage() {
  const [purpose, setPurpose] = useState(''); const [constraints, setConstraints] = useState(''); const [categoryId, setCategoryId] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const navigate = useNavigate();
  const flight = useRef<AbortController | null>(null);
  useEffect(() => () => flight.current?.abort(), []);
  const categories = useResource('ai-categories', s => catalogApi.categories(s));
  async function submit(e: FormEvent) {
    e.preventDefault(); if (flight.current) return;
    const controller = new AbortController(); flight.current = controller; setBusy(true); setError('');
    try {
      const result = await recommendationApi.create(purpose, constraints.split('\n').map(c => c.trim()).filter(Boolean), categoryId, controller.signal);
      if (!controller.signal.aborted) navigate(`/recommendations/${result.id}`);
    } catch (e) { if (!controller.signal.aborted) setError(recommendationError(e)); }
    finally { if (!controller.signal.aborted) { setBusy(false); flight.current = null; } }
  }
  return <section className="recommendation-page"><div className="section-title"><div><p className="eyebrow"><Icon name="sparkles" /> LABLEND × GEMINI</p><h1>AI tư vấn thiết bị phòng thí nghiệm</h1><p>Mô tả dự án hoặc thí nghiệm bạn muốn thực hiện. LabLend AI sẽ giúp bạn tìm những thiết bị phù hợp đang có trong phòng lab.</p></div><Link to="/recommendations/history">Lịch sử tư vấn →</Link></div>
    <div className="ai-workspace"><form className="surface form-stack" onSubmit={e => void submit(e)} aria-busy={busy}>
      <label htmlFor="ai-purpose">Bạn muốn thực hiện dự án gì?</label><textarea id="ai-purpose" rows={7} required minLength={10} maxLength={2000} value={purpose} disabled={busy} onChange={e => setPurpose(e.target.value)} placeholder="Mô tả mục tiêu, thí nghiệm và điều bạn muốn đo hoặc tạo ra…" />
      <small className="muted">{purpose.length.toLocaleString('vi-VN')}/2.000 ký tự · Tối thiểu 10 ký tự</small>
      <label>Yêu cầu bổ sung (không bắt buộc)<textarea rows={3} maxLength={2407} value={constraints} disabled={busy} onChange={e => setConstraints(e.target.value)} placeholder="Mỗi yêu cầu một dòng, ví dụ: Ưu tiên thiết bị cho người mới bắt đầu" /></label>
      <label>Phạm vi danh mục<select value={categoryId} disabled={busy || categories.loading} onChange={e => setCategoryId(e.target.value)}><option value="">Tất cả danh mục</option>{categories.data?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      {categories.error && <ErrorState message={categories.error} retry={categories.reload} />}
      <p className="muted">Khi danh mục quá lớn, chọn một phạm vi cụ thể để tiếp tục. Nội dung mô tả và yêu cầu được gửi đến Google Gemini; không nhập thông tin cá nhân hoặc bí mật.</p>
      <InlineError message={error} />{error && <Link to="/catalog">Xem danh mục →</Link>}
      <button disabled={busy || purpose.trim().length < 10}><Icon name="sparkles" />{busy ? 'Gemini đang phân tích…' : error ? 'Thử lại tư vấn AI' : 'Nhận tư vấn AI'}</button>
      {busy && <p role="status">Đang đọc danh mục và kiểm tra thiết bị phù hợp. Yêu cầu sẽ tự dừng nếu hết thời gian chờ.</p>}
    </form><aside className="ai-guide"><article className="surface"><h2>Bắt đầu từ ý tưởng của bạn</h2><p>Nhấn một ví dụ để điền mô tả, sau đó chỉnh sửa theo nhu cầu.</p><div className="ai-examples">{examples.map(example => <button type="button" className="button-quiet" key={example} disabled={busy} onClick={() => setPurpose(example)}>{example}<span aria-hidden="true">↗</span></button>)}</div></article><article className="surface"><h2>Từ ý tưởng đến phiếu mượn</h2><ol><li>Mô tả mục tiêu và nhận tư vấn từ Gemini.</li><li>Đọc lý do, kiểm tra tồn kho và chọn thiết bị.</li><li>Chọn hạn trả, điều chỉnh số lượng và xác nhận phiếu.</li></ol><p className="muted">AI chỉ tư vấn. Thiết bị chỉ được giữ khi nhân viên duyệt phiếu; tồn kho có thể thay đổi.</p></article></aside></div>
  </section>;
}

export function RecommendationDetailPage() {
  const { id = '' } = useParams();
  const result = useResource(`recommendation:${id}`, async signal => {
    try { return await recommendationApi.detail(id, signal); } catch (e) { throw new Error(recommendationError(e)); }
  });
  return <section className="recommendation-page"><Link to="/recommendations/history">← Lịch sử tư vấn</Link><div className="detail-heading"><p className="eyebrow">GỢI Ý TỪ GEMINI</p><h1>Thiết bị cho ý tưởng của bạn</h1></div>{result.loading ? <PageLoading /> : result.error ? <><ErrorState message={result.error} retry={result.reload} /><Link to="/catalog">Xem danh mục →</Link></> : result.data ? <RecommendationResults key={result.data.id} result={result.data} refresh={result.reload} /> : null}</section>;
}

export function RecommendationResults({ result, refresh }: { result: Recommendation; refresh: () => void }) {
  const [selected, setSelected] = useState<string[]>([]); const navigate = useNavigate();
  const selectable = result.recommendations.filter(i => i.isActive && i.available > 0);
  const chosen = selected.filter(id => selectable.some(i => i.equipmentTypeId === id));
  return <><article className="surface ai-summary"><p className="preserve-lines">{result.purpose}</p>{result.constraints.length > 0 && <ul>{result.constraints.map((c, i) => <li key={i}>{c}</li>)}</ul>}<small className="muted">{formatDate(result.createdAt)} · {result.modelUsed}</small></article>
    <div className="row-between section-space"><p>Tồn kho kiểm tra lúc {formatDate(result.availabilityCheckedAt)}. Đây là thông tin tham khảo, không giữ chỗ.</p><button className="button-quiet" onClick={refresh}>Cập nhật tồn kho</button></div>
    {!result.recommendations.length ? <EmptyState message="Không tìm thấy thiết bị phù hợp trong danh mục đã chọn. Hãy bổ sung mô tả hoặc đổi phạm vi danh mục." /> : <div className="ai-results">{result.recommendations.map(item => <article className={`surface ai-result${chosen.includes(item.equipmentTypeId) ? ' selected' : ''}`} key={item.equipmentTypeId}>
      <div className="ai-card-heading">{item.imageUrl && /^https?:\/\//i.test(item.imageUrl) && <img src={item.imageUrl} alt={item.equipmentName} loading="lazy" />}<div><p className="eyebrow">GỢI Ý {item.rank} · {item.categoryName}</p><h2>{item.equipmentName}</h2>{item.description && <p>{item.description}</p>}</div></div>
      {item.specifications && <div><h3>Thông số kỹ thuật</h3><p className="preserve-lines">{item.specifications}</p></div>}<div className="ai-reason"><h3>Vì sao phù hợp?</h3><p className="preserve-lines">{item.reason}</p><h3>Cách sử dụng</h3><p className="preserve-lines">{item.usageNotes}</p></div>
      <p className="ai-stock">{!item.isActive ? 'Đã ngừng cho mượn' : item.available > 0 ? `${item.available}/${item.total} thiết bị khả dụng` : 'Hiện chưa có thiết bị khả dụng'}</p><div className="row-between"><Link to={`/equipment/${item.equipmentTypeId}`}>Xem chi tiết →</Link><label className="ai-select"><input type="checkbox" checked={chosen.includes(item.equipmentTypeId)} disabled={!item.isActive || item.available < 1} onChange={e => setSelected(ids => e.target.checked ? [...ids, item.equipmentTypeId] : ids.filter(id => id !== item.equipmentTypeId))} />Chọn thiết bị {item.equipmentName}</label></div>
    </article>)}</div>}
    <div className="surface ai-continue"><div><strong>Đã chọn {chosen.length} loại thiết bị</strong><p className="muted">Mặc định 1 chiếc mỗi loại. Bạn sẽ chọn hạn trả và xem lại trước khi gửi phiếu.</p></div><button disabled={!chosen.length} onClick={() => navigate('/loans/new', { state: { recommendationPrefill: { purpose: result.purpose, selections: chosen.map(equipmentTypeId => ({ equipmentTypeId, quantity: 1 })) } } })}>Tiếp tục tạo phiếu mượn →</button></div><p><Link to="/recommendations">Tạo tư vấn mới</Link> · <Link to="/catalog">Xem danh mục</Link></p>
  </>;
}

export function RecommendationHistoryPage() {
  const [params, setParams] = useSearchParams(); const page = Math.max(1, Math.min(100000, Number(params.get('page')) || 1));
  const result = useResource(`recommendation-history:${page}`, s => recommendationApi.history(page, s));
  return <section><div className="section-title"><div><p className="eyebrow">KHÔNG GIAN SINH VIÊN</p><h1>Lịch sử tư vấn AI</h1><p>Những ý tưởng và gợi ý thiết bị của riêng bạn.</p></div><Link className="button-link" to="/recommendations">Tư vấn mới</Link></div>{result.loading ? <PageLoading /> : result.error ? <ErrorState message={result.error} retry={result.reload} /> : !result.data?.items.length ? <EmptyState message="Bạn chưa có lịch sử tư vấn. Hãy bắt đầu với một ý tưởng thí nghiệm." /> : <><div className="loan-list">{result.data.items.map(item => <article className="surface" key={item.id}><p className="muted">{formatDate(item.createdAt)} · {item.itemCount} loại thiết bị</p><h2 className="preserve-lines">{item.purpose}</h2><Link to={`/recommendations/${item.id}`}>Xem gợi ý và tồn kho hiện tại →</Link></article>)}</div><Pager page={page} pageSize={result.data.pageSize} total={result.data.total} onChange={p => setParams({ page: String(p) })} /></>}</section>;
}
