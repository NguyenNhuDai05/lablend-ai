import type { LoanSelection } from './api';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function parseLoanPrefill(state: unknown, queryId: string | null): { purpose: string; selections: LoanSelection[]; invalid: boolean; fromRecommendation: boolean } {
  const empty = { purpose: '', selections: [] as LoanSelection[], invalid: false, fromRecommendation: false };
  if (state && typeof state === 'object' && 'recommendationPrefill' in state) {
    const input = state.recommendationPrefill as { purpose?: unknown; selections?: unknown } | null;
    if (!input || typeof input.purpose !== 'string' || input.purpose.trim().length < 10 || input.purpose.length > 2000
      || !Array.isArray(input.selections) || input.selections.length < 1 || input.selections.length > 20) return { ...empty, invalid: true };
    const selections: LoanSelection[] = []; let total = 0;
    for (const row of input.selections) {
      if (!row || typeof row.equipmentTypeId !== 'string' || !uuid.test(row.equipmentTypeId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(row.equipmentTypeId)
        || !Number.isInteger(row.quantity) || row.quantity < 1 || row.quantity > 20 || selections.some(s => s.equipmentTypeId === row.equipmentTypeId.toLowerCase())) return { ...empty, invalid: true };
      total += row.quantity; selections.push({ equipmentTypeId: row.equipmentTypeId.toLowerCase(), quantity: row.quantity });
    }
    return total <= 20 ? { purpose: input.purpose.trim(), selections, invalid: false, fromRecommendation: true } : { ...empty, invalid: true };
  }
  if (queryId) return uuid.test(queryId) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(queryId)
    ? { ...empty, selections: [{ equipmentTypeId: queryId.toLowerCase(), quantity: 1 }] } : { ...empty, invalid: true };
  return empty;
}
