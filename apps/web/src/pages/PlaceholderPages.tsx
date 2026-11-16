import { EmptyState, UnavailableState } from '../components/States';
export const LoansPage = () => <EmptyState message="Dịch vụ mượn thiết bị đang chờ M1 hoàn thiện hợp đồng API." />;
export const StaffCatalogSlot = () => <EmptyState title="Điểm tích hợp danh mục" message="Route đã sẵn sàng để gắn CatalogPage do M2 sở hữu." />;
export const StaffInventorySlot = () => <EmptyState title="Điểm tích hợp kho" message="Route đã sẵn sàng để gắn InventoryPage do M2 sở hữu." />;
export const NotFoundPage = () => <UnavailableState status="404" title="Không tìm thấy trang" message="Đường dẫn này không tồn tại hoặc đã được di chuyển." />;
