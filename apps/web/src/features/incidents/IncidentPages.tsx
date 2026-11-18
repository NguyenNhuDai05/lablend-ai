import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { EmptyState, ErrorState, PageLoading } from '../../components/States';
import { InlineError, Pager, ReviewDialog, ShortId, useCommand, useResource } from '../../components/Workflow';
import { formatDate } from '../../lib/workflows';
import { accountApi } from '../account/api';
import { catalogApi } from '../catalog/api';
import { loanApi } from '../loans/api';
import { incidentApi, incidentLabels, outcomeLabels, problemLabels, validateImages, type Incident } from './api';
import './incidents.css';

const closed = (status: string) => status.startsWith('RESOLVED_') || status === 'CANCELLED';
function useDebounced(value: string) {
  const [settled, setSettled] = useState(value);
  useEffect(() => { const timer = setTimeout(() => setSettled(value), 300); return () => clearTimeout(timer); }, [value]);
  return settled;
}
function localDate(value: string | null) {
  if (!value) return '';
  const date = new Date(value); return Number.isFinite(date.getTime()) ? new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 10) : '';
}
const dateBoundary = (value: string, end = false) => value ? new Date(`${value}T${end ? '23:59:59.999' : '00:00:00'}`).toISOString() : '';
export function EquipmentName({ id }: { id: string }) {
  const type = useResource(`incident-type:${id}`, signal => catalogApi.detail(id, signal));
  return <Link to={`/equipment/${id}`}>{type.data?.name ?? `Thiết bị ${id.slice(0, 8)}`}</Link>;
}
export function IncidentListPage({ staff = false, admin = false }: { staff?: boolean; admin?: boolean }) {
  const [params, setParams] = useSearchParams(); const [draft, setDraft] = useState(params.get('search') ?? '');
  const [typeTerm, setTypeTerm] = useState(''), [staffTerm, setStaffTerm] = useState('');
  const typeQuery = useDebounced(typeTerm), staffQuery = useDebounced(staffTerm);
  const page = Math.max(1, Number(params.get('page')) || 1); const filters = new URLSearchParams(params); filters.set('pageSize', '10'); filters.set('page', String(page));
  const result = useResource(`incidents:${staff || admin}:${filters}`, signal => incidentApi.list(staff || admin, filters, signal), 15000);
  const types = useResource(`incident-filter-types:${staff || admin}:${typeQuery}`, signal => staff || admin ? catalogApi.list({ pageSize: 100, keyword: typeQuery }, signal) : Promise.resolve({ items: [], total: 0, page: 1, pageSize: 100 }));
  const people = useResource(`incident-staff-filter:${admin}:${staffQuery}`, async signal => admin
    ? (await Promise.all(['LAB_STAFF', 'ADMIN'].map(role => accountApi.list(1, signal, { role, keyword: staffQuery })))).flatMap(p => p.items) : []);
  const stats = useResource(`incident-stats:${admin}:${filters}`, signal => admin ? incidentApi.stats(filters, signal) : Promise.resolve(null), admin ? 15000 : 0);
  function update(changes: Record<string, string>) { const next = new URLSearchParams(params); next.set('page', '1'); for (const [key, value] of Object.entries(changes)) value ? next.set(key, value) : next.delete(key); setParams(next); }
  const base = staff || admin ? '/staff/incidents' : '/incidents';
  return <section className="incident-page"><div className="section-title"><div><p className="eyebrow">SỰ CỐ & CẤP ĐỔI THIẾT BỊ</p><h1>{admin ? 'Thống kê sự cố thiết bị' : staff ? 'Tiếp nhận sự cố thiết bị' : 'Sự cố thiết bị của tôi'}</h1></div><button className="button-quiet" onClick={() => { result.reload(); stats.reload(); }}>Tải lại</button></div>
    <p>Ảnh và mô tả là thông tin hỗ trợ. Nhân viên cần kiểm tra thiết bị thực tế trước khi kết luận hoặc cấp đổi.</p>
    <form className="filters" onSubmit={e => { e.preventDefault(); update({ search: draft }); }}><label>Mã sự cố hoặc serial<input value={draft} maxLength={100} onChange={e => setDraft(e.target.value)} /></label>
      <label>Trạng thái sự cố<select value={params.get('status') ?? ''} onChange={e => update({ status: e.target.value })}><option value="">Tất cả</option>{Object.entries(incidentLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      {(staff || admin) && <><label>Từ ngày<input type="date" value={localDate(params.get('from'))} onChange={e => update({ from: dateBoundary(e.target.value) })} /></label><label>Đến ngày<input type="date" value={localDate(params.get('to'))} onChange={e => update({ to: dateBoundary(e.target.value, true) })} /></label>
        <label>Tìm loại thiết bị<input type="search" value={typeTerm} maxLength={100} onChange={e => setTypeTerm(e.target.value)} placeholder="Tên thiết bị cần lọc" /></label><label>Loại thiết bị<select aria-label="Loại thiết bị" value={params.get('equipmentTypeId') ?? ''} onChange={e => update({ equipmentTypeId: e.target.value })}><option value="">Tất cả</option>{types.data?.items.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label></>}
      {admin && <><label>Tìm nhân viên<input type="search" value={staffTerm} maxLength={100} onChange={e => setStaffTerm(e.target.value)} placeholder="Tên hoặc email nhân viên" /></label><label>Nhân viên kiểm tra hoặc cấp đổi<select value={params.get('staffId') ?? ''} onChange={e => update({ staffId: e.target.value })}><option value="">Tất cả</option>{people.data?.map(p => <option key={p.id} value={p.id}>{p.displayName || p.email}</option>)}</select></label></>}<button>Tìm sự cố</button></form>
    {(types.error || people.error) && <p role="alert">Chưa tải đủ bộ lọc. <button className="button-quiet" onClick={() => { types.reload(); people.reload(); }}>Thử lại bộ lọc</button></p>}
    {admin && (stats.loading ? <PageLoading /> : stats.error ? <ErrorState message={stats.error} retry={stats.reload} /> : stats.data && <><div className="kpi-grid incident-kpis">{[['Tổng sự cố', stats.data.total], ['Chờ kiểm tra', stats.data.awaitingInspection], ['Chờ cấp đổi', stats.data.waitingReplacement], ['Đã cấp đổi', stats.data.successfullyReplaced], ['Đóng không cấp đổi', stats.data.closedWithoutReplacement], ['Giờ xử lý trung bình', stats.data.averageResolutionHours?.toFixed(1) ?? 'Chưa có']].map(([label, value]) => <article className="surface" key={label}><span>{label}</span><strong>{value}</strong></article>)}</div><article className="surface section-space"><h2>Thiết bị có lỗi đã được xác nhận</h2><p className="muted">Chỉ tính kết luận lỗi sau kiểm tra; không coi mọi báo cáo là lỗi thiết bị đã xác minh.</p>{stats.data.verifiedFailures.length ? <ul>{stats.data.verifiedFailures.map(f => <li key={f.equipmentTypeId}><EquipmentName id={f.equipmentTypeId} /> · {f.count} lần</li>)}</ul> : <p>Chưa có lỗi thiết bị được xác nhận trong bộ lọc.</p>}</article></>)}
    {result.loading ? <PageLoading /> : result.error ? <ErrorState message={result.error} retry={result.reload} /> : !result.data?.items.length ? <EmptyState message="Chưa có sự cố phù hợp. Bạn có thể báo sự cố từ chi tiết phiếu đang mượn." /> : <><div className="loan-list section-space">{result.data.items.map(i => <article className="surface" key={i.id}><div className="row-between"><Link to={`${base}/${i.id}`}><strong>{i.code}</strong></Link><span className="status-pill">{incidentLabels[i.status]}</span></div><h2><EquipmentName id={i.equipmentTypeId} /></h2><p>Serial: <strong className="identifier">{i.serialNumber}</strong></p><p className="preserve-lines">{i.description}</p><p>{formatDate(i.createdAt)} · {i.replacementRequested ? 'Có yêu cầu cấp đổi' : 'Không yêu cầu cấp đổi'}</p><Link to={`${base}/${i.id}`}>Xem kết quả và lịch sử →</Link></article>)}</div><Pager page={page} total={result.data.total} pageSize={10} onChange={p => update({ page: String(p) })} /></>}
  </section>;
}

export function NewIncidentPage() {
  const [params] = useSearchParams(); const loanId = params.get('loanId') ?? '', itemId = params.get('loanItemId') ?? '';
  const { user } = useAuth(); const navigate = useNavigate(); const command = useCommand(); const key = useRef(crypto.randomUUID());
  const [description, setDescription] = useState(''), [category, setCategory] = useState('POWER_FAILURE'), [replacement, setReplacement] = useState(true), [acknowledged, setAcknowledged] = useState(false);
  const [images, setImages] = useState<File[]>([]), [review, setReview] = useState(false), [fileError, setFileError] = useState('');
  const context = useResource(`report:${loanId}:${itemId}`, async signal => ({ loan: await loanApi.detail(loanId, signal), assets: await incidentApi.assets(loanId, signal) }));
  if (context.loading) return <PageLoading />;
  if (context.error || !context.data) return <ErrorState message={context.error ?? 'Không tìm thấy phiếu.'} retry={context.reload} />;
  const item = context.data.loan.items.find(i => i.id === itemId), asset = context.data.assets.find(a => a.loanItemId === itemId);
  if (context.data.loan.studentId !== user?.id || context.data.loan.status !== 'BORROWED' || item?.itemStatus !== 'BORROWED' || !asset || asset.reservationStatus !== 'HANDED_OVER') return <EmptyState message="Chỉ báo sự cố cho thiết bị của bạn hiện đang mượn. Mở lại phiếu để kiểm tra trạng thái." />;
  async function submit() { await command.run(async () => {
    validateImages(images);
    const incident = await incidentApi.report({ loanId, loanItemId: itemId, problemCategory: category, description: description.trim(), replacementRequested: replacement }, key.current);
    const failed: string[] = [];
    for (const image of images) { try { await incidentApi.upload(incident.id, image); } catch { failed.push(image.name); } }
    navigate(`/incidents/${incident.id}`, { replace: true, state: { notice: failed.length ? `Đã lưu sự cố. ${failed.length} ảnh chưa tải thành công; bạn có thể gửi lại ở đây.` : 'Đã gửi sự cố. Mang thiết bị đến phòng lab để kiểm tra trực tiếp.' } });
  }); }
  return <section className="incident-page"><Link to={`/loans/${loanId}`}>← Chi tiết phiếu mượn</Link><h1>Báo sự cố thiết bị</h1><article className="surface"><h2><EquipmentName id={item.equipmentTypeId} /></h2><p>Serial: <strong>{asset.serialNumber}</strong> · Phiếu <ShortId id={loanId} /></p><form className="form-stack" onSubmit={e => { e.preventDefault(); if (!fileError) setReview(true); }}>
    <label>Nhóm vấn đề<select value={category} onChange={e => setCategory(e.target.value)}>{Object.entries(problemLabels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select></label><p className="muted">Đây là mô tả ban đầu, không phải kết luận lỗi hoặc quy trách nhiệm.</p>
    <label>Mô tả sự cố<textarea required minLength={10} maxLength={2000} value={description} onChange={e => setDescription(e.target.value)} /></label>
    <label>Ảnh hỗ trợ (không bắt buộc)<input type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={e => { const files = [...(e.target.files ?? [])]; setImages(files); try { validateImages(files); setFileError(''); } catch (error) { setFileError((error as Error).message); } }} /></label><p className="muted">Tối đa 3 ảnh, mỗi ảnh 5 MB. Ảnh được lưu riêng tư cùng dữ liệu sự cố và chỉ người có quyền mới xem được.</p><InlineError message={fileError} />
    <label className="incident-check"><input type="checkbox" checked={replacement} onChange={e => setReplacement(e.target.checked)} />Tôi muốn nhận thiết bị thay thế nếu lỗi được xác nhận</label>
    <label className="incident-check"><input type="checkbox" required checked={acknowledged} onChange={e => setAcknowledged(e.target.checked)} />Tôi hiểu rằng phải mang thiết bị đến nhân viên để kiểm tra trực tiếp</label>
    <button disabled={command.busy || !acknowledged || !!fileError}>Xem lại báo cáo</button></form></article>
    {review && <ReviewDialog title="Xác nhận báo sự cố" busy={command.busy} onClose={() => setReview(false)} onConfirm={() => void submit()} confirmLabel="Gửi báo sự cố"><p>{asset.serialNumber} · {problemLabels[category]}</p><p className="preserve-lines">{description}</p><p>{replacement ? 'Có yêu cầu thiết bị thay thế cùng loại.' : 'Không yêu cầu cấp đổi.'}</p><InlineError message={command.error} /></ReviewDialog>}
  </section>;
}

export function IncidentDetailPage() {
  const { id = '' } = useParams(); const { user } = useAuth(); const location = useLocation(); const staff = user?.role === 'LAB_STAFF' || user?.role === 'ADMIN';
  const result = useResource(`incident:${id}`, signal => incidentApi.detail(id, signal), 5000); const command = useCommand();
  const [notice, setNotice] = useState<string>(location.state?.notice ?? ''); const [action, setAction] = useState(''); const operationKey = useRef(crypto.randomUUID());
  const [notes, setNotes] = useState(''), [outcome, setOutcome] = useState('DEFECTIVE'), [corrective, setCorrective] = useState(''), [received, setReceived] = useState(false), [serial, setSerial] = useState(''), [handover, setHandover] = useState(false);
  const loan = useResource(`incident-loan:${result.data?.loanId}:${result.data?.updatedAt}`, signal => result.data ? loanApi.detail(result.data.loanId, signal) : Promise.resolve(null));
  const stock = useResource(`incident-stock:${result.data?.equipmentTypeId}`, signal => result.data ? catalogApi.availability([result.data.equipmentTypeId], signal) : Promise.resolve([]), 10000);
  function confirm(next: string) { command.clearError(); operationKey.current = crypto.randomUUID(); setAction(next); }
  async function execute() { await command.run(async () => {
    const body = action === 'inspection' ? { outcome, notes: notes.trim(), correctiveAction: corrective.trim() || null, physicallyReceived: outcome === 'LOST' ? false : received, verifiedSerialNumber: outcome === 'LOST' ? null : serial.trim() }
      : action === 'resolve' ? { notes: notes.trim() } : action === 'replace' ? { confirmHandover: handover } : undefined;
    await incidentApi.command(id, action, body, action === 'replace' || action === 'defective-return' ? operationKey.current : undefined);
    setAction(''); setNotice('Đã ghi nhận thao tác.'); result.reload(); loan.reload(); stock.reload(); window.dispatchEvent(new Event('lablend:notifications-changed'));
  }); }
  if (result.loading) return <PageLoading />;
  if (result.error || !result.data) return <ErrorState message={result.error ?? 'Không tìm thấy sự cố.'} retry={result.reload} />;
  const incident = result.data; const original = loan.data?.items.find(i => i.id === incident.loanItemId); const returned = original?.itemStatus === 'DAMAGED' || original?.itemStatus === 'LOST';
  const confirmed = incident.status === 'CONFIRMED_DEFECT' || incident.status === 'WAITING_REPLACEMENT';
  const canReplace = confirmed && incident.replacementRequested && incident.inspection?.outcome === 'DEFECTIVE' && (!returned || incident.waitingConsented) && !!loan.data && new Date(loan.data.dueAt).getTime() > Date.now();
  return <section className="incident-page"><Link to={staff ? '/staff/incidents' : '/incidents'}>← Danh sách sự cố</Link><div className="section-title"><div><p className="eyebrow">{incident.code}</p><h1>Chi tiết sự cố thiết bị</h1></div><span className="status-pill">{incidentLabels[incident.status]}</span></div>
    {notice && !closed(incident.status) && <p role="status" className="success-message">{incident.status === 'REPORTED' ? notice : 'Trạng thái sự cố đã được cập nhật.'}</p>}<InlineError message={command.error} /><button className="button-quiet" onClick={() => { result.reload(); loan.reload(); stock.reload(); }}>Tải lại trạng thái</button>
    <div className="detail-grid section-space"><article className="surface"><h2><EquipmentName id={incident.equipmentTypeId} /></h2><dl className="metadata"><dt>Serial gốc</dt><dd className="identifier">{incident.serialNumber}</dd><dt>Người mượn</dt><dd><ShortId id={incident.studentId} /></dd><dt>Phiếu gốc</dt><dd><Link to={`${staff ? '/staff' : ''}/loans/${incident.loanId}`}>{incident.loanId.slice(0, 8)}</Link></dd><dt>Hạn trả gốc</dt><dd>{loan.data ? formatDate(loan.data.dueAt) : 'Đang tải…'}</dd><dt>Ngày báo</dt><dd>{formatDate(incident.createdAt)}</dd><dt>Nhóm vấn đề</dt><dd>{problemLabels[incident.problemCategory]}</dd><dt>Yêu cầu cấp đổi</dt><dd>{incident.replacementRequested ? 'Có' : 'Không'}</dd></dl><p className="preserve-lines">{incident.description}</p><p className="muted">Báo cáo không phải kết luận lỗi hoặc quy trách nhiệm. Cần kiểm tra thiết bị thực tế.</p>
      <EvidencePanel incident={incident} canUpload={!staff && incident.status === 'REPORTED'} reload={result.reload} />
      {incident.inspection && <div className="section-space"><h2>Kết quả kiểm tra</h2><p><strong>{outcomeLabels[incident.inspection.outcome]}</strong></p><p className="preserve-lines">{incident.inspection.notes}</p>{incident.inspection.correctiveAction && <p>Khắc phục: {incident.inspection.correctiveAction}</p>}<p>Nhân viên: <ShortId id={incident.inspection.inspectedBy} /> · {formatDate(incident.inspection.inspectedAt)}</p><p>{incident.inspection.physicallyReceivedAt ? `Đã nhận và đối chiếu serial: ${formatDate(incident.inspection.physicallyReceivedAt)}` : 'Chưa nhận được thiết bị thực tế; ghi nhận thất lạc.'}</p></div>}
      {incident.resolution && <p className="success-message">{incident.resolution}</p>}
      {incident.replacements.filter(r => r.replacedAt).map(r => <div className="confirmation" key={r.operationId}><h2>Thiết bị đã cấp đổi</h2><p>Serial mới: <strong>{r.serialNumber}</strong></p><p>{formatDate(r.replacedAt)}</p><Link to={`${staff ? '/staff' : ''}/loans/${r.replacementLoanId}`}>{r.replacementLoanId === incident.loanId ? 'Xem thiết bị mới trên phiếu mượn gốc' : 'Xem phiếu tiếp nối cấp đổi (phiếu riêng)'}</Link></div>)}
    </article><aside className="surface"><h2>Lịch sử sự cố</h2><ol className="timeline">{incident.history.map(h => <li key={h.id}><strong>{actionLabel(h.action) || incidentLabels[h.toStatus]}</strong><span>{incidentLabels[h.toStatus]}</span><time>{formatDate(h.occurredAt)}</time><span>Người thực hiện: <ShortId id={h.actorUserId} /></span>{h.notes && <p className="preserve-lines">{historyNote(h.notes)}</p>}</li>)}</ol></aside></div>
    {!closed(incident.status) && <article className="surface section-space"><h2>{staff ? 'Tiếp nhận và xử lý' : 'Bước tiếp theo'}</h2>
      {(incident.status === 'REPLACEMENT_IN_PROGRESS' || incident.status === 'MANUAL_REVIEW') ? <><p>Thao tác đang được đối soát giữa các dịch vụ. Chưa xác nhận đã cấp đổi. Không tạo một lần cấp mới khi kết quả chưa rõ.</p>{incident.pendingOperationId && <p>Mã đối soát: <ShortId id={incident.pendingOperationId} /></p>}{staff && <button disabled={command.busy} onClick={() => confirm('retry-operation')}>Đối soát lại thao tác đã lưu</button>}</>
      : staff ? <><p>Tồn kho cùng loại: {stock.data?.[0] ? `${stock.data[0].available} thiết bị sẵn sàng` : 'Đang tải…'}. Tồn kho được kiểm tra lại khi xác nhận cấp đổi.</p>{stock.error && <ErrorState message={stock.error} retry={stock.reload} />}
        {incident.status === 'REPORTED' && <button disabled={command.busy} onClick={() => confirm('start-inspection')}>Bắt đầu kiểm tra</button>}
        {incident.status === 'INSPECTING' && <form className="form-stack" onSubmit={(e: FormEvent) => { e.preventDefault(); confirm('inspection'); }}><label>Kết luận kiểm tra<select aria-label="Kết luận kiểm tra" value={outcome} onChange={e => setOutcome(e.target.value)}>{Object.entries(outcomeLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></label><label>Kết quả kiểm tra<textarea required minLength={10} maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
          {outcome === 'FIXED' && <label>Biện pháp khắc phục<textarea required minLength={10} maxLength={2000} value={corrective} onChange={e => setCorrective(e.target.value)} /></label>}
          {outcome !== 'LOST' ? <><label>Serial đối chiếu thực tế<input required value={serial} onChange={e => setSerial(e.target.value)} autoComplete="off" /></label><label className="incident-check"><input type="checkbox" required checked={received} onChange={e => setReceived(e.target.checked)} />Tôi đã trực tiếp nhận, đối chiếu đúng thiết bị và serial</label></> : <p>Thiết bị thất lạc: cần ghi nhận theo luồng trả thất lạc, không cấp đổi.</p>}<button disabled={command.busy}>Xem lại kết luận</button></form>}
        {confirmed && <><p>{returned ? incident.waitingConsented ? 'Người mượn đã đồng ý chờ. Chỉ cấp cùng loại, còn hạn và bàn giao trực tiếp.' : 'Đã nhận trả máy lỗi. Sinh viên có thể chọn chờ; hoặc nhân viên đóng sự cố nếu không còn nhu cầu.' : 'Có thể cấp đổi ngay nếu còn thiết bị; nếu hết hàng, nhận trả máy lỗi và để sinh viên quyết định chờ.'}</p>
          {canReplace && <><label className="incident-check"><input type="checkbox" checked={handover} onChange={e => setHandover(e.target.checked)} />Xác nhận đang bàn giao thiết bị thay thế cho đúng người mượn</label><button disabled={command.busy || !handover} onClick={() => confirm('replace')}>Cấp đổi thiết bị cùng loại</button></>}
          {!returned && loan.data && <button className="button-quiet" disabled={command.busy} onClick={() => confirm('defective-return')}>{incident.inspection?.outcome === 'LOST' ? 'Ghi nhận thiết bị thất lạc' : 'Nhận trả thiết bị lỗi, chưa cấp đổi'}</button>}
          {returned && <form className="form-stack section-space" onSubmit={e => { e.preventDefault(); confirm('resolve'); }}><label>Lý do đóng không cấp đổi<textarea required minLength={10} maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label><button className="button-quiet" disabled={command.busy}>Đóng sự cố không cấp đổi</button></form>}
          {loan.data && new Date(loan.data.dueAt).getTime() <= Date.now() && <p role="alert">Hạn trả gốc đã hết; không thể cấp đổi theo hạn này. Liên hệ phòng lab để xử lý nhu cầu mượn mới.</p>}</>}
      </> : <><p>{incident.status === 'WAITING_REPLACEMENT' ? 'Bạn đang chờ thiết bị cùng loại. Liên hệ phòng lab để kiểm tra tồn kho và nhận bàn giao; phiếu đã trả sẽ không tự mở lại.' : returned ? 'Thiết bị lỗi đã được nhận trả. Bạn có thể chọn chờ cấp đổi khi còn hạn, hoặc liên hệ nhân viên để đóng sự cố.' : 'Mang thiết bị đến phòng lab để nhân viên kiểm tra trực tiếp. Bạn chưa được coi là đã trả cho đến khi nhân viên xác nhận.'}</p>
        {incident.status === 'CONFIRMED_DEFECT' && returned && incident.replacementRequested && incident.inspection?.outcome === 'DEFECTIVE' && <button disabled={command.busy} onClick={() => confirm('wait')}>Tôi đồng ý chờ thiết bị cùng loại</button>}
        {(incident.status === 'REPORTED' || incident.status === 'WAITING_REPLACEMENT') && <button className="button-quiet" disabled={command.busy} onClick={() => confirm('cancel')}>{incident.status === 'WAITING_REPLACEMENT' ? 'Hủy yêu cầu chờ thiết bị' : 'Hủy báo cáo sự cố'}</button>}</>}
    </article>}
    {action && <ReviewDialog title="Xác nhận xử lý sự cố" busy={command.busy} onClose={() => setAction('')} onConfirm={() => void execute()} confirmLabel="Xác nhận thao tác sự cố"><p>{actionLabel(action)}</p><p>Thiết bị gốc: {incident.serialNumber}. {action === 'replace' ? 'Cấp một thiết bị cùng loại, giữ nguyên hạn trả và lưu riêng lịch sử thiết bị gốc.' : action === 'inspection' ? `${outcomeLabels[outcome]}: ${notes}` : action === 'defective-return' ? 'Chỉ ghi nhận thiết bị đã nhận kiểm tra hoặc đã xác minh thất lạc.' : 'Kết quả sẽ được lưu trong lịch sử sự cố.'}</p><InlineError message={command.error} /></ReviewDialog>}
  </section>;
}

function actionLabel(action: string) { return ({ REPORTED: 'Đã gửi báo cáo', INSPECTION_STARTED: 'Bắt đầu kiểm tra', INSPECTION_COMPLETED: 'Đã ghi kết luận kiểm tra', OPERATION_STARTED: 'Bắt đầu xử lý thiết bị', OPERATION_REJECTED: 'Chưa thể xử lý thiết bị', DEFECTIVE_RETURN_RECORDED: 'Đã nhận trả thiết bị', WAIT_CONSENTED: 'Sinh viên đồng ý chờ', EQUIPMENT_REPLACED: 'Đã bàn giao thiết bị mới', RESOLVED: 'Đóng sự cố', CANCELLED: 'Sinh viên hủy yêu cầu', MANUAL_REVIEW: 'Cần đối soát', RECOVERY_REQUESTED: 'Yêu cầu khôi phục thao tác', 'start-inspection': 'Bắt đầu kiểm tra trực tiếp', inspection: 'Lưu kết luận kiểm tra', replace: 'Xác nhận bàn giao thiết bị thay thế', 'defective-return': 'Ghi nhận trả thiết bị', wait: 'Đồng ý chờ thiết bị cùng loại', cancel: 'Hủy yêu cầu', resolve: 'Đóng sự cố không cấp đổi', 'retry-operation': 'Đối soát lại đúng thao tác trước' } as Record<string, string>)[action]; }
function EvidencePanel({ incident, canUpload, reload }: { incident: Incident; canUpload: boolean; reload: () => void }) {
  const [files, setFiles] = useState<File[]>([]); const command = useCommand();
  return <div className="section-space"><h3>Ảnh hỗ trợ kiểm tra</h3>{incident.evidence.length ? <div className="incident-evidence">{incident.evidence.map(e => <PrivateImage key={e.id} incidentId={incident.id} evidenceId={e.id} />)}</div> : <p>Chưa có ảnh đính kèm.</p>}{canUpload && incident.evidence.length < 3 && <form className="form-stack" onSubmit={e => { e.preventDefault(); void command.run(async () => { validateImages(files); if (files.length + incident.evidence.length > 3) throw new Error('Tối đa 3 ảnh cho mỗi sự cố.'); for (const file of files) await incidentApi.upload(incident.id, file); setFiles([]); reload(); }); }}><label>Bổ sung ảnh hỗ trợ<input type="file" accept="image/png,image/jpeg,image/webp" multiple onChange={e => setFiles([...(e.target.files ?? [])])} /></label><button disabled={command.busy || !files.length}>Tải ảnh lên</button><InlineError message={command.error} /></form>}</div>;
}
function PrivateImage({ incidentId, evidenceId }: { incidentId: string; evidenceId: string }) {
  const [url, setUrl] = useState(''), [error, setError] = useState('');
  useEffect(() => { const controller = new AbortController(); let objectUrl = ''; void incidentApi.image(incidentId, evidenceId, controller.signal).then(blob => { if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } }).catch(() => { if (!controller.signal.aborted) setError('Không tải được ảnh hỗ trợ.'); }); return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); }; }, [incidentId, evidenceId]);
  return error ? <p role="alert">{error}</p> : url ? <a href={url} target="_blank" rel="noreferrer"><img src={url} alt="Ảnh thiết bị do người mượn cung cấp để hỗ trợ kiểm tra" /></a> : <p role="status">Đang tải ảnh…</p>;
}

function historyNote(note: string) {
  const labels: Record<string, string> = { OUT_OF_STOCK: 'Chưa có thiết bị cùng loại sẵn sàng.', DAMAGED: 'Đã nhận trả thiết bị lỗi và chuyển sang bảo trì.', LOST: 'Đã ghi nhận thiết bị thất lạc.', DUE_DATE_PASSED: 'Hạn trả gốc đã hết.', LOCAL_COMMIT_FAILED: 'Cần đối soát kết quả đã ghi nhận tại kho.', OPERATION_ID_REUSED: 'Mã thao tác đã dùng cho nội dung khác.' };
  return labels[note] ?? (/^[A-Z][A-Z0-9_]+$/.test(note) ? 'Thao tác cần được nhân viên kiểm tra và đối soát.' : note);
}
