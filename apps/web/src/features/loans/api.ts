import { apiRequest } from '../../lib/api';
import { query, validateLoan, type LoanStatus } from '../../lib/workflows';
import type { Paged } from '../catalog/api';
export type LoanItem = { id: string; equipmentTypeId: string; assetUnitId: string | null; reservationId: string | null; itemStatus: string; returnCondition: string | null; returnedAt: string | null };
export type Loan = { id: string; studentId: string; purpose: string; status: LoanStatus; isOverdue: boolean; requestedAt: string; dueAt: string; reviewedBy: string | null; reviewedAt: string | null; rejectionReason: string | null; handedOverBy: string | null; handedOverAt: string | null; items: LoanItem[]; history: { id: string; actorUserId: string; action: string; fromStatus: string | null; toStatus: string; occurredAt: string; note: string | null }[] };
export type LoanSelection = { equipmentTypeId: string; quantity: number };
export type ReturnCondition = 'NORMAL' | 'DAMAGED' | 'LOST';
export const loanApi = {
  list: (staff: boolean, status: string, page: number, signal?: AbortSignal) => apiRequest<Paged<Loan>>(`/api/loans${staff ? '' : '/my'}?${query({ status, page, pageSize: 10 })}`, { signal }),
  staffSummary: async (signal?: AbortSignal) => {
    const page = (status: string, pageSize: number) => apiRequest<Paged<Loan>>(`/api/loans?${query({ status, pageSize })}`, { signal });
    // ponytail: overdue is counted within the first 100 borrowed loans (API max page, no overdue filter); add a server-side filter if more are out at once.
    const [pending, approved, borrowed] = await Promise.all([page('PENDING', 1), page('APPROVED', 1), page('BORROWED', 100)]);
    return { pending: pending.total, approved: approved.total, borrowed: borrowed.total, overdue: borrowed.items.filter(l => l.isOverdue).length };
  },
  detail: (id: string, signal?: AbortSignal) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}`, { signal }),
  create: (purpose: string, dueAt: string, selections: LoanSelection[], now = Date.now(), idempotencyKey?: string) => {
    const due = validateLoan(purpose, dueAt, selections.map(s => s.quantity), now);
    if (selections.some(s => !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s.equipmentTypeId))) throw new Error('Chọn loại thiết bị hợp lệ.');
    return apiRequest<Loan>('/api/loans', { method: 'POST', ...(idempotencyKey ? { headers: { 'Idempotency-Key': idempotencyKey } } : {}), body: JSON.stringify({ purpose: purpose.trim(), dueAt: due, items: selections.flatMap(s => Array.from({ length: s.quantity }, () => ({ equipmentTypeId: s.equipmentTypeId }))) }) });
  },
  cancel: (id: string) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
  approve: (id: string) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}/approve`, { method: 'POST' }),
  handover: (id: string) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}/handover`, { method: 'POST' }),
  reject: (id: string, rejectionReason: string) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}/reject`, { method: 'POST', body: JSON.stringify({ rejectionReason: rejectionReason.trim() }) }),
  returns: (id: string, items: { loanItemId: string; condition: ReturnCondition }[]) => apiRequest<Loan>(`/api/loans/${encodeURIComponent(id)}/returns`, { method: 'POST', body: JSON.stringify({ items }) }),
};
