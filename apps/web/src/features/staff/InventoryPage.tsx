import { useState, type FormEvent } from 'react';
import { api, assetStatuses, query, useLoad, type Asset, type AssetStatus, type Availability, type EquipmentType, type Maintenance, type Paged, type StockMovement } from './api';
import { Badge, Field, FormModal, LoadState, Modal, Pager, formatDate, optional, statusLabels, text, useAction } from './ui';

type AssetQuery = { equipmentTypeId: string; status: string; keyword: string; sortBy: string; direction: string; page: number };
type Dialog = { kind: 'edit'; asset?: Asset } | { kind: 'maintenance' | 'complete' | 'history'; asset: Asset };
const pageSize = 20;
const tones: Record<string, string> = { AVAILABLE: 'ok', RESERVED: 'warn', ON_LOAN: 'info', MAINTENANCE: 'warn', LOST: 'bad', RETIRED: 'off' };
const allocated = (a: Asset) => a.status === 'RESERVED' || a.status === 'ON_LOAN';

export function InventoryPage({ initialType }: { initialType?: string }) {
  const [q, setQ] = useState<AssetQuery>({ equipmentTypeId: initialType ?? '', status: '', keyword: '', sortBy: 'serialNumber', direction: 'asc', page: 1 });
  // ponytail: first 100 types (API max page size); switch to a searchable picker if the catalog grows past that.
  const types = useLoad(() => api<Paged<EquipmentType>>('/api/equipment-types?includeInactive=true&sortBy=name&pageSize=100'), 'types');
  const typeName = new Map(types.data?.items.map(t => [t.id, t.name]));
  const list = useLoad(async () => {
    const [page, stock] = await Promise.all([
      api<Paged<Asset>>(`/api/inventory/assets?${query({ ...q, pageSize })}`),
      q.equipmentTypeId ? api<Availability[]>(`/api/inventory/availability?equipmentTypeIds=${q.equipmentTypeId}`) : Promise.resolve([])
    ]);
    return { page, stock: stock[0] };
  }, JSON.stringify(q));
  const assets = list.data?.page;
  const { act, notice, done } = useAction(list.reload);
  const [dialog, setDialog] = useState<Dialog>();
  const close = () => setDialog(undefined);

  function applyFilters(form: HTMLFormElement) {
    const f = new FormData(form);
    setQ({ equipmentTypeId: text(f, 'equipmentTypeId'), status: text(f, 'status'), keyword: text(f, 'keyword'),
      sortBy: text(f, 'sortBy'), direction: text(f, 'direction'), page: 1 });
  }
  async function saveAsset(form: FormData, asset?: Asset) {
    const body = { equipmentTypeId: text(form, 'equipmentTypeId'), serialNumber: text(form, 'serialNumber'),
      location: text(form, 'location'), notes: optional(form, 'notes') };
    if (asset) await api(`/api/inventory/assets/${asset.id}`, 'PUT', body);
    else await api('/api/inventory/assets', 'POST', body);
    done(asset ? `Đã cập nhật ${body.serialNumber.toUpperCase()}.` : `Đã thêm ${body.serialNumber.toUpperCase()}.`);
  }
  const retire = (a: Asset) => confirm(`Ngừng sử dụng ${a.serialNumber}? Thiết bị sẽ không còn được cho mượn.`) &&
    void act(() => api(`/api/inventory/assets/${a.id}`, 'DELETE'), `Đã ngừng sử dụng ${a.serialNumber}.`);

  return <section aria-labelledby="assets-title">
    <div className="section-head">
      <h2 id="assets-title">Kho thiết bị</h2>
      <button type="button" onClick={() => setDialog({ kind: 'edit' })}>+ Thêm thiết bị</button>
    </div>
    <form className="filters" role="search" onSubmit={(e: FormEvent<HTMLFormElement>) => { e.preventDefault(); applyFilters(e.currentTarget); }}
      onChange={e => { if (!(e.target instanceof HTMLInputElement && e.target.type === 'search' && e.target.value)) applyFilters(e.currentTarget); }}>
      {/* remount once options exist, otherwise the preselected type (from the catalog tab) is not shown */}
      <Field label="Loại thiết bị"><select name="equipmentTypeId" defaultValue={q.equipmentTypeId} key={types.data ? 'ready' : 'loading'}>
        <option value="">Tất cả</option>
        {types.data?.items.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
      </select></Field>
      <Field label="Trạng thái"><select name="status" defaultValue="">
        <option value="">Tất cả</option>
        {assetStatuses.map(s => <option key={s} value={s}>{statusLabels[s]}</option>)}
      </select></Field>
      <Field label="Từ khóa"><input type="search" name="keyword" maxLength={100} placeholder="Serial hoặc vị trí" /></Field>
      <Field label="Sắp xếp"><select name="sortBy" defaultValue="serialNumber">
        <option value="serialNumber">Serial</option><option value="status">Trạng thái</option><option value="createdAt">Ngày tạo</option>
      </select></Field>
      <Field label="Thứ tự"><select name="direction" defaultValue="asc">
        <option value="asc">Tăng dần</option><option value="desc">Giảm dần</option>
      </select></Field>
      <button className="ghost">Tìm</button>
    </form>
    {list.data?.stock && <p className="stock">
      <strong>{typeName.get(q.equipmentTypeId) ?? 'Loại đã chọn'}:</strong> {list.data.stock.available} sẵn sàng / {list.data.stock.total} thiết bị đang quản lý
    </p>}
    {notice}
    <LoadState loading={list.loading} error={list.error} empty={assets?.items.length === 0} />
    {!!assets?.items.length && <div className="table-wrap"><table>
      <thead><tr><th>Serial</th><th>Loại thiết bị</th><th>Vị trí</th><th>Trạng thái</th><th>Ghi chú</th><th><span className="sr-only">Thao tác</span></th></tr></thead>
      <tbody>{assets.items.map(a => <tr key={a.id}>
        <td className="nowrap"><strong>{a.serialNumber}</strong></td>
        <td>{typeName.get(a.equipmentTypeId) ?? <span className="muted small">{a.equipmentTypeId}</span>}</td>
        <td>{a.location}</td>
        <td><Badge tone={tones[a.status]}>{statusLabels[a.status]}</Badge></td>
        <td className="muted">{a.notes}</td>
        <td className="row-actions">
          {a.status !== 'RETIRED' && <button type="button" className="ghost" onClick={() => setDialog({ kind: 'edit', asset: a })}>Sửa</button>}
          {a.status === 'AVAILABLE' && <button type="button" className="ghost" onClick={() => setDialog({ kind: 'maintenance', asset: a })}>Bảo trì</button>}
          {a.status === 'MAINTENANCE' && <button type="button" className="ghost" onClick={() => setDialog({ kind: 'complete', asset: a })}>Hoàn tất bảo trì</button>}
          <button type="button" className="ghost" onClick={() => setDialog({ kind: 'history', asset: a })}>Lịch sử</button>
          {a.status !== 'RETIRED' && !allocated(a) && <button type="button" className="ghost danger" onClick={() => retire(a)}>Ngừng dùng</button>}
        </td>
      </tr>)}</tbody>
    </table></div>}
    {assets && <Pager page={q.page} pageSize={pageSize} total={assets.total} onPage={page => setQ({ ...q, page })} />}

    {dialog?.kind === 'edit' && <FormModal title={dialog.asset ? `Sửa ${dialog.asset.serialNumber}` : 'Thêm thiết bị'} submitLabel="Lưu"
      onSubmit={f => saveAsset(f, dialog.asset)} onClose={close}>
      <Field label="Loại thiết bị">
        <select name="equipmentTypeId" required defaultValue={dialog.asset?.equipmentTypeId ?? q.equipmentTypeId}
          disabled={dialog.asset && allocated(dialog.asset)}>
          <option value="" disabled>Chọn loại thiết bị</option>
          {types.data?.items.filter(t => t.isActive || t.id === dialog.asset?.equipmentTypeId)
            .map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
        </select>
      </Field>
      {/* a disabled select is not submitted; keep the value for units that cannot change type while reserved/on loan */}
      {dialog.asset && allocated(dialog.asset) && <input type="hidden" name="equipmentTypeId" value={dialog.asset.equipmentTypeId} />}
      <Field label="Serial"><input name="serialNumber" required minLength={2} maxLength={64} defaultValue={dialog.asset?.serialNumber} autoFocus /></Field>
      <Field label="Vị trí"><input name="location" required maxLength={150} defaultValue={dialog.asset?.location} placeholder="VD: Phòng E101 - Tủ A" /></Field>
      <Field label="Ghi chú"><textarea name="notes" maxLength={1000} rows={2} defaultValue={dialog.asset?.notes ?? ''} /></Field>
    </FormModal>}
    {dialog?.kind === 'maintenance' && <FormModal title={`Đưa ${dialog.asset.serialNumber} vào bảo trì`} submitLabel="Bắt đầu bảo trì" onClose={close}
      onSubmit={async f => {
        await api(`/api/inventory/assets/${dialog.asset.id}/maintenance`, 'POST', { reason: text(f, 'reason') });
        done(`${dialog.asset.serialNumber} đang bảo trì.`);
      }}>
      <Field label="Lý do"><textarea name="reason" required maxLength={1000} rows={3} autoFocus /></Field>
    </FormModal>}
    {dialog?.kind === 'complete' && <FormModal title={`Hoàn tất bảo trì ${dialog.asset.serialNumber}`} submitLabel="Hoàn tất" onClose={close}
      onSubmit={async f => {
        await api(`/api/inventory/assets/${dialog.asset.id}/maintenance/complete`, 'POST', { notes: optional(f, 'notes') });
        done(`${dialog.asset.serialNumber} đã sẵn sàng cho mượn.`);
      }}>
      <Field label="Kết quả bảo trì"><textarea name="notes" maxLength={1000} rows={3} autoFocus /></Field>
    </FormModal>}
    {dialog?.kind === 'history' && <HistoryModal asset={dialog.asset} onClose={close} />}
  </section>;
}

// ponytail: no user/loan names until Identity and Borrowing expose them; show the ID prefix.
const shortId = (id: string) => id.slice(0, 8);
const status = (s: AssetStatus) => <Badge tone={tones[s]}>{statusLabels[s]}</Badge>;

function HistoryModal({ asset, onClose }: { asset: Asset; onClose: () => void }) {
  const history = useLoad(() => Promise.all([
    api<StockMovement[]>(`/api/inventory/assets/${asset.id}/movements`),
    api<Maintenance[]>(`/api/inventory/assets/${asset.id}/maintenance`)
  ]), asset.id);
  const [movements, maintenance] = history.data ?? [];
  return <Modal title={`Lịch sử ${asset.serialNumber}`} onClose={onClose}>
    <LoadState loading={history.loading} error={history.error} empty={false} />
    {movements && <>
      <h3>Trạng thái</h3>
      <ol className="timeline">{movements.map(m => <li key={m.id}>
        <strong>{formatDate(m.occurredAt)}</strong> · {m.fromStatus ? <>{status(m.fromStatus)}→ </> : 'Khởi tạo: '}{status(m.toStatus)}
        {(m.loanId || m.changedBy) && <div className="muted small">
          {m.loanId ? `Phiếu mượn ${shortId(m.loanId)}` : `Nhân viên ${shortId(m.changedBy!)}`}
        </div>}
      </li>)}</ol>
    </>}
    {maintenance && <>
      <h3>Bảo trì</h3>
      {maintenance.length === 0 && <p className="muted">Chưa có lần bảo trì nào.</p>}
      {maintenance.length > 0 && <ol className="timeline">{maintenance.map(m => <li key={m.id}>
        <strong>{formatDate(m.openedAt)}</strong> · {m.reason}
        <div className="muted small">{m.completedAt
          ? `Hoàn tất ${formatDate(m.completedAt)}${m.completionNotes ? ` · ${m.completionNotes}` : ''}`
          : 'Đang bảo trì'}</div>
      </li>)}</ol>}
    </>}
    <div className="actions"><button type="button" onClick={onClose}>Đóng</button></div>
  </Modal>;
}
