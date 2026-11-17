import { useNavigate, useSearchParams } from 'react-router-dom';
import { CatalogPage } from './CatalogPage';
import { InventoryPage } from './InventoryPage';
import './staff.css';

export function StaffCatalogRoute() {
  const navigate = useNavigate();
  return <div className="staff"><PageTitle title="Quản lý danh mục" note="Danh mục và loại thiết bị hiển thị trên trang Thiết bị." /><CatalogPage onShowAssets={equipmentTypeId => navigate(`/staff/inventory?equipmentTypeId=${encodeURIComponent(equipmentTypeId)}`)} /></div>;
}

export function StaffInventoryRoute() {
  const [search] = useSearchParams();
  const equipmentTypeId = search.get('equipmentTypeId') ?? undefined;
  return <div className="staff"><PageTitle title="Quản lý kho" note="Từng thiết bị vật lý, trạng thái, bảo trì và lịch sử." /><InventoryPage key={equipmentTypeId} initialType={equipmentTypeId} /></div>;
}

function PageTitle({ title, note }: { title: string; note: string }) {
  return <div className="section-title"><div><p className="eyebrow">Quản lý phòng lab</p><h1>{title}</h1></div><p>{note}</p></div>;
}
