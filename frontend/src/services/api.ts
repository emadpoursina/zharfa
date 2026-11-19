/**
 * API client for Zharfa backend.
 * Uses native fetch with same-origin credentials (cookie-based session).
 * No tokens, no client storage, no secrets.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly isUnauthenticated: boolean;
  /** Machine code from the server body (for example `forbidden` or `password-change-required`), when present. */
  readonly code?: string;
  readonly data?: unknown;

  constructor(status: number, message: string, data?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.isUnauthenticated = status === 401;
    this.code = readCode(data);
    this.data = data;
  }
}

function readCode(data: unknown): string | undefined {
  if (typeof data === 'object' && data !== null) {
    const code = (data as Record<string, unknown>).code;
    if (typeof code === 'string' && code.length > 0) {
      return code;
    }
  }
  return undefined;
}

export function isUnauthenticated(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthenticated;
}

/** 403 for a signed-in caller whose role lacks the permission (not the forced password change). */
export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403 && error.code !== 'password-change-required';
}

/** 403 `password-change-required`: the session must change its password before anything else. */
export function isPasswordChangeRequired(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403 && error.code === 'password-change-required';
}

/** Window event: a call answered 403 `password-change-required`; the shell shows the change-password screen. */
export const PASSWORD_CHANGE_REQUIRED_EVENT = 'zharfa:password-change-required';

/** Window event: a call answered 403 for a missing permission; the shell shows the no-access view. */
export const FORBIDDEN_EVENT = 'zharfa:forbidden';

