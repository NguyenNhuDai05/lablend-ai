export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

export type ProblemDetails = {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
};

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string,
    public readonly correlationId?: string,
    public readonly errors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

let accessTokenProvider: () => string | null = () => null;
let unauthorizedHandler: () => void = () => undefined;

export function configureApiClient(options: {
  getAccessToken: () => string | null;
  onUnauthorized: () => void;
}) {
  accessTokenProvider = options.getAccessToken;
  unauthorizedHandler = options.onUnauthorized;
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const token = accessTokenProvider();
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  headers.set('Accept', 'application/json');
  headers.set('X-Correlation-ID', crypto.randomUUID());

  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 10_000);
  const abort = () => controller.abort();
  init.signal?.addEventListener('abort', abort, { once: true });

  try {
    const response = await fetch(`${apiBaseUrl}${path}`, { ...init, headers, signal: controller.signal });
    if (response.status === 401) unauthorizedHandler();
    if (!response.ok) throw await toApiError(response);
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (controller.signal.aborted) throw new ApiError('Yêu cầu đã hết thời gian chờ.', 0, 'REQUEST_TIMEOUT');
    throw new ApiError('Không thể kết nối dịch vụ. Vui lòng thử lại.', 0, 'NETWORK_ERROR');
  } finally {
    window.clearTimeout(timeout);
    init.signal?.removeEventListener('abort', abort);
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};
  try { problem = await response.json() as ProblemDetails; } catch { /* upstream may have no JSON body */ }
  return new ApiError(
    (problem.errors ? Object.values(problem.errors).flat().join(' ') : '') || problem.detail || problem.title || `Dịch vụ trả HTTP ${response.status}.`,
    response.status,
    problem.code ?? 'HTTP_ERROR',
    problem.correlationId ?? response.headers.get('X-Correlation-ID') ?? undefined,
    problem.errors,
  );
}

export type HealthResponse = {
  status: string;
  checks: Record<string, string>;
  correlationId: string;
};

export async function checkGateway(): Promise<HealthResponse> {
  return apiRequest<HealthResponse>('/health/live');
}
