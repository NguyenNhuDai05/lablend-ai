export const apiBaseUrl = (import.meta.env?.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

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
let refreshHandler: (() => Promise<void>) | undefined;
let pendingRefresh: Promise<void> | null = null;
let sessionVersionProvider: () => number = () => 0;
const cookieCommands = new Set(['/api/auth/login', '/api/auth/register', '/api/auth/refresh', '/api/auth/logout']);

export function configureApiClient(options: {
  getAccessToken: () => string | null;
  onUnauthorized: () => void;
  refreshAccessToken?: () => Promise<void>;
  getSessionVersion?: () => number;
}) {
  accessTokenProvider = options.getAccessToken;
  unauthorizedHandler = options.onUnauthorized;
  refreshHandler = options.refreshAccessToken;
  sessionVersionProvider = options.getSessionVersion ?? (() => 0);
}

export async function apiRequest<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers);
  const token = accessTokenProvider();
  const version = sessionVersionProvider();
  if (token && !cookieCommands.has(path)) headers.set('Authorization', `Bearer ${token}`);
  if (cookieCommands.has(path) && init.method?.toUpperCase() === 'POST') headers.set('X-LabLend-CSRF', '1');
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  headers.set('Accept', 'application/json');
  headers.set('X-Correlation-ID', crypto.randomUUID());

  const controller = new AbortController();
  const timeout = globalThis.setTimeout(() => controller.abort(), 10_000);
  const abort = () => controller.abort();
  init.signal?.addEventListener('abort', abort, { once: true });

  try {
    if (init.signal?.aborted) controller.abort();
    const response = await fetch(`${apiBaseUrl}${path}`, { ...init, credentials: 'include', headers, signal: controller.signal });
    if (!cookieCommands.has(path) && version !== sessionVersionProvider()) {
      throw new ApiError('Phiên đăng nhập đã thay đổi. Vui lòng thực hiện lại yêu cầu.', 0, 'SESSION_CHANGED');
    }
    if (response.status === 401 && !cookieCommands.has(path)) {
      if (retry && refreshHandler) {
        // A late response for the previous JWT can use the token another request already renewed.
        if (!accessTokenProvider() || accessTokenProvider() === token) {
          pendingRefresh ??= Promise.resolve().then(refreshHandler).finally(() => { pendingRefresh = null; });
          try { await pendingRefresh; } catch (error) { unauthorizedHandler(); throw error; }
        }
        return await apiRequest<T>(path, init, false);
      }
      unauthorizedHandler();
    }
    if (!response.ok) throw await toApiError(response);
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (controller.signal.aborted) throw new ApiError('Yêu cầu đã hết thời gian chờ.', 0, 'REQUEST_TIMEOUT');
    throw new ApiError('Không thể kết nối dịch vụ. Vui lòng thử lại.', 0, 'NETWORK_ERROR');
  } finally {
    globalThis.clearTimeout(timeout);
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
