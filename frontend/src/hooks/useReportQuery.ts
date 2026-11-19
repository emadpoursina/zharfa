import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  getPurchasing,
  type PeriodKind,
  type PurchasingQueryValue,
  type ReportTable,
} from '../services/api';
import { currentQuarterKey, currentYearKey, toLatinDigits } from '../utils/jalali';

/** Filters a report or list may declare (contracts/api.md §0). `match` is set only by drill-down, never here. */
export type ReportFilterName = 'vendor' | 'product' | 'category' | 'month' | 'docType' | 'compare';

export const PAGE_SIZES = [25, 50, 100] as const;
export type PageSize = (typeof PAGE_SIZES)[number];

const SEARCH_DEBOUNCE_MS = 300;

/** Period value of the filter bar. Keys are Latin Jalali keys; `from`/`to` are «YYYY-MM-DD» for a custom range. */
export interface PeriodValue {
  kind: PeriodKind;
  /** «YYYY-MM» */
  month: string;
  /** «YYYY-Qn» */
  quarter: string;
  /** «YYYY» */
  year: string;
  /** «YYYY-MM-DD» (custom) */
  from: string;
  to: string;
}

/** One filter state for every screen that uses the query hook. */
export interface ReportFilters {
  period: PeriodValue;
  compare: boolean;
  vendor: string | null;
  product: string | null;
  category: string | null;
  /** «YYYY-MM» drill-down filter of a month (trend), not the period. */
  month: string | null;
  docType: 'invoice' | 'return' | null;
  /** Search text as typed; the query uses it after a short debounce. */
  q: string;
  sort: string;
  dir: 'asc' | 'desc';
  page: number;
  pageSize: PageSize;
}

/** The default period is the current Jalali month of the server (sync time zone). */
export function defaultPeriod(serverToday: string): PeriodValue {
  const today = toLatinDigits(serverToday);
  return {
    kind: 'month',
    month: today.slice(0, 7),
    quarter: currentQuarterKey(today),
    year: currentYearKey(today),
    from: today.slice(0, 10),
    to: today.slice(0, 10),
  };
}

/** The period's query parameters: only the ones its kind uses (contracts/api.md §0). */
export function periodParams(period: PeriodValue): Record<string, PurchasingQueryValue> {
  switch (period.kind) {
    case 'month':
      return { period: 'month', month: period.month };
    case 'quarter':
      return { period: 'quarter', quarter: period.quarter };
    case 'year':
      return { period: 'year', year: period.year };
    case 'custom':
      return { period: 'custom', from: period.from, to: period.to };
  }
}

/** Canonical data-model period object (data-model.md §3) for saving reports. */
export function canonicalPeriod(period: PeriodValue): { kind: string; month?: string; quarter?: string; year?: string; from?: string; to?: string } {
  switch (period.kind) {
    case 'month':
      return { kind: 'month', month: period.month };
    case 'quarter':
      return { kind: 'quarter', quarter: period.quarter };
    case 'year':
      return { kind: 'year', year: period.year };
    case 'custom':
      return { kind: 'custom', from: period.from, to: period.to };
  }
}

export interface ReportQueryOptions {
  /** Path below /api/purchasing, without query, for example `reports/by-vendor` or `vendors`. */
  path: string;
  /** Filters the report declares; anything else is never sent (the server would answer 400). */
  declared: readonly ReportFilterName[];
  /** Whether the report searches (`q`). */
  searchable: boolean;
  sortKeys: readonly string[];
  initialSort: string;
  initialDir: 'asc' | 'desc';
  /** Server's Jalali today «YYYY-MM-DD» for the default period. */
  serverToday: string;
}

export interface ReportQuery<TRow> {
  filters: ReportFilters;
  /** Patches the filters. Any change other than `page` resets the page to 1. */
  update: (changes: Partial<ReportFilters>) => void;
  /** Sets the search text; the query follows it after the debounce and resets the page. */
  setSearch: (text: string) => void;
  /** Sort on a header: same key flips the direction. */
  toggleSort: (key: string) => void;
  data: ReportTable<TRow> | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
  /** Query for the screen, built from the declared parameters only. */
  screenParams: () => Record<string, PurchasingQueryValue>;
  /** Query for the print twin: the same filters and sort, without paging. */
  printParams: () => Record<string, PurchasingQueryValue>;
  /** Path of the print twin. */
  printPath: string;
}

