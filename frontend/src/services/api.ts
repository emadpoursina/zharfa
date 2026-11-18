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
  /** Purchasing reports, vendor directory and comparison (spec 004): navigation only; the server enforces roles. */
  purchasingReports: boolean;
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

// ---------------------------------------------------------------------------
// Vendor and purchasing reports (spec 004, contracts/api.md). Same conventions:
// exact decimal strings in Rial, null = unavailable (listed in `unavailable`),
// no balance, payment, credit, debt or paid field exists anywhere (FR-012).
// ---------------------------------------------------------------------------

export type PeriodKind = 'month' | 'quarter' | 'year' | 'custom';

export interface ReportPeriod {
  kind: PeriodKind;
  /** Jalali YYYY-MM-DD, inclusive. */
  from: string;
  to: string;
  label: string;
  /** Set when the requested range runs past today. */
  trimmedTo: string | null;
}

export interface ReportFilterEcho {
  name: string;
  value: string;
  label: string;
}

export interface ReportUnavailable {
  figure: string;
  reason: string;
}

/** The envelope every data response carries (contracts/api.md §0). */
export interface ReportEnvelope {
  dataLabel: DataLabel;
  lastSuccessfulSync: LastSuccessfulSync | null;
  currency: string;
  currencyLabel: string;
  discountBasis: string;
  period: ReportPeriod;
  /** Present only with compare=true (always on the profile). */
  previousPeriod?: ReportPeriod | null;
  filters: ReportFilterEcho[];
  unavailable: ReportUnavailable[];
}

/** Totals over the full filtered result (never the page). Absent fields are not used by the report. */
export interface ReportTotals {
  net?: string;
  gross?: string;
  returns?: string;
  vat?: string;
  vatInvoice?: string;
  vatReturn?: string;
  netQuantity?: string;
  invoiceAmount?: string;
  returnAmount?: string;
  preDiscount?: string;
  discount?: string;
  documentCount?: number;
  lineCount?: number;
  quantityMissingLines?: number;
}

export interface ReportDrill {
  vendor?: string;
  product?: string;
  category?: string;
  month?: string;
  match?: string;
  docType?: 'invoice' | 'return';
}

export interface GroupedRow {
  key: string | null;
  name: string;
  code: string | null;
  unknown: boolean;
  net: string | null;
  gross: string | null;
  returns: string | null;
  vat: string | null;
  netQuantity: string | null;
  documentCount: number;
  previousNet?: string | null;
  delta?: string | null;
  percent?: string | null;
  rank?: number | null;
  drill: ReportDrill;
}

export interface TrendRow {
  month: string;
  label: string;
  net: string | null;
  netQuantity: string | null;
  available: boolean;
  partial: boolean;
  previousNet?: string | null;
  delta?: string | null;
  percent?: string | null;
  drill: ReportDrill;
}

export interface ReturnsRow {
  vendorKey: string | null;
  vendorName: string;
  productKey: string | null;
  productName: string;
  unknownVendor: boolean;
  unknownProduct: boolean;
  returns: string | null;
  quantity: string | null;
  documentCount: number;
  previousReturns?: string | null;
  delta?: string | null;
  percent?: string | null;
  drill: ReportDrill;
}

export interface DocumentRow {
  documentId: number;
  number: string;
  docType: 'invoice' | 'return';
  docTypeLabel: string;
  date: string;
  dateJalali: string;
  vendorKey: string | null;
  vendorName: string;
  preDiscount: string;
  discount: string;
  vat: string;
  net: string;
  signedNet: string;
  quantity: string;
  lineCount: number;
}

export interface LineRow {
  lineId: number;
  productKey: string | null;
  productName: string;
  categoryName: string | null;
  unknownProduct: boolean;
  quantity: string | null;
  preDiscount: string;
  discount: string;
  lineAmount: string;
  vat: string;
  signedAmount: string;
}

/** A table response: the envelope, paging, totals over every filtered row and the rows of the page. */
export interface ReportTable<TRow> extends ReportEnvelope {
  /** Effective page after clamping to the last page. */
  page: number;
  pageSize: number;
  totalRows: number;
  sort: string;
  dir: 'asc' | 'desc';
  totals: ReportTotals;
  previousTotals?: ReportTotals | null;
  rows: TRow[];
}

/** The print twin: envelope, totals and at most 1,000 rows in the current sort. */
export interface ReportPrint<TRow> extends ReportEnvelope {
  totalRows: number;
  truncated: boolean;
  limit: number;
  totals: ReportTotals;
  previousTotals?: ReportTotals | null;
  rows: TRow[];
}

export interface VendorRow {
  key: string;
  name: string;
  code: string | null;
  /** "0.0000" for a vendor without purchases; null only when unavailable. */
  net: string | null;
  lastPurchaseDate: string | null;
  lastPurchaseDateJalali: string | null;
  previousNet?: string | null;
  delta?: string | null;
  percent?: string | null;
}

export interface LookupItem {
  key: string | null;
  name: string;
  code: string | null;
}

export interface LookupResponse {
  items: LookupItem[];
  unavailable?: ReportUnavailable[];
}

export interface ProfileTrendPoint {
  month: string;
  net: string | null;
  available: boolean;
  partial: boolean;
}

export interface VendorProfile extends ReportEnvelope {
  vendor: { key: string; name: string; code: string | null };
  totals: ReportTotals;
  comparison: { previousNet: string; delta: string; percent: string | null } | null;
  trend: ProfileTrendPoint[];
  topProducts: Array<{ key: string | null; name: string; net: string | null; netQuantity: string | null; unknown: boolean }>;
  returns: Array<{ productKey: string | null; productName: string; returns: string | null; quantity: string | null; unknown: boolean }>;
  documents: { page: number; pageSize: number; totalRows: number; rows: DocumentRow[] };
}

export interface DocumentLines extends Omit<ReportEnvelope, 'period' | 'previousPeriod'> {
  document: DocumentRow;
  lines: LineRow[];
  totals: { lineAmount: string; signedAmount: string; quantity: string };
}

export interface VendorComparisonVendor {
  key: string;
  name: string;
  code: string | null;
  net: string | null;
  netQuantity: string | null;
  documentCount: number | null;
  returns: string | null;
  trend: ProfileTrendPoint[];
}

export interface VendorComparison extends Omit<ReportEnvelope, 'previousPeriod'> {
  months: string[];
  vendors: VendorComparisonVendor[];
  truncated?: boolean;
  limit?: number;
}

export type PurchasingQueryValue = string | number | boolean | null | undefined;

/**
 * GET /api/purchasing/{path} (Manager, Admin). `params` keys with empty values are omitted, so each screen sends only
 * the parameters its report declares. 400/404/503 surface the server's Persian `message`.
 */
export function getPurchasing<T>(path: string, params: Record<string, PurchasingQueryValue> = {}): Promise<T> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<T>(`/api/purchasing/${path}${query ? `?${query}` : ''}`);
}
