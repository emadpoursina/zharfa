import { useEffect, useState } from 'react';
import {
  getVendorFinancialDetail,
  getVendorPaymentHistory,
  getVendorOutstanding,
  downloadVendorHistoryExport,
  downloadVendorOutstandingExport,
  downloadVendorPurchasesVsPaymentsExport,
  type VendorFinancialDetailResponse,
  type VendorFinancialListResponse,
  type PaymentHistoryRow,
  type OutstandingInstrumentRow,
} from '../services/api';
import { periodParams, canonicalPeriod, type PageSize, type PeriodValue } from '../hooks/useReportQuery';
import { DataQualityNotice } from '../components/DataQualityNotice';
import { KpiCard } from '../components/KpiCard';
import { Pager } from '../components/Pager';
import { PeriodPicker } from '../components/PeriodPicker';
import { SaveReportDialog } from '../components/SaveReportDialog';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from '../components/ReportTable';
import { faPercent, faRialFromDecimal } from '../utils/format';

export interface VendorFinancialDetailProps {
  vendor: { key: string; name: string };
  serverToday: string;
  initialPeriod: PeriodValue;
  onBack: () => void;
  showLink?: boolean;
  canExport?: boolean;
}

const historyColumns: ReportColumn<PaymentHistoryRow>[] = [
  { id: 'paidAt', label: 'تاریخ پرداخت', sortKey: 'paidAt', render: (row) => row.paidAtJalali },
  { id: 'reference', label: 'مرجع', sortKey: 'reference', render: (row) => row.reference ?? '—' },
  { id: 'type', label: 'نوع سند', render: (row) => row.instrumentTypeLabel },
  { id: 'amount', label: 'مبلغ', sortKey: 'amount', numeric: true, render: (row) => faRialFromDecimal(row.amount) },
];

const outstandingColumns: ReportColumn<OutstandingInstrumentRow>[] = [
  { id: 'date', label: 'تاریخ', sortKey: 'date', render: (row) => row.dateJalali },
  { id: 'reference', label: 'مرجع', sortKey: 'reference', render: (row) => row.reference ?? '—' },
  { id: 'type', label: 'نوع سند', render: (row) => row.instrumentTypeLabel },
  { id: 'unpaidAmount', label: 'مبلغ معوق', sortKey: 'amount', numeric: true, render: (row) => faRialFromDecimal(row.unpaidAmount) },
];

