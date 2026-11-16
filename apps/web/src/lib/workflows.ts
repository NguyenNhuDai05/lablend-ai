import { ApiError } from './api';

export const loanStatuses = ['PENDING', 'APPROVING', 'APPROVED', 'BORROWED', 'RETURNED', 'REJECTED', 'CANCELLED', 'APPROVAL_FAILED', 'EXPIRED'] as const;
export type LoanStatus = typeof loanStatuses[number];
export const statusLabels: Record<string, string> = { PENDING: 'Chờ duyệt', APPROVING: 'Đang giữ thiết bị', APPROVED: 'Đã duyệt', BORROWED: 'Đang mượn', RETURNED: 'Đã trả', REJECTED: 'Bị từ chối', CANCELLED: 'Đã hủy', APPROVAL_FAILED: 'Duyệt không thành công', EXPIRED: 'Hết hạn nhận', REQUESTED: 'Đã yêu cầu', RESERVED: 'Đã giữ chỗ', DAMAGED: 'Hư hỏng', LOST: 'Thất lạc' };
export const formatDate = (value?: string | null) => value ? new Date(value).toLocaleString('vi-VN') : '—';
export function safeDestination(value?: string) { return value && /^\/(?!\/)/.test(value) && !value.includes('\\') && (value === '/' || /^\/(catalog|equipment|loans|staff|admin|profile|notifications)([/?#]|$)/.test(value)) ? value : '/'; }
export function query(params: Record<string, string | number | undefined>) { return new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)])).toString(); }
export function validateLoan(purpose: string, due: string, quantities: number[], now = Date.now()) {
  if (purpose.trim().length < 10 || purpose.trim().length > 2000) throw new Error('Mục đích cần từ 10 đến 2.000 ký tự.');
  const at = new Date(due).getTime();
  if (!Number.isFinite(at) || at <= now || at > now + 30 * 86400_000) throw new Error('Hạn trả phải trong tương lai và không quá 30 ngày.');
  if (!quantities.length || quantities.some(q => !Number.isInteger(q) || q < 1) || quantities.reduce((a, b) => a + b, 0) > 20) throw new Error('Chọn tổng cộng từ 1 đến 20 thiết bị, số lượng là số nguyên dương.');
  return new Date(at).toISOString();
}
export function errorText(error: unknown) {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : 'Không thể hoàn tất yêu cầu.';
  const hints: Record<number, string> = { 401: 'Phiên đăng nhập đã hết hạn.', 403: 'Bạn không có quyền thực hiện thao tác này.', 404: 'Không tìm thấy dữ liệu hoặc dữ liệu không thuộc tài khoản này.', 409: 'Dữ liệu đã thay đổi hoặc thao tác không còn hợp lệ. Hãy tải lại để kiểm tra.', 503: 'Dịch vụ tạm thời không sẵn sàng.' };
  return `${hints[error.status] ?? ''} ${error.message}${error.correlationId ? ` (Mã tra cứu: ${error.correlationId})` : ''}`.trim();
}

// A command is never automatically repeated after a network failure. A second click joins the same flight.
export function singleFlight() {
  let pending: Promise<unknown> | undefined;
  return <T>(work: () => Promise<T>): Promise<T> => {
    if (pending) return pending as Promise<T>;
    const run = Promise.resolve().then(work);
    pending = run;
    void run.finally(() => { if (pending === run) pending = undefined; }).catch(() => undefined);
    return run;
  };
}