/** Tells the shell about a refusal, so a page the role may not see is replaced by the no-access view. */
function announceRefusal(error: ApiError): void {
  if (typeof window === 'undefined') {
    return;
  }
  if (isPasswordChangeRequired(error)) {
    window.dispatchEvent(new Event(PASSWORD_CHANGE_REQUIRED_EVENT));
  } else if (isForbidden(error)) {
    window.dispatchEvent(new Event(FORBIDDEN_EVENT));
  }
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

  const refusal = new ApiError(res.status, errorMessage, responseData);
  announceRefusal(refusal);
  throw refusal;
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

export async function apiDelete<T = void>(path: string): Promise<T> {
  const res = await fetch(path, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
    },
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
  /** Purchasing reports, vendor directory and comparison (spec 004): navigation only; the server enforces permissions. */
  purchasingReports: boolean;
  /** Users and roles page (admin permission): navigation only. */
  users: boolean;
  /** Audit log page (admin permission): navigation only. */
  auditLog: boolean;
  /** Data quality page (admin permission): navigation only. */
  dataQuality: boolean;
  /** AI provider settings page (admin permission): navigation only. */
  aiSettings: boolean;
  /** Saved reports page (purchasing.reports permission, spec 007). */
  savedReports: boolean;
  /** Vendor financials (spec 008): true iff the caller holds `vendor.financials`; navigation only — the server enforces the policy. */
  vendorFinancials: boolean;
}

export interface SystemStatus {
  dataMode: DataMode;
  dataLabel: DataLabel;
  lastSuccessfulSync: LastSuccessfulSync | null;
  /** The caller's role id, for example `purchasing`. */
  role: string;
  /** Persian label of the caller's role. */
  roleLabel: string;
  /** Permission ids granted to the caller, in the server's order. */
  permissions: string[];
  modules: SystemModules;
  /** Jalali date of the server today in the sync time zone, e.g. 1405-08-25. */
  serverJalaliToday: string;
}

/** GET /api/auth/me: the signed-in user and the permissions of their role. */
export interface Me {
  id: number;
  username: string;
  role: string;
  roleLabel: string;
  permissions: string[];
  mustChangePassword: boolean;
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
  dataQuality?: DataQualityNotice | null;
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
  trimmedTo?: string | null;
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

export interface DataQualityNotice {
  issueTypes: { type: string; label: string; count: number }[];
  affectedRecords: number;
  unavailable: boolean;
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
  dataQuality?: DataQualityNotice | null;
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

// ---------------------------------------------------------------------------
// Accounts, roles and users (spec 005, contracts/api.md §1–§2). Every call goes through apiGet/apiPost; the server
// enforces the Admin permission on the /api/admin routes and the signed-in rule on /api/auth.
// ---------------------------------------------------------------------------

export interface RolePermissionInfo {
  id: string;
  label: string;
}

export interface RoleInfo {
  id: string;
  label: string;
  permissions: RolePermissionInfo[];
}

export interface UserRow {
  id: number;
  username: string;
  role: string;
  roleLabel: string;
  active: boolean;
  locked: boolean;
  /** Jalali date and time with Persian digits, present while the account is locked. */
  lockedUntilJalali: string | null;
  mustChangePassword: boolean;
  createdAtJalali: string;
}

export interface UsersResponse {
  dataLabel: DataLabel;
  users: UserRow[];
}

/** Reads the signed-in user (the `Me` shape of contracts/api.md §1). */
export function getMe(): Promise<Me> {
  return apiGet<Me>('/api/auth/me');
}

/** POST /api/auth/change-password: 204 on success; 400 carries the Persian reason. */
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return apiPost<void>('/api/auth/change-password', { currentPassword, newPassword });
}

/** GET /api/admin/roles: the four roles in contract order with their permission labels. */
export function getRoles(): Promise<RoleInfo[]> {
  return apiGet<{ roles: RoleInfo[] }>('/api/admin/roles').then((response) => response.roles);
}

/** GET /api/admin/users: every user sorted by username (never a hash or a failure counter). */
export function getUsers(): Promise<UsersResponse> {
  return apiGet<UsersResponse>('/api/admin/users');
}

export function createUser(username: string, password: string, role: string): Promise<UserRow> {
  return apiPost<UserRow>('/api/admin/users', { username, password, role });
}

export function setUserRole(id: number, role: string): Promise<UserRow> {
  return apiPost<UserRow>(`/api/admin/users/${id}/role`, { role });
}

export function resetUserPassword(id: number, password: string): Promise<void> {
  return apiPost<void>(`/api/admin/users/${id}/password`, { password });
}

export function deactivateUser(id: number): Promise<UserRow> {
  return apiPost<UserRow>(`/api/admin/users/${id}/deactivate`);
}

export function reactivateUser(id: number): Promise<UserRow> {
  return apiPost<UserRow>(`/api/admin/users/${id}/reactivate`);
}

export function unlockUser(id: number): Promise<UserRow> {
  return apiPost<UserRow>(`/api/admin/users/${id}/unlock`);
}

// ---------------------------------------------------------------------------
// Audit log and data quality (spec 005, contracts/api.md §3–§4). Admin only; the server enforces the permission.
// ---------------------------------------------------------------------------

export interface AuditAction {
  code: string;
  label: string;
}

export interface AuditRow {
  id: number;
  /** ISO 8601 with the sync time zone offset. */
  at: string;
  /** yyyy/mm/dd HH:mm with Persian digits. */
  atJalali: string;
  actor: string | null;
  action: string;
  actionLabel: string;
  target: string | null;
  detail: string | null;
}

export interface AuditPage {
  dataLabel: DataLabel;
  total: number;
  page: number;
  pageSize: number;
  rows: AuditRow[];
}

export interface AuditQueryParams {
  user?: string;
  action?: string;
  /** Jalali yyyy-mm-dd (Latin or Persian digits). */
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

function queryString(params: Record<string, PurchasingQueryValue>): string {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return query ? `?${query}` : '';
}

/** GET /api/admin/audit/actions: the action catalog with Persian labels. */
export function getAuditActions(): Promise<AuditAction[]> {
  return apiGet<{ actions: AuditAction[] }>('/api/admin/audit/actions').then((response) => response.actions);
}

/** GET /api/admin/audit: newest first, server-paged. A first page writes an audit entry on the server. */
export function getAuditLog(params: AuditQueryParams = {}): Promise<AuditPage> {
  return apiGet<AuditPage>(`/api/admin/audit${queryString({ ...params })}`);
}

export interface DataQualityExample {
  recordKind: 'document' | 'line' | 'product';
  documentNumber: string | null;
  dateJalali: string | null;
  vendor: string | null;
  product: string | null;
  field: string;
  fieldLabel: string;
}

export interface DataQualityAffectedReport {
  key: string;
  label: string;
}

export interface DataQualityNotChecked {
  code: string;
  reason: string;
}

export interface DataQualityIssueType {
  type: string;
  label: string;
  description: string;
  /** Null when no check has run; the page then shows S-DQ-NO-CHECK. */
  count: number | null;
  examples: DataQualityExample[];
  affectedReports: DataQualityAffectedReport[];
  /** Persian note for a heuristic rule (inconsistent-product-ids), else null. */
  heuristic: string | null;
  notChecked: DataQualityNotChecked[];
}

export interface RejectedAtSync {
  table: string;
  reason: string;
  count: number;
}

export interface DataQualityPage {
  dataLabel: DataLabel;
  latestSync: {
    id: number;
    status: string;
    finishedAt: string | null;
    finishedAtJalali: string | null;
    error: string | null;
  } | null;
  latestSyncFailed: boolean;
  check: {
    runId: number;
    syncJobId: number;
    checkedAt: string;
    checkedAtJalali: string;
  } | null;
  latestCheckFailed: boolean;
  focus: string[];
  issueTypes: DataQualityIssueType[];
  rejectedAtSync: RejectedAtSync[];
}

/** GET /api/admin/data-quality[?types=a,b]: every issue type; `types` only narrows the focus. */
export function getDataQuality(types: string[] = []): Promise<DataQualityPage> {
  return apiGet<DataQualityPage>(`/api/admin/data-quality${queryString({ types: types.join(',') })}`);
}

// ---- Local AI over Purchasing (Phase 4) ----

export interface PurchasingPeriod {
  from: string;
  to: string;
  jalaliLabel: string;
  month?: string | null;
}

export interface PurchasingFilters {
  vendor: string | null;
  product: string | null;
  category: string | null;
}

export interface VisualColumn {
  id: string;
  header: string;
}

export interface VisualCaptions {
  period: string;
  filters: string;
  metrics: string;
}

export interface VisualChartSeries {
  metric: string;
  label: string;
  values: (string | null)[];
}

export interface VisualChart {
  type: string;
  categoryId: string;
  series: VisualChartSeries[];
}

export interface VisualTable {
  columns: VisualColumn[];
  rows: Record<string, string | null | undefined>[];
}

export interface SavedReportPeriodSpec {
  kind: string;
  from?: string;
  to?: string;
  month?: string;
  quarter?: string;
  year?: string;
}

export interface GeneratedToolSpec {
  tool: string;
  limit: number;
  sort: string;
  dir: string;
  chart: string;
  metrics: string[];
}

export interface VisualSaveSpec {
  kind: string;
  period: SavedReportPeriodSpec;
  filters: Record<string, string | null | undefined>;
  generated: GeneratedToolSpec;
}

export interface PurchasingVisual {
  kind: string;
  captions: VisualCaptions;
  table: VisualTable;
  chart?: VisualChart | null;
  dataQuality?: DataQualityNotice | null;
  saveSpec: VisualSaveSpec;
  dataLabel?: DataLabel;
}

export interface PurchasingChatResponse {
  answer: string;
  period: PurchasingPeriod;
  filters: PurchasingFilters;
  view: string | null;
  facts: string[];
  interpretation: string;
  unavailable: boolean;
  dataLabel: DataLabel;
  demoNotice?: string | null;
  visual?: PurchasingVisual | null;
}

export interface PurchasingChatUnavailableResponse {
  unavailable: boolean;
  code?: string;
  answer: string;
  message: string;
  period?: PurchasingPeriod | null;
  filters?: PurchasingFilters | null;
  facts: string[];
  interpretation: string;
  view: string | null;
  dataLabel: DataLabel;
  demoNotice?: string | null;
}

export function postPurchasingChat(question: string): Promise<PurchasingChatResponse> {
  return apiPost<PurchasingChatResponse>('/api/ai/chat', { question });
}

export interface AiProviderConfig {
  name: string;
  endpoint: string;
  model: string;
  configured: boolean;
  isDefault?: boolean;
  enabled?: boolean;
  ownerApprovalRecorded?: boolean;
}

export interface AiSettingsResponse {
  answeringProvider: 'local' | 'cloud';
  aggregatesOnlyNotice: string;
  local: AiProviderConfig;
  cloud: AiProviderConfig;
}

export interface AiTestResponse {
  ok: boolean;
  message: string;
}

export function getAiSettings(): Promise<AiSettingsResponse> {
  return apiGet<AiSettingsResponse>('/api/admin/ai');
}

export function testAiProvider(target: 'local' | 'cloud'): Promise<AiTestResponse> {
  return apiPost<AiTestResponse>('/api/admin/ai/test', { target });
}

export function setAiCloud(enabled: boolean, ownerApprovalNote?: string): Promise<AiSettingsResponse> {
  return apiPost<AiSettingsResponse>('/api/admin/ai/cloud', { enabled, ownerApprovalNote });
}

// ---------------------------------------------------------------------------
// Saved Reports (spec 007, contracts/api.md §2)
// ---------------------------------------------------------------------------

export interface SavedReportItem {
  id: number;
  name: string;
  kind: string;
  kindLabel: string;
  requiredPermission: string;
  period: Record<string, unknown>;
  filters: unknown[];
  updatedAtJalali: string;
}

export interface SavedReportListResponse {
  dataLabel: DataLabel;
  max: number;
  items: SavedReportItem[];
}

export interface SaveReportRequest {
  name: string;
  kind: string;
  period: unknown;
  filters?: unknown;
  generated?: unknown;
  overwrite?: boolean;
}

export interface SavedReportOpenResponse {
  data?: unknown;
  saved: {
    id: number;
    name: string;
    kind: string;
  };
  period?: PurchasingPeriod;
  filters?: PurchasingFilters;
  view?: string | null;
  visual?: PurchasingVisual;
  dataLabel?: DataLabel;
  unavailable?: boolean;
}

export function getSavedReports(): Promise<SavedReportListResponse> {
  return apiGet<SavedReportListResponse>('/api/saved-reports');
}

export function getSavedReport(id: number): Promise<SavedReportItem> {
  return apiGet<SavedReportItem>(`/api/saved-reports/${id}`);
}

export function saveReport(req: SaveReportRequest): Promise<SavedReportItem> {
  return apiPost<SavedReportItem>('/api/saved-reports', req);
}

export function openSavedReport(id: number): Promise<SavedReportOpenResponse> {
  return apiPost<SavedReportOpenResponse>(`/api/saved-reports/${id}/open`, {});
}

export function deleteSavedReport(id: number): Promise<void> {
  return apiDelete<void>(`/api/saved-reports/${id}`);
}

// ---------------------------------------------------------------------------
// Excel Export helpers (spec 007 FR-014..FR-019)
// ---------------------------------------------------------------------------

/**
 * Triggers a file download from an authenticated GET/POST endpoint (spec 007 FR-014).
 * Uses credentials/cookie, parses filename from Content-Disposition header (RFC 5987 / UTF-8),
 * creates a temporary object URL and clicks it.
 */
export async function downloadFile(url: string, body?: unknown, method: 'GET' | 'POST' = 'GET'): Promise<void> {
  const options: RequestInit = {
    method,
    credentials: 'same-origin',
  };

  if (method === 'POST') {
    options.headers = {
      'Content-Type': 'application/json',
    };
    if (body !== undefined) {
      options.body = JSON.stringify(body);
    }
  }

  const res = await fetch(url, options);

  if (!res.ok) {
    let errorMessage = 'خطا در دریافت فایل اکسل.';
    try {
      const json = await res.json();
      if (typeof json === 'object' && json !== null && typeof (json as { message?: string }).message === 'string') {
        errorMessage = (json as { message: string }).message;
      }
    } catch {
      // ignore
    }
    const refusal = new ApiError(res.status, errorMessage);
    announceRefusal(refusal);
    throw refusal;
  }

  let filename = 'گزارش.xlsx';
  const disposition = res.headers.get('content-disposition');
  if (disposition) {
    const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (utf8Match?.[1]) {
      try {
        filename = decodeURIComponent(utf8Match[1]);
      } catch {
        filename = utf8Match[1];
      }
    } else {
      const match = disposition.match(/filename="?([^";]+)"?/i);
      if (match?.[1]) {
        filename = match[1];
      }
    }
  }

  const blob = await res.blob();
  const objectUrl = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  setTimeout(() => {
    document.body.removeChild(a);
    window.URL.revokeObjectURL(objectUrl);
  }, 100);
}

export function downloadReportExport(path: string, params?: Record<string, PurchasingQueryValue>): Promise<void> {
  const search = new URLSearchParams();
  if (params) {
    for (const [name, value] of Object.entries(params)) {
      if (value !== undefined && value !== null && value !== '') {
        search.set(name, String(value));
      }
    }
  }
  const query = search.toString();
  return downloadFile(`/api/purchasing/${path}/export${query ? `?${query}` : ''}`, undefined, 'GET');
}

export function downloadAiVisualExport(saveSpec: unknown): Promise<void> {
  return downloadFile('/api/ai/visual/export', saveSpec, 'POST');
}

export function downloadSavedReportExport(id: number): Promise<void> {
  return downloadFile(`/api/saved-reports/${id}/export`, undefined, 'GET');
}

// ---------------------------------------------------------------------------
// Vendor Financials (Release 1, Phase 6, contracts/api.md §1–§5)
// ---------------------------------------------------------------------------

export interface VendorFinancialUnavailable {
  figure: string;
  reason: string;
  message: string;
}

export interface VendorFinancialPeriod extends ReportPeriod {
  month?: string | null;
}

export interface VendorFinancialSummaryRow {
  key: string;
  name: string;
  code: string | null;
  vendorBalance: string | null;
  outstandingPayments: string | null;
  previousVendorBalance?: string | null;
  previousOutstandingPayments?: string | null;
  deltaVendorBalance?: string | null;
  percentVendorBalance?: string | null;
  deltaOutstanding?: string | null;
  percentOutstanding?: string | null;
}

export interface VendorFinancialsSummaryResponse {
  dataLabel: DataLabel;
  demoNotice?: string | null;
  lastSuccessfulSync?: LastSuccessfulSync | null;
  currency: string;
  currencyLabel: string;
  period: VendorFinancialPeriod;
  previousPeriod?: VendorFinancialPeriod | null;
  filters: ReportFilterEcho[];
  unavailable: VendorFinancialUnavailable[];
  compare: boolean;
  page: number;
  pageSize: number;
  totalRows: number;
  search: { q: string; normalized: string };
  sort: { key: string; dir: 'asc' | 'desc' };
  dataQuality?: DataQualityNotice | null;
  available: boolean;
  rows: VendorFinancialSummaryRow[];
}

export interface VendorFinancialsPrintResponse extends Omit<VendorFinancialsSummaryResponse, 'page' | 'pageSize'> {
  truncated: boolean;
  limit: number;
}

export interface VendorPurchasesVsPayments {
  netPurchases: string | null;
  payments: string | null;
  purchasesMinusPayments?: string | null;
  previousNetPurchases?: string | null;
  previousPayments?: string | null;
  deltaNetPurchases?: string | null;
  percentNetPurchases?: string | null;
  deltaPayments?: string | null;
  percentPayments?: string | null;
}

export interface VendorFinancialDetailResponse {
  dataLabel: DataLabel;
  demoNotice?: string | null;
  lastSuccessfulSync?: LastSuccessfulSync | null;
  currency: string;
  currencyLabel: string;
  period: VendorFinancialPeriod;
  previousPeriod?: VendorFinancialPeriod | null;
  vendor: { key: string; name: string; code?: string | null };
  unavailable: VendorFinancialUnavailable[];
  dataQuality?: DataQualityNotice | null;
  available: boolean;
  vendorBalance: string | null;
  outstandingPayments: string | null;
  purchasesVsPayments: VendorPurchasesVsPayments;
}

export interface PaymentHistoryRow {
  paidAtJalali: string;
  reference: string | null;
  instrumentType: string;
  instrumentTypeLabel: string;
  amount: string;
}

export interface OutstandingInstrumentRow {
  dateJalali: string;
  reference: string | null;
  instrumentType: string;
  instrumentTypeLabel: string;
  unpaidAmount: string;
}

export interface VendorFinancialListResponse<T> {
  dataLabel: DataLabel;
  demoNotice?: string | null;
  lastSuccessfulSync?: LastSuccessfulSync | null;
  currency: string;
  currencyLabel: string;
  period: VendorFinancialPeriod;
  filters: ReportFilterEcho[];
  unavailable: VendorFinancialUnavailable[];
  page: number;
  pageSize: number;
  totalRows: number;
  sort: string;
  dir: 'asc' | 'desc';
  dataQuality?: DataQualityNotice | null;
  available: boolean;
  rows: T[];
}

export function getVendorFinancialsSummary(params: Record<string, PurchasingQueryValue> = {}): Promise<VendorFinancialsSummaryResponse> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<VendorFinancialsSummaryResponse>(`/api/vendor-financials/summary${query ? `?${query}` : ''}`);
}

export function getVendorFinancialsPrint(params: Record<string, PurchasingQueryValue> = {}): Promise<VendorFinancialsPrintResponse> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<VendorFinancialsPrintResponse>(`/api/vendor-financials/summary/print${query ? `?${query}` : ''}`);
}

