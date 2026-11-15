import { useState, type FormEvent } from 'react';
import { api, query, useLoad, type Availability, type Category, type EquipmentType, type Paged } from './api';
import { Badge, Field, FormModal, LoadState, Pager, optional, text, useAction } from './ui';

type TypeQuery = { keyword: string; categoryId: string; sortBy: string; direction: string; includeInactive: boolean; page: number };
const pageSize = 10;

type Categories = ReturnType<typeof useLoad<Category[]>>;

export function CatalogPage({ onShowAssets }: { onShowAssets: (equipmentTypeId: string) => void }) {
  const categories = useLoad(() => api<Category[]>('/api/categories?includeInactive=true'), 'categories');
  const [changes, setChanges] = useState(0); // category edits change categoryName shown in the type list
  return <>
    <CategoriesSection categories={categories} onChanged={() => setChanges(n => n + 1)} />
    <EquipmentTypesSection categories={categories.data ?? []} refreshKey={changes} onShowAssets={onShowAssets} />
  </>;
}

function CategoriesSection({ categories, onChanged }: { categories: Categories; onChanged: () => void }) {
  const { act, notice, done } = useAction(() => { categories.reload(); onChanged(); });
  const [editing, setEditing] = useState<Category | 'new'>();
  const current = editing === 'new' ? undefined : editing;

  async function save(form: FormData) {
    const body = { name: text(form, 'name'), description: optional(form, 'description') };
    if (current) await api(`/api/categories/${current.id}`, 'PUT', { ...body, isActive: form.has('isActive') });
    else await api('/api/categories', 'POST', body);
    done(current ? 'Đã cập nhật danh mục.' : 'Đã thêm danh mục.');
  }
  const setActive = (c: Category, isActive: boolean) => act(
    () => isActive ? api(`/api/categories/${c.id}`, 'PUT', { name: c.name, description: c.description, isActive }) : api(`/api/categories/${c.id}`, 'DELETE'),
    isActive ? `Đã kích hoạt “${c.name}”.` : `Đã ngừng “${c.name}”.`);

  return <section aria-labelledby="categories-title">
    <div className="section-head">
      <h2 id="categories-title">Danh mục</h2>
      <button type="button" onClick={() => setEditing('new')}>+ Thêm danh mục</button>
    </div>
    {notice}
    <LoadState loading={categories.loading} error={categories.error} empty={categories.data?.length === 0} />
    {!!categories.data?.length && <div className="table-wrap"><table>
      <thead><tr><th>Tên</th><th>Mô tả</th><th>Trạng thái</th><th><span className="sr-only">Thao tác</span></th></tr></thead>
      <tbody>{categories.data.map(c => <tr key={c.id}>
        <td>{c.name}</td>
        <td className="muted">{c.description}</td>
        <td><Badge tone={c.isActive ? 'ok' : 'off'}>{c.isActive ? 'Đang dùng' : 'Đã ngừng'}</Badge></td>
        <td className="row-actions">
          <button type="button" className="ghost" onClick={() => setEditing(c)}>Sửa</button>
          {c.isActive
            ? <button type="button" className="ghost danger" onClick={() => confirm(`Ngừng danh mục “${c.name}”?`) && void setActive(c, false)}>Ngừng</button>
            : <button type="button" className="ghost" onClick={() => void setActive(c, true)}>Kích hoạt</button>}
        </td>
      </tr>)}</tbody>
    </table></div>}
    {editing && <FormModal title={current ? 'Sửa danh mục' : 'Thêm danh mục'} submitLabel="Lưu" onSubmit={save} onClose={() => setEditing(undefined)}>
      <Field label="Tên"><input name="name" required minLength={2} maxLength={150} defaultValue={current?.name} autoFocus /></Field>
      <Field label="Mô tả"><textarea name="description" maxLength={1000} rows={3} defaultValue={current?.description ?? ''} /></Field>
      {current && <label className="check"><input type="checkbox" name="isActive" defaultChecked={current.isActive} /> Đang dùng</label>}
    </FormModal>}
  </section>;
}