export function VendorFinancialDetail({
  vendor,
  serverToday,
  initialPeriod,
  onBack,
  showLink = false,
  canExport = false,
}: VendorFinancialDetailProps) {
  const [period, setPeriod] = useState<PeriodValue>(initialPeriod);
  const [comparePvp, setComparePvp] = useState(false);

  const [header, setHeader] = useState<VendorFinancialDetailResponse | null>(null);
  const [, setHeaderLoading] = useState(false);
  const [headerError, setHeaderError] = useState<string | null>(null);

  const [history, setHistory] = useState<VendorFinancialListResponse<PaymentHistoryRow> | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [historyPageSize, setHistoryPageSize] = useState<PageSize>(25);
  const [historySort, setHistorySort] = useState('paidAt');
  const [historyDir, setHistoryDir] = useState<'asc' | 'desc'>('desc');
  const [, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

  const [outstanding, setOutstanding] = useState<VendorFinancialListResponse<OutstandingInstrumentRow> | null>(null);
  const [outstandingPage, setOutstandingPage] = useState(1);
  const [outstandingPageSize, setOutstandingPageSize] = useState<PageSize>(25);
  const [outstandingSort, setOutstandingSort] = useState('date');
  const [outstandingDir, setOutstandingDir] = useState<'asc' | 'desc'>('desc');
  const [, setOutstandingLoading] = useState(false);
  const [outstandingError, setOutstandingError] = useState<string | null>(null);

  const [saveKind, setSaveKind] = useState<string | null>(null);
  const [exportingSection, setExportingSection] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const pQuery = periodParams(period);

  // 1. Fetch Header & PvP
  useEffect(() => {
    let active = true;
    setHeaderLoading(true);
    setHeaderError(null);

    getVendorFinancialDetail(vendor.key, { ...pQuery, compare: comparePvp })
      .then((res) => {
        if (active) {
          setHeader(res);
          setHeaderLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setHeaderError(err instanceof Error ? err.message : 'خطا در بارگذاری اطلاعات مالی تأمین‌کننده.');
          setHeaderLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [vendor.key, JSON.stringify(pQuery), comparePvp]);

  // 2. Fetch Payment History
  useEffect(() => {
    let active = true;
    setHistoryLoading(true);
    setHistoryError(null);

    getVendorPaymentHistory(vendor.key, {
      ...pQuery,
      page: historyPage,
      pageSize: historyPageSize,
      sort: historySort,
      dir: historyDir,
    })
      .then((res) => {
        if (active) {
          setHistory(res);
          setHistoryLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setHistoryError(err instanceof Error ? err.message : 'خطا در بارگذاری تاریخچه پرداخت‌ها.');
          setHistoryLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [vendor.key, JSON.stringify(pQuery), historyPage, historyPageSize, historySort, historyDir]);

  // 3. Fetch Outstanding Instruments
  useEffect(() => {
    let active = true;
    setOutstandingLoading(true);
    setOutstandingError(null);

    getVendorOutstanding(vendor.key, {
      ...pQuery,
      page: outstandingPage,
      pageSize: outstandingPageSize,
      sort: outstandingSort,
      dir: outstandingDir,
    })
      .then((res) => {
        if (active) {
          setOutstanding(res);
          setOutstandingLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setOutstandingError(err instanceof Error ? err.message : 'خطا در بارگذاری اسناد معوق.');
          setOutstandingLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [vendor.key, JSON.stringify(pQuery), outstandingPage, outstandingPageSize, outstandingSort, outstandingDir]);

  const toggleHistorySort = (key: string) => {
    if (historySort === key) {
      setHistoryDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setHistorySort(key);
      setHistoryDir('desc');
    }
  };

  const toggleOutstandingSort = (key: string) => {
    if (outstandingSort === key) {
      setOutstandingDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setOutstandingSort(key);
      setOutstandingDir('desc');
    }
  };

  const exportPvp = async () => {
    setExportingSection('pvp');
    setExportError(null);
    try {
      await downloadVendorPurchasesVsPaymentsExport(vendor.key, { ...pQuery, compare: comparePvp });
    } catch (e) {
      setExportError(e instanceof Error ? e.message : 'خطا در دریافت فایل اکسل.');
    } finally {
      setExportingSection(null);
    }
  };

  const exportHistory = async () => {
    setExportingSection('history');
    setExportError(null);
    try {
      await downloadVendorHistoryExport(vendor.key, {
        ...pQuery,
        sort: historySort,
        dir: historyDir,
      });
    } catch (e) {
      setExportError(e instanceof Error ? e.message : 'خطا در دریافت فایل اکسل.');
    } finally {
      setExportingSection(null);
    }
  };

  const exportOutstanding = async () => {
    setExportingSection('outstanding');
    setExportError(null);
    try {
      await downloadVendorOutstandingExport(vendor.key, {
        ...pQuery,
        sort: outstandingSort,
        dir: outstandingDir,
      });
    } catch (e) {
      setExportError(e instanceof Error ? e.message : 'خطا در دریافت فایل اکسل.');
    } finally {
      setExportingSection(null);
    }
  };

  const pvp = header?.purchasesVsPayments;
  const isBalanceUnavailable = header?.unavailable.some((u) => u.figure === 'vendorBalance');
  const isOutstandingUnavailable = header?.unavailable.some((u) => u.figure === 'outstandingPayments');
  const isNetPurchasesUnavailable = header?.unavailable.some((u) => u.figure === 'netPurchases');
  const isPaymentsUnavailable = header?.unavailable.some((u) => u.figure === 'payments');
  const isPvpUnavailable =
    header?.unavailable.some((u) => u.figure === 'purchasesMinusPayments') ||
    isNetPurchasesUnavailable ||
    isPaymentsUnavailable;

  return (
    <div className="space-y-6">
      {/* Header and Back navigation */}
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-200 pb-4">
        <div>
          <button
            type="button"
            onClick={onBack}
            className="mb-2 text-sm font-medium text-blue-700 hover:text-blue-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
          >
            ← بازگشت به فهرست مالی تأمین‌کنندگان
          </button>
          <h2 className="text-xl font-bold text-gray-900">
            نمای مالی: {header?.vendor.name ?? vendor.name}
            {header?.vendor.code && <span className="mr-2 text-sm font-normal text-gray-500">(کد: {header.vendor.code})</span>}
          </h2>
        </div>
        <PeriodPicker value={period} onChange={setPeriod} serverToday={serverToday} />
      </header>

      {header && (
        <ReportStatusLine
          envelope={{
            dataLabel: header.dataLabel,
            lastSuccessfulSync: header.lastSuccessfulSync ?? null,
            period: header.period,
            previousPeriod: header.previousPeriod ?? null,
            filters: [],
            unavailable: header.unavailable,
          }}
        />
      )}

      {header?.dataQuality && <DataQualityNotice notice={header.dataQuality} showLink={showLink} />}
      {headerError && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {headerError}
        </p>
      )}
      {exportError && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {exportError}
        </p>
      )}

      {/* KPI Cards: Vendor Balance & Outstanding */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <KpiCard
          label="مانده تأمین‌کننده"
          value={isBalanceUnavailable ? null : (header?.vendorBalance ?? null)}
          note="مثبت: بدهی شرکت به تأمین‌کننده · منفی: بستانکاری شرکت"
        />
        <KpiCard
          label="پرداخت‌های معوق"
          value={isOutstandingUnavailable ? null : (header?.outstandingPayments ?? null)}
          note="مجموع مانده‌های پرداخت‌نشده اسناد و چک‌های باز تا پایان دوره"
        />
      </div>

      {/* Purchases vs Payments Section */}
      <section aria-labelledby="pvp-title" className="rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-100 pb-3">
          <h3 id="pvp-title" className="text-base font-semibold text-gray-900">
            خرید در برابر پرداخت
          </h3>
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
              <input
                type="checkbox"
                checked={comparePvp}
                onChange={(e) => setComparePvp(e.target.checked)}
                className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              مقایسه با دوره قبل
            </label>
            <button
              type="button"
              onClick={() => setSaveKind('vendor-financials-purchases-vs-payments')}
              className="rounded-lg border border-gray-300 bg-white px-2.5 py-1 text-xs font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              ذخیره گزارش
            </button>
            {canExport && (
              <button
                type="button"
                onClick={exportPvp}
                disabled={exportingSection === 'pvp'}
                className="rounded-lg border border-emerald-300 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
              >
                {exportingSection === 'pvp' ? 'در حال دریافت...' : 'خروجی اکسل'}
              </button>
            )}
          </div>
        </div>

        <dl className="mt-4 grid grid-cols-1 gap-4 text-sm sm:grid-cols-3">
          <div className="rounded-lg bg-gray-50 p-3">
            <dt className="text-gray-600">خالص خرید</dt>
            <dd className="mt-1 text-base font-bold text-gray-900">
              {isNetPurchasesUnavailable ? 'داده در دسترس نیست' : faRialFromDecimal(pvp?.netPurchases)}
            </dd>
            {comparePvp && pvp?.previousNetPurchases && (
              <p className="mt-1 text-xs text-gray-500">
                دوره قبل: {faRialFromDecimal(pvp.previousNetPurchases)} ({faPercent(pvp.percentNetPurchases)})
              </p>
            )}
          </div>
          <div className="rounded-lg bg-gray-50 p-3">
            <dt className="text-gray-600">پرداخت‌ها</dt>
            <dd className="mt-1 text-base font-bold text-gray-900">
              {isPaymentsUnavailable ? 'داده در دسترس نیست' : faRialFromDecimal(pvp?.payments)}
            </dd>
            {comparePvp && pvp?.previousPayments && (
              <p className="mt-1 text-xs text-gray-500">
                دوره قبل: {faRialFromDecimal(pvp.previousPayments)} ({faPercent(pvp.percentPayments)})
              </p>
            )}
          </div>
          <div className="rounded-lg bg-gray-50 p-3">
            <dt className="text-gray-600">خرید منهای پرداخت</dt>
            <dd className="mt-1 text-base font-bold text-gray-900">
              {isPvpUnavailable
                ? 'داده در دسترس نیست'
                : pvp?.purchasesMinusPayments
                  ? faRialFromDecimal(pvp.purchasesMinusPayments, { signed: true })
                  : '—'}
            </dd>
            <p className="mt-1 text-xs text-gray-500">صرفاً تفاضل خرید خالص و پرداخت‌ها</p>
          </div>
        </dl>
      </section>

      {/* Payment History Section */}
      <section aria-labelledby="history-title" className="space-y-4 rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-100 pb-3">
          <div>
            <h3 id="history-title" className="text-base font-semibold text-gray-900">
              تاریخچه پرداخت‌ها
            </h3>
            <p className="text-xs text-gray-500">مبالغ پرداخت‌شده به این تأمین‌کننده در طول دوره</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setSaveKind('vendor-financials-history')}
              className="rounded-lg border border-gray-300 bg-white px-2.5 py-1 text-xs font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              ذخیره گزارش
            </button>
            {canExport && (
              <button
                type="button"
                onClick={exportHistory}
                disabled={exportingSection === 'history'}
                className="rounded-lg border border-emerald-300 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
              >
                {exportingSection === 'history' ? 'در حال دریافت...' : 'خروجی اکسل تاریخچه'}
              </button>
            )}
          </div>
        </div>

        {historyError && (
          <p role="alert" className="text-sm text-red-700">
            {historyError}
          </p>
        )}

        {history && (
          <>
            <Table
              caption="تاریخچه پرداخت‌های تأمین‌کننده"
              columns={historyColumns}
              rows={history.rows}
              rowKey={(r) => `${r.paidAtJalali}-${r.reference ?? ''}-${r.amount}`}
              sort={historySort}
              dir={historyDir}
              onSort={toggleHistorySort}
              emptyText="پرداختی در این دوره برای این تأمین‌کننده ثبت نشده است."
            />
            <Pager
              page={historyPage}
              pageSize={historyPageSize}
              totalRows={history.totalRows}
              onPage={setHistoryPage}
              onPageSize={(sz) => {
                setHistoryPageSize(sz);
                setHistoryPage(1);
              }}
            />
          </>
        )}
      </section>

      {/* Outstanding Instruments Section */}
      <section aria-labelledby="outstanding-title" className="space-y-4 rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-100 pb-3">
          <div>
            <h3 id="outstanding-title" className="text-base font-semibold text-gray-900">
              اسناد و پرداخت‌های معوق
            </h3>
            <p className="text-xs text-gray-500">اسناد پرداختنی و چک‌های سررسیدنشده یا تسویه‌نشده تا پایان دوره</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setSaveKind('vendor-financials-outstanding')}
              className="rounded-lg border border-gray-300 bg-white px-2.5 py-1 text-xs font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              ذخیره گزارش
            </button>
            {canExport && (
              <button
                type="button"
                onClick={exportOutstanding}
                disabled={exportingSection === 'outstanding'}
                className="rounded-lg border border-emerald-300 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
              >
                {exportingSection === 'outstanding' ? 'در حال دریافت...' : 'خروجی اکسل معوق'}
              </button>
            )}
          </div>
        </div>

        {outstandingError && (
          <p role="alert" className="text-sm text-red-700">
            {outstandingError}
          </p>
        )}

        {outstanding && (
          <>
            <Table
              caption="اسناد و پرداخت‌های معوق تأمین‌کننده"
              columns={outstandingColumns}
              rows={outstanding.rows}
              rowKey={(r) => `${r.dateJalali}-${r.reference ?? ''}-${r.unpaidAmount}`}
              sort={outstandingSort}
              dir={outstandingDir}
              onSort={toggleOutstandingSort}
              emptyText="سند معوقی برای این تأمین‌کننده در این بازه وجود ندارد."
            />
            <Pager
              page={outstandingPage}
              pageSize={outstandingPageSize}
              totalRows={outstanding.totalRows}
              onPage={setOutstandingPage}
              onPageSize={(sz) => {
                setOutstandingPageSize(sz);
                setOutstandingPage(1);
              }}
            />
          </>
        )}
      </section>

      {/* Save Report Dialog */}
      {saveKind && (
        <SaveReportDialog
          open={Boolean(saveKind)}
          onClose={() => setSaveKind(null)}
          defaultName={
            saveKind === 'vendor-financials-history'
              ? `تاریخچه پرداخت‌ها - ${header?.vendor.name ?? vendor.name}`
              : saveKind === 'vendor-financials-outstanding'
                ? `پرداخت‌های معوق - ${header?.vendor.name ?? vendor.name}`
                : `خرید در برابر پرداخت - ${header?.vendor.name ?? vendor.name}`
          }
          kind={saveKind}
          period={canonicalPeriod(period)}
          filters={{ vendor: vendor.key, compare: saveKind === 'vendor-financials-purchases-vs-payments' ? comparePvp : false }}
        />
      )}
    </div>
  );
}
