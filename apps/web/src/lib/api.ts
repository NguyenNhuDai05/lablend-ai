export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

export type HealthResponse = {
  status: string;
  checks: Record<string, string>;
  correlationId: string;
};

export async function checkGateway(): Promise<HealthResponse> {
  const response = await fetch(`${apiBaseUrl}/health/live`, { signal: AbortSignal.timeout(5000) });
  if (!response.ok) throw new Error(`Gateway trả HTTP ${response.status}.`);
  return response.json() as Promise<HealthResponse>;
}