function EquipmentTypesSection({ categories, refreshKey, onShowAssets }: {
  categories: Category[]; refreshKey: number; onShowAssets: (equipmentTypeId: string) => void;
}) {
  const [q, setQ] = useState<TypeQuery>({ keyword: '', categoryId: '', sortBy: 'name', direction: 'asc', includeInactive: true, page: 1 });
  const types = useLoad(() => api<Paged<EquipmentType>>(`/api/equipment-types?${query({ ...q, pageSize })}`), `${JSON.stringify(q)}#${refreshKey}`);
  const ids = types.data?.items.map(t => t.id) ?? [];
  const stock = useLoad(() => ids.length
    ? api<Availability[]>(`/api/inventory/availability?${ids.map(id => `equipmentTypeIds=${id}`).join('&')}`)
    : Promise.resolve([]), ids.join());
  const stockById = new Map(stock.data?.map(s => [s.equipmentTypeId, s]));
  const { act, notice, done } = useAction(types.reload);
  const [editing, setEditing] = useState<EquipmentType | 'new'>();
  const current = editing === 'new' ? undefined : editing;

  function applyFilters(form: HTMLFormElement) {
    const f = new FormData(form);
    setQ({ keyword: text(f, 'keyword'), categoryId: text(f, 'categoryId'), sortBy: text(f, 'sortBy'),
      direction: text(f, 'direction'), includeInactive: f.has('includeInactive'), page: 1 });
  }
  const body = (t: EquipmentType) => ({ categoryId: t.categoryId, name: t.name, description: t.description,
    specifications: t.specifications, tags: t.tags, imageUrl: t.imageUrl });
  async function save(form: FormData) {
    const fields = {
      categoryId: text(form, 'categoryId'), name: text(form, 'name'), description: optional(form, 'description'),
      specifications: optional(form, 'specifications'), imageUrl: optional(form, 'imageUrl'),
      tags: text(form, 'tags').split(',').map(t => t.trim()).filter(Boolean)
    };
    if (current) await api(`/api/equipment-types/${current.id}`, 'PUT', { ...fields, isActive: form.has('isActive') });
    else await api('/api/equipment-types', 'POST', fields);
    done(current ? 'Đã cập nhật loại thiết bị.' : 'Đã thêm loại thiết bị.');
  }
  const setActive = (t: EquipmentType, isActive: boolean) => act(
    () => isActive ? api(`/api/equipment-types/${t.id}`, 'PUT', { ...body(t), isActive }) : api(`/api/equipment-types/${t.id}`, 'DELETE'),
    isActive ? `Đã kích hoạt “${t.name}”.` : `Đã ngừng “${t.name}”.`);

  const selectable = categories.filter(c => c.isActive || c.id === current?.categoryId);
  return <section aria-labelledby="types-title">
    <div className="section-head">
      <h2 id="types-title">Loại thiết bị</h2>
      <button type="button" onClick={() => setEditing('new')}>+ Thêm loại thiết bị</button>
    </div>
    <form className="filters" role="search" onSubmit={(e: FormEvent<HTMLFormElement>) => { e.preventDefault(); applyFilters(e.currentTarget); }}
      onChange={e => { if (!(e.target instanceof HTMLInputElement && e.target.type === 'search' && e.target.value)) applyFilters(e.currentTarget); }}>
      <Field label="Từ khóa"><input type="search" name="keyword" maxLength={100} placeholder="Tên hoặc mô tả" /></Field>
      <Field label="Danh mục"><select name="categoryId" defaultValue="">
        <option value="">Tất cả</option>
        {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select></Field>
      <Field label="Sắp xếp"><select name="sortBy" defaultValue="name">
        <option value="name">Tên</option><option value="createdAt">Ngày tạo</option>
      </select></Field>
      <Field label="Thứ tự"><select name="direction" defaultValue="asc">
        <option value="asc">Tăng dần</option><option value="desc">Giảm dần</option>
      </select></Field>
      <label className="check"><input type="checkbox" name="includeInactive" defaultChecked /> Gồm loại đã ngừng</label>
      <button className="ghost">Tìm</button>
    </form>
    {notice}
    <LoadState loading={types.loading} error={types.error} empty={types.data?.items.length === 0} />
    {!!types.data?.items.length && <div className="table-wrap"><table>
      <thead><tr><th>Tên</th><th>Danh mục</th><th>Tags</th><th>Sẵn sàng / tổng</th><th>Trạng thái</th><th><span className="sr-only">Thao tác</span></th></tr></thead>
      <tbody>{types.data.items.map(t => {
        const s = stockById.get(t.id);
        return <tr key={t.id}>
          <td><strong>{t.name}</strong>{t.specifications && <div className="muted small">{t.specifications}</div>}</td>
          <td>{t.categoryName}</td>
          <td>{t.tags.map(tag => <Badge key={tag} tone="tag">{tag}</Badge>)}</td>
          <td>{s ? `${s.available} / ${s.total}` : '—'}</td>
          <td><Badge tone={t.isActive ? 'ok' : 'off'}>{t.isActive ? 'Đang dùng' : 'Đã ngừng'}</Badge></td>
          <td className="row-actions">
            <button type="button" className="ghost" onClick={() => onShowAssets(t.id)}>Thiết bị</button>
            <button type="button" className="ghost" onClick={() => setEditing(t)}>Sửa</button>
            {t.isActive
              ? <button type="button" className="ghost danger" onClick={() => confirm(`Ngừng loại “${t.name}”?`) && void setActive(t, false)}>Ngừng</button>
              : <button type="button" className="ghost" onClick={() => void setActive(t, true)}>Kích hoạt</button>}
          </td>
        </tr>;
      })}</tbody>
    </table></div>}
    {types.data && <Pager page={q.page} pageSize={pageSize} total={types.data.total} onPage={page => setQ({ ...q, page })} />}
    {editing && <FormModal title={current ? 'Sửa loại thiết bị' : 'Thêm loại thiết bị'} submitLabel="Lưu" onSubmit={save} onClose={() => setEditing(undefined)}>
      <Field label="Danh mục"><select name="categoryId" required defaultValue={current?.categoryId ?? ''}>
        <option value="" disabled>Chọn danh mục</option>
        {selectable.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select></Field>
      <Field label="Tên"><input name="name" required minLength={2} maxLength={150} defaultValue={current?.name} autoFocus /></Field>
      <Field label="Mô tả"><textarea name="description" maxLength={2000} rows={2} defaultValue={current?.description ?? ''} /></Field>
      <Field label="Thông số kỹ thuật"><textarea name="specifications" maxLength={4000} rows={2} defaultValue={current?.specifications ?? ''} /></Field>
      <Field label="Tags (phân cách bằng dấu phẩy, tối đa 20)"><input name="tags" defaultValue={current?.tags.join(', ')} /></Field>
      <Field label="URL hình ảnh"><input type="url" name="imageUrl" maxLength={500} defaultValue={current?.imageUrl ?? ''} /></Field>
      {current && <label className="check"><input type="checkbox" name="isActive" defaultChecked={current.isActive} /> Đang dùng</label>}
    </FormModal>}
  </section>;
}
