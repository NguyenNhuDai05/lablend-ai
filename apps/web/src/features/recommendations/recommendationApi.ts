import { ApiError, apiRequest } from '../../lib/api';
import type { Paged } from '../catalog/api';

export type RecommendationItem = {
  equipmentTypeId: string; equipmentName: string; categoryName: string; description: string | null;
  specifications: string | null; imageUrl: string | null; isActive: boolean; rank: number;
  reason: string; usageNotes: string; available: number; total: number;
};
export type Recommendation = {
  id: string; purpose: string; constraints: string[]; categoryId: string | null; createdAt: string;
  modelUsed: string; availabilityCheckedAt: string; recommendations: RecommendationItem[];
};
export type RecommendationHistory = Pick<Recommendation, 'id' | 'purpose' | 'createdAt' | 'modelUsed'> & { itemCount: number };
export function validateRecommendation(purpose: string, constraints: string[]) {
  if (purpose.trim().length < 10 || purpose.trim().length > 2000) throw new Error('Mô tả cần từ 10 đến 2.000 ký tự.');
  if (constraints.length > 8 || constraints.some(c => !c.trim() || c.length > 300)) throw new Error('Tối đa 8 yêu cầu bổ sung, mỗi dòng không quá 300 ký tự.');
}
export const recommendationApi = {
  create: (purpose: string, constraints: string[], categoryId?: string, signal?: AbortSignal) => {
    validateRecommendation(purpose, constraints);
    return apiRequest<Recommendation>('/api/recommendations', { method: 'POST', signal,
      body: JSON.stringify({ purpose: purpose.trim(), constraints, ...(categoryId ? { categoryId } : {}) }) }, true, 120_000);
  },
  history: (page: number, signal?: AbortSignal) => apiRequest<Paged<RecommendationHistory>>(`/api/recommendations/history?page=${page}&pageSize=10`, { signal }),
  detail: (id: string, signal?: AbortSignal) => apiRequest<Recommendation>(`/api/recommendations/${encodeURIComponent(id)}`, { signal }, true, 45_000),
};
export function recommendationError(error: unknown) {
  const messages: Record<string, string> = {
    AI_UNAVAILABLE: 'AI tư vấn hiện tạm thời không khả dụng. Bạn vẫn có thể tìm và mượn thiết bị qua danh mục.',
    AI_INVALID_RESPONSE: 'Gemini trả kết quả không hợp lệ. Vui lòng thử lại.',
    RATE_LIMITED: 'Bạn đã đạt giới hạn tư vấn. Vui lòng chờ trước khi thử lại; nếu hết lượt ngày, hãy thử vào ngày mai (UTC).',
    CATALOG_UNAVAILABLE: 'Chưa thể đọc danh mục thiết bị. Vui lòng thử lại.',
    INVENTORY_UNAVAILABLE: 'Chưa thể kiểm tra tồn kho. Vui lòng thử lại.',
  };
  if (error instanceof ApiError) return `${messages[error.code] ?? (error.status === 502 || error.status === 503 ? messages.AI_UNAVAILABLE : error.message)}${error.correlationId ? ` (Mã hỗ trợ: ${error.correlationId})` : ''}`;
  return error instanceof Error ? error.message : 'Không thể hoàn tất yêu cầu. Vui lòng thử lại.';
}
