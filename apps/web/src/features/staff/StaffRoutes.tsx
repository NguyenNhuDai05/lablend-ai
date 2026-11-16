import { useNavigate, useSearchParams } from 'react-router-dom';
import { CatalogPage } from './CatalogPage';
import { InventoryPage } from './InventoryPage';
import './staff.css';

export function StaffCatalogRoute() {
  const navigate = useNavigate();
  return <div className="staff"><CatalogPage onShowAssets={equipmentTypeId => navigate(`/staff/inventory?equipmentTypeId=${encodeURIComponent(equipmentTypeId)}`)} /></div>;
}

export function StaffInventoryRoute() {
  const [search] = useSearchParams();
  const equipmentTypeId = search.get('equipmentTypeId') ?? undefined;
  return <div className="staff"><InventoryPage key={equipmentTypeId} initialType={equipmentTypeId} /></div>;
}
