import { useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { InlineError, Pager, ReviewDialog, useCommand, useResource } from '../../components/Workflow';
import { formatDate, loanStatuses, statusLabels, validateLoan } from '../../lib/workflows';
import { catalogApi, type EquipmentType } from '../catalog/api';
import { apiRequest } from '../../lib/api';
import { loanApi, type Loan, type LoanSelection, type ReturnCondition } from './api';

export function LoansPage({ staff = false }: { staff?: boolean }) {
  const [params, setParams] = useSearchParams(); const status = params.get('status') ?? ''; const page = Math.max(1, Number(params.get('page')) || 1);
  const result = useResource(`${staff}:${status}:${page}`, s => loanApi.list(staff, status, page, s), 15000);
  // At most three type previews per card, ten cards/page: at most thirty reads, four in flight.
  const previewIds = [...new Set(result.data?.items.flatMap(loan => [...new Set(loan.items.map(item => item.equipmentTypeId))].slice(0, 3)) ?? [])].sort();
  const typeNames = useResource(`loan-types:${previewIds.join(',')}`, async signal => {
    const names: Record<string, string> = {};
    for (let offset = 0; offset < previewIds.length; offset += 4) {
      const rows = await Promise.all(previewIds.slice(offset, offset + 4).map(id => catalogApi.detail(id, signal)));
      for (const row of rows) names[row.id] = row.name;
    }
    return names;
  });
  function update(changes: Record<string, string>) { setParams({ status, page: String(page), ...changes }); }
  const base = staff ? '/staff/loans' : '/loans';
  return <section><div className="section-title"><div><p className="eyebrow">{staff ? 'QUẢN LÝ PHÒNG LAB' : 'KHÔNG GIAN SINH VIÊN'}</p><h1>{staff ? 'Quản lý phiếu mượn' : 'Phiếu mượn của tôi'}</h1></div>{!staff && <Link className="button-link" to="/loans/new">Tạo phiếu mượn</Link>}</div>
    <div className="filters"><label>Trạng thái<select value={status} onChange={e => update({ status: e.target.value, page: '1' })}><option value="">Tất cả</option>{loanStatuses.map(s => <option key={s} value={s}>{statusLabels[s]}</option>)}</select></label><button onClick={result.reload} disabled={result.loading}>Tải lại</button><span>Dữ liệu tự cập nhật mỗi 15 giây khi mở trang.</span></div>
    {typeNames.error && <p role="alert">Chưa tải được tên thiết bị. Mã và số lượng vẫn hiển thị. <button className="button-quiet" onClick={typeNames.reload}>Tải lại tên thiết bị</button></p>}{result.loading ? <PageLoading /> : result.error ? <ErrorState message={result.error} retry={result.reload} /> : !result.data?.items.length ? <EmptyState message="Không có phiếu mượn phù hợp." /> : <><div className="loan-list">{result.data.items.map(loan => { const counts = new Map<string, number>(); for (const item of loan.items) counts.set(item.equipmentTypeId, (counts.get(item.equipmentTypeId) ?? 0) + 1); return <article className="surface" key={loan.id}><div className="row-between"><Link to={`${base}/${loan.id}`}><strong>Phiếu {loan.id.slice(0, 8)}</strong></Link><span className={`status-pill ${loan.isOverdue ? 'late' : ''}`}>{loan.isOverdue ? 'Quá hạn' : statusLabels[loan.status]}</span></div><p>{loan.purpose}</p><ul className="loan-equipment-list">{[...counts].slice(0, 3).map(([typeId, quantity]) => <li key={typeId}><Link to={`/equipment/${typeId}`}>{typeNames.data?.[typeId] ?? `Loại ${typeId.slice(0, 8)}`}</Link> × {quantity}</li>)}</ul>{counts.size > 3 && <p className="muted">Còn {counts.size - 3} loại khác trong chi tiết phiếu.</p>}<p>Ngày yêu cầu: {formatDate(loan.requestedAt)}</p><p>{loan.items.length} thiết bị · Hạn trả: {formatDate(loan.dueAt)}</p>{staff && <p className="identifier">Sinh viên: {loan.studentId}</p>}<Link to={`${base}/${loan.id}`}>Xem chi tiết và lịch sử →</Link></article>; })}</div><Pager page={page} pageSize={result.data.pageSize} total={result.data.total} onChange={p => update({ page: String(p) })} /></>}
  </section>;
}

export function CreateLoanPage() {
  const [params] = useSearchParams(); const navigate = useNavigate();
  const [selections, setSelections] = useState<LoanSelection[]>(() => params.get('equipmentTypeId') ? [{ equipmentTypeId: params.get('equipmentTypeId')!, quantity: 1 }] : []);
  const [selectedNames, setSelectedNames] = useState<Record<string, EquipmentType>>({});
  const [purpose, setPurpose] = useState(''); const [due, setDue] = useState(''); const [term, setTerm] = useState(''); const [search, setSearch] = useState('');
  const command = useCommand(); const [reviewCreate, setReviewCreate] = useState(false);
  const submission = useRef<{ fingerprint: string; key: string } | null>(null);
  const choices = useResource(search, s => catalogApi.list({ keyword: search, pageSize: 20 }, s));
  const chosen = useResource(selections.map(s => s.equipmentTypeId).join(','), async s => Promise.all(selections.map(item => catalogApi.detail(item.equipmentTypeId, s))));
  const known = Object.fromEntries([...(choices.data?.items ?? []), ...(chosen.data ?? []), ...Object.values(selectedNames)].map(item => [item.id, item]));
  function add(item: EquipmentType) { setSelectedNames(n => ({ ...n, [item.id]: item })); setSelections(rows => rows.some(r => r.equipmentTypeId === item.id) ? rows : [...rows, { equipmentTypeId: item.id, quantity: 1 }]); }
  async function submit(e: FormEvent) {
    e.preventDefault();
    await command.run(async () => {
      if (chosen.error || chosen.loading || chosen.data?.some(i => !i.isActive)) throw new Error('Hãy kiểm tra lại các loại thiết bị đã chọn.');
      validateLoan(purpose, due, selections.map(s => s.quantity));
      setReviewCreate(true);
    });
  }
  async function confirmCreate() {
    await command.run(async () => {
      const fingerprint = JSON.stringify([purpose.trim(), due, selections]);
      if (submission.current?.fingerprint !== fingerprint) submission.current = { fingerprint, key: crypto.randomUUID() };
      const loan = await loanApi.create(purpose, due, selections, Date.now(), submission.current.key);
      navigate(`/loans/${loan.id}`, { replace: true });
    });
  }
  return <section><Link to="/loans">← Phiếu mượn của tôi</Link><div className="detail-heading"><p className="eyebrow">YÊU CẦU MỚI</p><h1>Tạo phiếu mượn</h1><p>Mỗi số lượng tương ứng một thiết bị. Nhân viên sẽ xác nhận và giữ thiết bị khi duyệt.</p></div><div className="detail-grid"><form className="surface form-stack" onSubmit={e => void submit(e)}><label>Mục đích mượn<textarea value={purpose} onChange={e => setPurpose(e.target.value)} minLength={10} maxLength={2000} required rows={4} /></label><label>Hạn trả dự kiến<input type="datetime-local" step={1} value={due} onChange={e => setDue(e.target.value)} required /></label><p className="muted">Giờ trên màn hình là giờ địa phương, được gửi thành UTC. Hạn trả tối đa 30 ngày.</p><h2>Thiết bị đã chọn</h2>{chosen.loading && selections.length > 0 && <p role="status">Đang kiểm tra thiết bị…</p>}<InlineError message={chosen.error} />{!selections.length && <p>Chọn ít nhất một loại thiết bị ở danh sách bên cạnh.</p>}{selections.map(row => <div className="selection-row" key={row.equipmentTypeId}><div><strong>{known[row.equipmentTypeId]?.name ?? row.equipmentTypeId}</strong>{known[row.equipmentTypeId]?.isActive === false && <p role="alert">Đã ngừng cho mượn.</p>}</div><label>Số lượng<input aria-label={`Số lượng ${known[row.equipmentTypeId]?.name ?? row.equipmentTypeId}`} type="number" min={1} max={20} value={row.quantity} onChange={e => setSelections(rows => rows.map(r => r.equipmentTypeId === row.equipmentTypeId ? { ...r, quantity: Number(e.target.value) } : r))} /></label><button type="button" className="button-quiet" onClick={() => setSelections(rows => rows.filter(r => r.equipmentTypeId !== row.equipmentTypeId))}>Bỏ</button></div>)}<p>Tổng: {selections.reduce((sum, row) => sum + row.quantity, 0)}/20 thiết bị</p><InlineError message={command.error} />{command.error && <p>Nếu kết nối bị ngắt sau khi gửi, hãy <Link to="/loans">kiểm tra danh sách phiếu</Link> trước khi tạo lại.</p>}<button disabled={command.busy || !selections.length || chosen.loading}>{command.busy ? 'Đang kiểm tra…' : 'Xem lại yêu cầu'}</button></form>
      <aside className="surface"><h2>Chọn từ danh mục</h2><form className="form-stack" onSubmit={e => { e.preventDefault(); setSearch(term); }}><label>Tìm thiết bị<input value={term} maxLength={100} onChange={e => setTerm(e.target.value)} /></label><button>Tìm</button></form>{choices.loading ? <p role="status">Đang tải…</p> : choices.error ? <ErrorState message={choices.error} retry={choices.reload} /> : <><p className="muted">Hiển thị tối đa 20 kết quả. Có thể tìm thêm theo tên hoặc <Link to="/catalog">mở danh mục đầy đủ</Link>.</p><ul className="choice-list">{choices.data?.items.map(item => <li key={item.id}><span>{item.name}</span><button type="button" disabled={selections.some(s => s.equipmentTypeId === item.id) || selections.length >= 20} onClick={() => add(item)}>{selections.some(s => s.equipmentTypeId === item.id) ? 'Đã chọn' : 'Thêm'}</button></li>)}</ul></>}</aside></div>{reviewCreate && <ReviewDialog title="Xem lại yêu cầu mượn" busy={command.busy} onClose={() => setReviewCreate(false)} onConfirm={() => void confirmCreate()} confirmLabel="Gửi yêu cầu mượn"><p className="preserve-lines">{purpose.trim()}</p><p>Hạn trả: {formatDate(new Date(due).toISOString())}</p><ul>{selections.map(row => <li key={row.equipmentTypeId}>{known[row.equipmentTypeId]?.name ?? row.equipmentTypeId} × {row.quantity}</li>)}</ul><p>Nhân viên sẽ duyệt và cấp thiết bị sau khi gửi.</p><InlineError message={command.error} />{command.error && <p>Nếu kết quả chưa rõ, hãy kiểm tra phiếu đã tạo trước khi thay đổi yêu cầu. Gửi lại cùng nội dung sử dụng cùng mã chống trùng.</p>}</ReviewDialog>}</section>;
}

export function LoanDetailPage() {
  const { id = '' } = useParams(); const { user } = useAuth(); const staff = user?.role === 'LAB_STAFF' || user?.role === 'ADMIN';
  const result = useResource(id, s => loanApi.detail(id, s), 15000); const command = useCommand();
  const [cancelConfirm, setCancelConfirm] = useState(false); const [review, setReview] = useState<'approve' | 'reject' | 'handover' | ''>('');
  const [returnReview, setReturnReview] = useState(false); const [notice, setNotice] = useState('');
  const [reason, setReason] = useState(''); const [returns, setReturns] = useState<Record<string, ReturnCondition>>({});
  const details = useResource(`names:${id}:${result.data?.items.map(i => i.equipmentTypeId).join(',') ?? ''}`, async s => {
    const ids = [...new Set(result.data?.items.map(i => i.equipmentTypeId) ?? [])];
    return Object.fromEntries(await Promise.all(ids.map(async type => [type, (await catalogApi.detail(type, s)).name])));
  });
  const units = useResource(`units:${staff}:${result.data?.items.map(i => i.assetUnitId).join(',') ?? ''}`, async signal => {
    if (!staff) return {} as Record<string, string>;
    const ids = [...new Set(result.data?.items.flatMap(i => i.assetUnitId ? [i.assetUnitId] : []) ?? [])];
    return Object.fromEntries(await Promise.all(ids.map(async assetId => {
      const unit = await apiRequest<{ serialNumber: string }>(`/api/inventory/assets/${assetId}`, { signal });
      return [assetId, unit.serialNumber];
    })));
  });
  if (result.loading) return <PageLoading />;
  if (result.error || !result.data) return <ErrorState message={result.error ?? 'Không tìm thấy phiếu.'} retry={result.reload} />;
  const loan = result.data;
  async function action(e: FormEvent) { e.preventDefault(); await command.run(async () => { if (review === 'reject' && reason.trim().length < 3) throw new Error('Lý do từ chối cần từ 3 đến 1.000 ký tự.'); if (review === 'approve') await loanApi.approve(id); else if (review === 'handover') await loanApi.handover(id); else if (review === 'reject') await loanApi.reject(id, reason); setReview(''); setReason(''); result.reload(); }); }
  async function receive() {
    await command.run(async () => {
      const items = Object.entries(returns).map(([loanItemId, condition]) => ({ loanItemId, condition }));
      if (!items.length || items.some(item => !loan.items.some(i => i.id === item.loanItemId && i.itemStatus === 'BORROWED'))) throw new Error('Thiết bị đã thay đổi. Hãy tải lại và chọn các mục chưa trả.');
      const updated = await loanApi.returns(id, items);
      setNotice(`Đã ghi nhận ${items.length} thiết bị; còn ${updated.items.filter(i => i.itemStatus === 'BORROWED').length} thiết bị chưa trả.`);
      setReturnReview(false); setReturns({}); result.reload();
    });
  }
  return <section><Link to={staff ? '/staff/loans' : '/loans'}>← Danh sách phiếu mượn</Link><div className="detail-heading"><p className="eyebrow">PHIẾU {loan.id.slice(0, 8)}</p><h1>Chi tiết phiếu mượn</h1><span className={`status-pill ${loan.isOverdue ? 'late' : ''}`}>{statusLabels[loan.status]}{loan.isOverdue ? ' · Quá hạn' : ''}</span></div><InlineError message={command.error} />{notice && <p role="status" className="success-message">{notice}</p>}<div className="actions"><button className="button-quiet" onClick={result.reload} disabled={command.busy}>Tải lại trạng thái</button></div><div className="detail-grid"><article className="surface"><h2>Thông tin yêu cầu</h2><p className="preserve-lines">{loan.purpose}</p><dl className="metadata"><dt>Mã phiếu</dt><dd className="identifier">{loan.id}</dd><dt>Người mượn</dt><dd className="identifier">{loan.studentId}</dd><dt>Ngày yêu cầu</dt><dd>{formatDate(loan.requestedAt)}</dd><dt>Hạn trả</dt><dd>{formatDate(loan.dueAt)}</dd><dt>Người duyệt</dt><dd className="identifier">{loan.reviewedBy ?? '—'}</dd><dt>Thời điểm duyệt</dt><dd>{formatDate(loan.reviewedAt)}</dd><dt>Người bàn giao</dt><dd className="identifier">{loan.handedOverBy ?? '—'}</dd><dt>Thời điểm bàn giao</dt><dd>{formatDate(loan.handedOverAt)}</dd></dl>{loan.rejectionReason && <p>Lý do từ chối: {loan.rejectionReason}</p>}
      {!staff && loan.status === 'PENDING' && (cancelConfirm ? <div className="confirmation"><p>Xác nhận hủy yêu cầu này?</p><button disabled={command.busy} onClick={() => void command.run(async () => { await loanApi.cancel(id); setCancelConfirm(false); result.reload(); })}>Xác nhận hủy phiếu</button><button className="button-quiet" disabled={command.busy} onClick={() => setCancelConfirm(false)}>Giữ phiếu</button></div> : <button onClick={() => setCancelConfirm(true)}>Hủy phiếu mượn</button>)}
      {staff && <StaffActions loan={loan} busy={command.busy} review={review} setReview={setReview} reason={reason} setReason={setReason} onSubmit={action} />}</article>
      <aside className="surface"><h2>Lịch sử xử lý</h2><ol className="timeline">{loan.history.map(h => <li key={h.id}><strong>{statusLabels[h.toStatus] ?? h.action}</strong><time>{formatDate(h.occurredAt)}</time><span className="identifier">Người thực hiện: {h.actorUserId === '00000000-0000-0000-0000-000000000000' ? 'Hệ thống' : h.actorUserId}</span>{h.note && <p>{h.note}</p>}</li>)}</ol></aside></div>
      <article className="surface section-space"><h2>Thiết bị trong phiếu</h2>{details.error && <p>Chưa tải được tên thiết bị; mã thiết bị vẫn hiển thị. <button className="button-quiet" onClick={details.reload}>Tải lại tên</button></p>}{staff && units.error && <p role="alert">Chưa tải được serial; mã thiết bị vẫn hiển thị. <button className="button-quiet" onClick={units.reload}>Thử lại serial</button></p>}<div className="table-scroll"><table><thead><tr><th>Loại thiết bị</th><th>Thiết bị được cấp</th><th>Trạng thái</th><th>Trả lúc</th></tr></thead><tbody>{loan.items.map(item => <tr key={item.id}><td><Link to={`/equipment/${item.equipmentTypeId}`}>{details.data?.[item.equipmentTypeId] ?? item.equipmentTypeId}</Link><small className="identifier">Loan item: {item.id}</small></td><td className="identifier">{item.assetUnitId ? <>{units.data?.[item.assetUnitId] ?? item.assetUnitId}{units.data?.[item.assetUnitId] && <small>{item.assetUnitId}</small>}</> : 'Chưa cấp phát'}</td><td>{statusLabels[item.itemStatus] ?? item.itemStatus}{item.returnCondition && <small>{item.returnCondition}</small>}</td><td>{formatDate(item.returnedAt)}</td></tr>)}</tbody></table></div>
      {staff && loan.status === 'BORROWED' && <form className="form-stack section-space" onSubmit={e => { e.preventDefault(); command.clearError(); setReturnReview(true); }}><h3>Nhận trả từng thiết bị</h3><p>Chọn các thiết bị thực tế đã nhận và tình trạng tương ứng. Những mục chưa nhận tiếp tục ở trạng thái đang mượn.</p>{loan.items.filter(i => i.itemStatus === 'BORROWED').map(item => <label key={item.id}>{details.data?.[item.equipmentTypeId] ?? 'Thiết bị'} · {item.id.slice(0, 8)}<select aria-label={`Tình trạng trả ${item.id.slice(0, 8)}`} value={returns[item.id] ?? ''} onChange={e => setReturns(current => { const next = { ...current }; if (e.target.value) next[item.id] = e.target.value as ReturnCondition; else delete next[item.id]; return next; })}><option value="">Chưa nhận trả</option><option value="NORMAL">Bình thường</option><option value="DAMAGED">Hư hỏng → bảo trì</option><option value="LOST">Thất lạc</option></select></label>)}<button disabled={command.busy || !Object.keys(returns).length}>{command.busy ? 'Đang ghi nhận…' : 'Xem lại thiết bị nhận trả'}</button></form>}</article>{returnReview && <ReviewDialog title="Xác nhận tình trạng nhận trả" busy={command.busy} onClose={() => setReturnReview(false)} onConfirm={() => void receive()} confirmLabel="Xác nhận nhận trả"><p>Chỉ xác nhận thiết bị đã kiểm tra và nhận thực tế.</p><ul>{Object.entries(returns).map(([itemId, condition]) => <li key={itemId}>{itemId.slice(0, 8)} · {returnLabels[condition]}</li>)}</ul><InlineError message={command.error} /></ReviewDialog>}<p className="section-space"><Link to={`/notifications?loanId=${loan.id}`}>Xem thông báo của bạn về phiếu này →</Link></p></section>;
}
function StaffActions({ loan, busy, review, setReview, reason, setReason, onSubmit }: { loan: Loan; busy: boolean; review: string; setReview: (v: 'approve' | 'reject' | 'handover' | '') => void; reason: string; setReason: (v: string) => void; onSubmit: (e: FormEvent) => Promise<void> }) {
  return <div className="section-space"><div className="actions">{loan.status === 'PENDING' && <><button disabled={busy} onClick={() => setReview('approve')}>Duyệt phiếu</button><button className="button-quiet" disabled={busy} onClick={() => setReview('reject')}>Từ chối</button></>}{loan.status === 'APPROVED' && <button disabled={busy} onClick={() => setReview('handover')}>Bàn giao thiết bị</button>}</div>{review && <form className="form-stack confirmation" onSubmit={e => void onSubmit(e)}><p>{review === 'approve' ? 'Xác nhận duyệt và giữ toàn bộ thiết bị cho phiếu này?' : review === 'handover' ? 'Xác nhận đã bàn giao các thiết bị đúng người mượn?' : 'Vui lòng ghi rõ lý do từ chối.'}</p>{review === 'reject' && <label>Lý do từ chối<textarea value={reason} onChange={e => setReason(e.target.value)} required minLength={3} maxLength={1000} /></label>}<div className="actions"><button disabled={busy}>{busy ? 'Đang xử lý…' : 'Xác nhận thao tác'}</button><button type="button" className="button-quiet" disabled={busy} onClick={() => setReview('')}>Quay lại</button></div></form>}{loan.status === 'APPROVING' && <p>Đang xác minh giữ thiết bị. Tải lại trạng thái trước khi thực hiện yêu cầu khác.</p>}</div>;
}

const returnLabels: Record<ReturnCondition, string> = { NORMAL: 'Bình thường → sẵn sàng', DAMAGED: 'Hư hỏng → bảo trì', LOST: 'Thất lạc' };
