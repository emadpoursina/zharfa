/**
 * API client for Zharfa backend.
 * Uses native fetch with same-origin credentials (cookie-based session).
 * No tokens, no client storage, no secrets.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly isUnauthenticated: boolean;
  readonly data?: unknown;

  constructor(status: number, message: string, data?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.isUnauthenticated = status === 401;
    this.data = data;
  }
}

export function isUnauthenticated(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthenticated;
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (res.ok) {
    if (res.status === 204) {
      return {} as T;
    }
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      return (await res.json()) as T;
    }
    return (await res.text()) as unknown as T;
  }

  let errorMessage =
    res.status === 401
      ? 'احراز هویت انجام نشده است یا نشست منقضی شده است.'
      : 'خطایی در پردازش درخواست رخ داد.';
  let responseData: unknown;

  try {
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      responseData = await res.json();
      if (typeof responseData === 'object' && responseData !== null) {
        const record = responseData as Record<string, unknown>;
        if (typeof record.message === 'string' && record.message.trim().length > 0) {
          errorMessage = record.message;
        } else if (typeof record.error === 'string' && record.error.trim().length > 0) {
          errorMessage = record.error;
        }
      }
    } else {
      const text = await res.text();
      if (text && text.trim().length > 0) {
        errorMessage = text;
      }
    }
  } catch {
    // If body parsing fails, preserve the default Persian error message
  }

  throw new ApiError(res.status, errorMessage, responseData);
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
    },
  });
  return handleResponse<T>(res);
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  return handleResponse<T>(res);
}

// ---------------------------------------------------------------------------
// Real purchase data (spec 003, contracts/api.md). Amounts are strings with up
// to 4 decimals in Rial (exact, never float). An unavailable figure is null and
// listed in `unavailable` with a reason code.
// ---------------------------------------------------------------------------

export type DataMode = 'production' | 'demo';
export type DataLabel = 'real' | 'demo';

export interface LastSuccessfulSync {
  /** ISO 8601 with the sync time zone offset. */
  at: string;
  /** Jalali date and time with Persian digits. */
  atJalali: string;
}

export interface SystemModules {
  overview: boolean;
  dataSync: boolean;
  demoPages: boolean;
  ai: boolean;
}

export interface SystemStatus {
  dataMode: DataMode;
  dataLabel: DataLabel;
  lastSuccessfulSync: LastSuccessfulSync | null;
  modules: SystemModules;
  /** Jalali date of the server today in the sync time zone, e.g. 1405-08-25. */
  serverJalaliToday: string;
}

export interface PurchasingTotals {
  net: string | null;
  gross: string | null;
  returns: string | null;
  vat: string | null;
  vatInvoice: string | null;
  vatReturn: string | null;
}

export interface PurchasingComparison {
  previousMonth: string;
  previousNet: string;
  delta: string;
  /** null when the previous month's net is "0.0000". */
  percent: string | null;
}

export interface PurchasingTrendPoint {
  month: string;
  net: string | null;
  available: boolean;
}

export interface PurchasingRankedItem {
  key: string | null;
  name: string;
  net: string;
  unknown: boolean;
}

export interface PurchasingUnavailable {
  figure: string;
  reason: string;
}

export interface PurchasingOverview {
  month: string;
  monthLabel: string;
  dataLabel: DataLabel;
  lastSuccessfulSync: LastSuccessfulSync | null;
  currency: string;
  currencyLabel: string;
  discountBasis: string;
  totals: PurchasingTotals;
  comparison: PurchasingComparison | null;
  trend: PurchasingTrendPoint[];
  activeVendors: number | null;
  topVendors: PurchasingRankedItem[] | null;
  topProducts: PurchasingRankedItem[] | null;
  unavailable: PurchasingUnavailable[];
}

export type SyncTrigger = 'manual' | 'scheduled' | 'startup-recovery';
export type SyncMode = 'incremental' | 'full';
export type SyncStatus = 'running' | 'succeeded' | 'failed' | 'skipped';

export interface SyncJobTable {
  table: string;
  read: number;
  inserted: number;
  updated: number;
  deleted: number;
  rejected: number;
}

export interface SyncJob {
  id: number;
  trigger: SyncTrigger;
  mode: SyncMode;
  status: SyncStatus;
  requestedBy: string | null;
  startedAt: string;
  startedAtJalali: string;
  finishedAt: string | null;
  finishedAtJalali: string | null;
  durationMs: number | null;
  error: string | null;
  skipReason: string | null;
  /** Filled on GET /api/admin/sync/jobs/{id}; may be empty in the list. */
  tables: SyncJobTable[];
}

export interface SyncJobsResponse {
  jobs: SyncJob[];
  running: SyncJob | null;
  source: {
    configured: boolean;
    mapValid: boolean;
    mapProblems: string[];
  };
  schedule: {
    nightlyTime: string;
    timeZone: string;
    nextRunAt: string | null;
    nextRunAtJalali: string | null;
  };
}

/** GET /api/system/status: any signed-in user. */
export function getSystemStatus(): Promise<SystemStatus> {
  return apiGet<SystemStatus>('/api/system/status');
}

/** GET /api/overview/purchasing (Manager, Admin). `month` is Jalali YYYY-MM; omitted = current month. */
export function getPurchasingOverview(month?: string): Promise<PurchasingOverview> {
  const query = month ? `?month=${encodeURIComponent(month)}` : '';
  return apiGet<PurchasingOverview>(`/api/overview/purchasing${query}`);
}

/** GET /api/admin/sync/jobs (Admin only). `limit` 1..200, default 50. */
export function getSyncJobs(limit = 50): Promise<SyncJobsResponse> {
  return apiGet<SyncJobsResponse>(`/api/admin/sync/jobs?limit=${limit}`);
}

/** GET /api/admin/sync/jobs/{id} (Admin only). */
export function getSyncJob(id: number): Promise<SyncJob> {
  return apiGet<SyncJob>(`/api/admin/sync/jobs/${id}`);
}

/** POST /api/admin/sync/run (Admin only): 202 started, 409 already running, 503 cannot start. */
export function runSyncNow(): Promise<{ job: SyncJob }> {
  return apiPost<{ job: SyncJob }>('/api/admin/sync/run');
}
