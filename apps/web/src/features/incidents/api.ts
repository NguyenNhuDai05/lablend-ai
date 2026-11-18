import { apiRequest } from '../../lib/api';
import type { Paged } from '../catalog/api';

export const incidentLabels: Record<string, string> = {
  REPORTED: 'Chờ kiểm tra', INSPECTING: 'Đang kiểm tra', CONFIRMED_DEFECT: 'Đã xác nhận, chờ xử lý', WAITING_REPLACEMENT: 'Đang chờ thiết bị',
  REPLACEMENT_IN_PROGRESS: 'Đang xử lý thiết bị', RESOLVED_REPLACED: 'Đã cấp đổi', RESOLVED_NO_FAULT: 'Thiết bị hoạt động bình thường',
  RESOLVED_REPAIRED: 'Đã khắc phục tại chỗ', RESOLVED_NO_REPLACEMENT: 'Đã đóng, không cấp đổi', CANCELLED: 'Đã hủy', MANUAL_REVIEW: 'Cần nhân viên đối soát',
};
export const problemLabels: Record<string, string> = { POWER_FAILURE: 'Không lên nguồn', PHYSICAL_DAMAGE: 'Dấu hiệu hư hỏng bên ngoài', FUNCTIONAL_ERROR: 'Hoạt động không đúng', MISSING_COMPONENT: 'Thiếu linh kiện', OTHER: 'Vấn đề khác' };
export const outcomeLabels: Record<string, string> = { NO_FAULT: 'Không phát hiện lỗi', FIXED: 'Đã khắc phục tại chỗ', DEFECTIVE: 'Xác nhận thiết bị lỗi', LOST: 'Thiết bị thất lạc' };
export type Evidence = { id: string; contentType: string; size: number; createdAt: string };
export type Incident = {
  id: string; code: string; loanId: string; loanItemId: string; studentId: string; originalAssetUnitId: string; equipmentTypeId: string; serialNumber: string;
  problemCategory: string; description: string; replacementRequested: boolean; waitingConsented: boolean; status: string; createdAt: string; updatedAt: string;
  resolution: string | null; resolvedBy: string | null; resolvedAt: string | null; pendingOperationId: string | null; operationState: string | null; operationError: string | null;
  inspection: { outcome: string; notes: string; correctiveAction: string | null; inspectedBy: string; inspectedAt: string; physicallyReceivedAt: string | null } | null;
  history: { id: string; action: string; fromStatus: string | null; toStatus: string; actorUserId: string; notes: string | null; occurredAt: string }[];
  replacements: { operationId: string; replacementLoanId: string; replacementLoanItemId: string; replacementAssetUnitId: string | null; serialNumber: string | null; replacedBy: string; replacedAt: string | null }[];
  evidence: Evidence[];
};
export type IncidentStats = { total: number; awaitingInspection: number; waitingReplacement: number; successfullyReplaced: number; closedWithoutReplacement: number; averageResolutionHours: number | null; verifiedFailures: { equipmentTypeId: string; count: number }[] };
export type AssetIdentity = { loanItemId: string; assetUnitId: string; serialNumber: string; reservationStatus: string; assetStatus: string };
const path = (id: string) => `/api/incidents/${encodeURIComponent(id)}`;
export const incidentApi = {
  list: (staff: boolean, params: URLSearchParams, signal?: AbortSignal) => apiRequest<Paged<Incident>>(`/api/incidents${staff ? '' : '/my'}?${params}`, { signal }),
  detail: (id: string, signal?: AbortSignal) => apiRequest<Incident>(path(id), { signal }),
  assets: (loanId: string, signal?: AbortSignal) => apiRequest<AssetIdentity[]>(`/api/loans/${encodeURIComponent(loanId)}/asset-identities`, { signal }),
  report: (body: { loanId: string; loanItemId: string; problemCategory: string; description: string; replacementRequested: boolean }, key: string) =>
    apiRequest<Incident>('/api/incidents', { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify(body) }),
  command: (id: string, action: string, body?: unknown, key?: string) => apiRequest<Incident>(`${path(id)}/${action}`, { method: 'POST', ...(key ? { headers: { 'Idempotency-Key': key } } : {}), ...(body ? { body: JSON.stringify(body) } : {}) }, true, 30_000),
  stats: (params: URLSearchParams, signal?: AbortSignal) => apiRequest<IncidentStats>(`/api/incidents/stats?${params}`, { signal }),
  upload: (id: string, file: File) => { const form = new FormData(); form.set('file', file); return apiRequest<Evidence>(`${path(id)}/evidence`, { method: 'POST', body: form }, true, 30_000); },
  image: (id: string, evidenceId: string, signal?: AbortSignal) => apiRequest<Blob>(`${path(id)}/evidence/${encodeURIComponent(evidenceId)}`, { signal }, true, 15_000, 'blob'),
};
export function validateImages(files: File[]) {
  if (files.length > 3 || files.some(f => !['image/png', 'image/jpeg', 'image/webp'].includes(f.type) || f.size > 5 * 1024 * 1024 || f.size === 0))
    throw new Error('Chọn tối đa 3 ảnh JPEG, PNG hoặc WebP, mỗi ảnh không quá 5 MB. Nội dung được kiểm tra lại trên máy chủ.');
}
