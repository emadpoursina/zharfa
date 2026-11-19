import { useEffect, useState } from 'react';
import {
  getVendorFinancialsSummary,
  getVendorFinancialsPrint,
  downloadVendorFinancialsSummaryExport,
  type ReportPrint,
  type VendorFinancialSummaryRow,
  type VendorFinancialsSummaryResponse,
  type VendorFinancialsPrintResponse,
} from '../services/api';
import { canonicalPeriod, defaultPeriod, periodParams, type ReportFilters } from '../hooks/useReportQuery';
import { DataQualityNotice } from '../components/DataQualityNotice';
import { FilterBar } from '../components/FilterBar';
import { Pager } from '../components/Pager';
import { PrintReport } from '../components/PrintReport';
import { SaveReportDialog } from '../components/SaveReportDialog';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from '../components/ReportTable';
import { faPercent, faRialFromDecimal } from '../utils/format';
import { VendorFinancialDetail } from './VendorFinancialDetail';

export interface VendorFinancialsProps {
  serverToday: string;
  showLink?: boolean;
  canExport?: boolean;
  initialVendorKey?: string | null;
}

export function VendorFinancials({ serverToday, showLink = false, canExport = false, initialVendorKey = null }: VendorFinancialsProps) {
  const [filters, setFilters] = useState<ReportFilters>(() => ({
    period: defaultPeriod(serverToday),
    compare: false,
    vendor: null,
    product: null,
    category: null,
    month: null,
    docType: null,
    q: '',
    sort: 'vendorBalance',
    dir: 'desc',
    page: 1,
    pageSize: 25,
  }));

  const [data, setData] = useState<VendorFinancialsSummaryResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [selectedVendor, setSelectedVendor] = useState<{ key: string; name: string } | null>(null);

  const [print, setPrint] = useState<VendorFinancialsPrintResponse | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);

  const [saveOpen, setSaveOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  // If initialVendorKey is passed, load or select it
  useEffect(() => {
    if (initialVendorKey) {
      setSelectedVendor({ key: initialVendorKey, name: initialVendorKey });
    }
  }, [initialVendorKey]);

  // Fetch Summary
  const pQuery = periodParams(filters.period);
  const queryParams = {
    ...pQuery,
    compare: filters.compare,
    q: filters.q ? filters.q.trim() : undefined,
    page: filters.page,
    pageSize: filters.pageSize,
    sort: filters.sort,
    dir: filters.dir,
  };

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    getVendorFinancialsSummary(queryParams)
      .then((res) => {
        if (active) {
          setData(res);
          setLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setError(err instanceof Error ? err.message : 'خطا در بارگذاری اطلاعات مالی تأمین‌کنندگان.');
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [
    JSON.stringify(pQuery),
    filters.compare,
    filters.q,
    filters.page,
    filters.pageSize,
    filters.sort,
    filters.dir,
  ]);

  const updateFilters = (changes: Partial<ReportFilters>) => {
    setFilters((prev) => ({
      ...prev,
      ...changes,
      page: changes.page ?? 1,
    }));
  };

  const toggleSort = (key: string) => {
    if (filters.sort === key) {
      updateFilters({ dir: filters.dir === 'asc' ? 'desc' : 'asc' });
    } else {
      updateFilters({ sort: key, dir: 'desc' });
    }
  };

  const exportNow = async () => {
    setExporting(true);
    setExportError(null);
    try {
      await downloadVendorFinancialsSummaryExport(queryParams);
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'دریافت خروجی اکسل انجام نشد.');
    } finally {
      setExporting(false);
    }
  };

  const printNow = async () => {
    setPrintError(null);
    try {
      const response = await getVendorFinancialsPrint(queryParams);
      setPrint(response);
      window.setTimeout(() => window.print(), 50);
    } catch (caught: unknown) {
      setPrintError(caught instanceof Error ? caught.message : 'چاپ انجام نشد.');
    }
  };

  if (selectedVendor) {
    return (
      <VendorFinancialDetail
        vendor={selectedVendor}
        serverToday={serverToday}
        initialPeriod={filters.period}
        onBack={() => setSelectedVendor(null)}
        showLink={showLink}
        canExport={canExport}
      />
    );
  }

  const columns: ReportColumn<VendorFinancialSummaryRow>[] = [
    { id: 'name', label: 'نام تأمین‌کننده', sortKey: 'name', render: (row) => row.name },
    { id: 'code', label: 'کد', sortKey: 'code', render: (row) => row.code ?? '—' },
    {
      id: 'vendorBalance',
      label: 'مانده تأمین‌کننده',
      sortKey: 'vendorBalance',
      numeric: true,
      render: (row) => (row.vendorBalance ? faRialFromDecimal(row.vendorBalance, { signed: true }) : '—'),
    },
    {
      id: 'outstandingPayments',
      label: 'پرداخت‌های معوق',
      sortKey: 'outstandingPayments',
      numeric: true,
      render: (row) => (row.outstandingPayments ? faRialFromDecimal(row.outstandingPayments) : '—'),
    },
    ...(data?.previousPeriod
      ? [
          {
            id: 'prevBalance',
            label: 'مانده دوره قبل',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.previousVendorBalance ? faRialFromDecimal(row.previousVendorBalance, { signed: true }) : '—',
          },
          {
            id: 'deltaBalance',
            label: 'تفاوت مانده',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.deltaVendorBalance ? faRialFromDecimal(row.deltaVendorBalance, { signed: true }) : '—',
          },
          {
            id: 'percentBalance',
            label: 'درصد تغییر مانده',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.percentVendorBalance ? faPercent(row.percentVendorBalance) : '—',
          },
          {
            id: 'prevOutstanding',
            label: 'معوق دوره قبل',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.previousOutstandingPayments ? faRialFromDecimal(row.previousOutstandingPayments) : '—',
          },
          {
            id: 'deltaOutstanding',
            label: 'تفاوت معوق',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.deltaOutstanding ? faRialFromDecimal(row.deltaOutstanding, { signed: true }) : '—',
          },
          {
            id: 'percentOutstanding',
            label: 'درصد تغییر معوق',
            numeric: true,
            render: (row: VendorFinancialSummaryRow) =>
              row.percentOutstanding ? faPercent(row.percentOutstanding) : '—',
          },
        ]
      : []),
  ];

  return (
    <div className="space-y-5">
      <FilterBar
        filters={filters}
        update={updateFilters}
        setSearch={(text) => updateFilters({ q: text, page: 1 })}
        declared={['compare']}
        searchable
        searchLabel="جستجوی نام یا کد تأمین‌کننده"
        serverToday={serverToday}
        activeLabels={data?.filters}
      />

      {data && (
        <ReportStatusLine
          envelope={{
            dataLabel: data.dataLabel,
            lastSuccessfulSync: data.lastSuccessfulSync ?? null,
            period: data.period,
            previousPeriod: data.previousPeriod ?? null,
            filters: data.filters,
            unavailable: data.unavailable,
          }}
        />
      )}

      {data?.dataQuality && <DataQualityNotice notice={data.dataQuality} showLink={showLink} />}

      {error && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </p>
      )}

      {data?.unavailable && data.unavailable.length > 0 && (
        <div className="space-y-2">
          {data.unavailable.map((u) => (
            <p key={u.figure} className="rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-900">
              <span className="font-semibold">{u.figure}:</span> {u.message} ({u.reason})
            </p>
          ))}
        </div>
      )}

      {/* Action buttons */}
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={() => setSaveOpen(true)}
          disabled={loading || !data}
          className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-50 cursor-pointer"
        >
          ذخیره گزارش
        </button>
        {canExport && (
          <button
            type="button"
            onClick={exportNow}
            disabled={loading || !data || exporting}
            className="rounded-lg border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-sm font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
          >
            {exporting ? 'در حال دریافت...' : 'خروجی اکسل'}
          </button>
        )}
        <button
          type="button"
          onClick={printNow}
          disabled={loading || !data}
          className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-50 cursor-pointer"
        >
          چاپ فهرست
        </button>
      </div>

      {exportError && <p role="alert" className="text-sm text-red-700">{exportError}</p>}
      {printError && <p role="alert" className="text-sm text-red-700">{printError}</p>}

      {data && (
        <>
          <Table
            caption="فهرست اطلاعات مالی تأمین‌کنندگان"
            columns={columns}
            rows={data.rows}
            rowKey={(row) => row.key}
            sort={filters.sort}
            dir={filters.dir}
            onSort={toggleSort}
            rowActionLabel=""
            rowAction={(row) => (
              <button
                type="button"
                onClick={() => setSelectedVendor({ key: row.key, name: row.name })}
                className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                aria-label={`جزئیات مالی ${row.name}`}
              >
                جزئیات مالی
              </button>
            )}
            emptyText="اطلاعات مالی برای تأمین‌کننده‌ای در این بازه یافت نشد."
          />

          <Pager
            page={filters.page}
            pageSize={filters.pageSize}
            totalRows={data.totalRows}
            onPage={(page) => updateFilters({ page })}
            onPageSize={(pageSize) => updateFilters({ pageSize, page: 1 })}
          />
        </>
      )}

      {saveOpen && (
        <SaveReportDialog
          open={saveOpen}
          onClose={() => setSaveOpen(false)}
          defaultName="اطلاعات مالی تأمین‌کنندگان"
          kind="vendor-financials"
          period={canonicalPeriod(filters.period)}
          filters={{ compare: filters.compare }}
        />
      )}

      <PrintReport
        title="اطلاعات مالی تأمین‌کنندگان"
        print={print as unknown as ReportPrint<VendorFinancialSummaryRow>}
        columns={columns}
        rowKey={(row) => row.key}
      />
    </div>
  );
}