export function getVendorFinancialDetail(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<VendorFinancialDetailResponse> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<VendorFinancialDetailResponse>(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}${query ? `?${query}` : ''}`);
}

export function getVendorPaymentHistory(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<VendorFinancialListResponse<PaymentHistoryRow>> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<VendorFinancialListResponse<PaymentHistoryRow>>(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}/history${query ? `?${query}` : ''}`);
}

export function getVendorOutstanding(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<VendorFinancialListResponse<OutstandingInstrumentRow>> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return apiGet<VendorFinancialListResponse<OutstandingInstrumentRow>>(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}/outstanding${query ? `?${query}` : ''}`);
}

export function downloadVendorFinancialsSummaryExport(params: Record<string, PurchasingQueryValue> = {}): Promise<void> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return downloadFile(`/api/vendor-financials/summary/export${query ? `?${query}` : ''}`, undefined, 'GET');
}

export function downloadVendorHistoryExport(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<void> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return downloadFile(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}/history/export${query ? `?${query}` : ''}`, undefined, 'GET');
}

export function downloadVendorOutstandingExport(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<void> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return downloadFile(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}/outstanding/export${query ? `?${query}` : ''}`, undefined, 'GET');
}

export function downloadVendorPurchasesVsPaymentsExport(vendorKey: string, params: Record<string, PurchasingQueryValue> = {}): Promise<void> {
  const search = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(name, String(value));
    }
  }
  const query = search.toString();
  return downloadFile(`/api/vendor-financials/vendors/${encodeURIComponent(vendorKey)}/purchases-vs-payments/export${query ? `?${query}` : ''}`, undefined, 'GET');
}