function messageOf(error: unknown): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : 'خطایی در پردازش درخواست رخ داد.';
}

/**
 * One filter state, the server-paged table and its print twin (spec 004 T036). The query carries only the
 * parameters the report declares; the page follows the server's effective page after clamping.
 */
export function useReportQuery<TRow>(options: ReportQueryOptions): ReportQuery<TRow> {
  const { path, declared, searchable, serverToday, initialSort, initialDir } = options;
  const has = useCallback((name: ReportFilterName) => declared.includes(name), [declared]);

  const [filters, setFilters] = useState<ReportFilters>(() => ({
    period: defaultPeriod(serverToday),
    compare: false,
    vendor: null,
    product: null,
    category: null,
    month: null,
    docType: null,
    q: '',
    sort: initialSort,
    dir: initialDir,
    page: 1,
    pageSize: 25,
  }));

  const [debouncedQ, setDebouncedQ] = useState('');
  const firstSearch = useRef(true);
  const [data, setData] = useState<ReportTable<TRow> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);

  // Debounce the search text; a new search starts on the first page.
  useEffect(() => {
    if (!searchable) {
      return;
    }
    const timer = window.setTimeout(() => setDebouncedQ(filters.q.trim()), SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [filters.q, searchable]);

  useEffect(() => {
    if (firstSearch.current) {
      firstSearch.current = false;
      return;
    }
    setFilters((current) => (current.page === 1 ? current : { ...current, page: 1 }));
  }, [debouncedQ]);

  const update = useCallback((changes: Partial<ReportFilters>) => {
    setFilters((current) => {
      const onlyPage = Object.keys(changes).every((key) => key === 'page');
      return { ...current, ...changes, page: onlyPage ? (changes.page ?? current.page) : 1 };
    });
  }, []);

  const setSearch = useCallback((text: string) => {
    setFilters((current) => ({ ...current, q: text }));
  }, []);

  const toggleSort = useCallback((key: string) => {
    setFilters((current) => {
      if (current.sort === key) {
        return { ...current, dir: current.dir === 'asc' ? 'desc' : 'asc', page: 1 };
      }
      return { ...current, sort: key, dir: 'desc', page: 1 };
    });
  }, []);

  const buildParams = useCallback(
    (print: boolean): Record<string, PurchasingQueryValue> => {
      const params: Record<string, PurchasingQueryValue> = { ...periodParams(filters.period) };
      if (has('vendor') && filters.vendor) params.vendor = filters.vendor;
      if (has('product') && filters.product) params.product = filters.product;
      if (has('category') && filters.category) params.category = filters.category;
      if (has('month') && filters.month) params.month = filters.month;
      if (has('docType') && filters.docType) params.docType = filters.docType;
      if (has('compare') && filters.compare) params.compare = true;
      if (searchable && debouncedQ) params.q = debouncedQ;
      params.sort = filters.sort;
      params.dir = filters.dir;
      if (!print) {
        params.page = filters.page;
        params.pageSize = filters.pageSize;
      }
      return params;
    },
    [filters, has, searchable, debouncedQ],
  );

  const screenParams = useCallback(() => buildParams(false), [buildParams]);
  const printParams = useCallback(() => buildParams(true), [buildParams]);

  const paramKey = useMemo(() => JSON.stringify(buildParams(false)), [buildParams]);

  useEffect(() => {
    let active = true;
    const requested = JSON.parse(paramKey) as Record<string, PurchasingQueryValue>;
    setLoading(true);
    setError(null);
    getPurchasing<ReportTable<TRow>>(path, requested)
      .then((response) => {
        if (!active) {
          return;
        }
        setData(response);
        setLoading(false);
        if (response.page !== requested.page) {
          // The requested page was beyond the last one: adopt the server's page and refetch.
          setFilters((current) => ({ ...current, page: response.page }));
        }
      })
      .catch((caught: unknown) => {
        if (active) {
          setError(messageOf(caught));
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [path, paramKey, reloadToken]);

  const reload = useCallback(() => setReloadToken((token) => token + 1), []);

  return {
    filters,
    update,
    setSearch,
    toggleSort,
    data,
    error,
    loading,
    reload,
    screenParams,
    printParams,
    printPath: `${path}/print`,
  };
}
