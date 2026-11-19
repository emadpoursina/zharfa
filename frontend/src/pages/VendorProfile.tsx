import { useEffect, useState } from 'react';
import {
  getPurchasing,
  downloadReportExport,
  type DocumentRow,
  type ReportPrint,
  type ReportTable,
  type VendorProfile as VendorProfileData,
} from '../services/api';
import { canonicalPeriod, periodParams, type PageSize, type PeriodValue } from '../hooks/useReportQuery';
import { DataQualityNotice } from '../components/DataQualityNotice';
import { DocumentLinesPanel } from '../components/DocumentLinesPanel';
import { KpiCard } from '../components/KpiCard';
import { PeriodPicker } from '../components/PeriodPicker';
import { Pager } from '../components/Pager';
import { PrintReport } from '../components/PrintReport';
import { SaveReportDialog } from '../components/SaveReportDialog';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from '../components/ReportTable';
import { TrendChart } from '../components/TrendChart';
import { UnavailableNote } from '../components/UnavailableNote';
import { faPercent, faQty, faRialFromDecimal } from '../utils/format';
import { toLatinDigits } from '../utils/jalali';

interface VendorProfileProps {
  vendor: { key: string; name: string };
  serverToday: string;
  /** The period the profile opens with (the Overview's month, or the directory's period). */
  initialPeriod: PeriodValue;
  onBack: () => void;
  showLink?: boolean;
  canExport?: boolean;
}

const documentColumns: ReportColumn<DocumentRow>[] = [
  { id: 'number', label: 'شماره سند', render: (row) => row.number },
  { id: 'date', label: 'تاریخ', render: (row) => row.dateJalali },
  { id: 'type', label: 'نوع سند', render: (row) => row.docTypeLabel },
  { id: 'pre', label: 'پیش از تخفیف', numeric: true, render: (row) => faRialFromDecimal(row.preDiscount) },
  { id: 'discount', label: 'تخفیف', numeric: true, render: (row) => faRialFromDecimal(row.discount) },
  { id: 'vat', label: 'ارزش افزوده', numeric: true, render: (row) => faRialFromDecimal(row.vat) },
  { id: 'net', label: 'خالص (علامت‌دار)', numeric: true, render: (row) => faRialFromDecimal(row.signedNet) },
  { id: 'qty', label: 'تعداد', numeric: true, render: (row) => faQty(row.quantity) },
];

/**
 * The vendor profile (spec 004 T046, US1): header, period, KPI cards (net, gross, returns, VAT split), the comparison
 * with the previous period (both labels), the 12-month trend, top products, returns, and the paged documents whose rows
 * expand to their lines. No balance, payment or credit figure exists here (FR-012).
 */
export function VendorProfile({ vendor, serverToday, initialPeriod, onBack, showLink, canExport = false }: VendorProfileProps) {
  const [period, setPeriod] = useState<PeriodValue>(initialPeriod);
  const [profile, setProfile] = useState<VendorProfileData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [docPage, setDocPage] = useState(1);
  const [docPageSize, setDocPageSize] = useState<PageSize>(25);
  const [docs, setDocs] = useState<ReportTable<DocumentRow> | null>(null);
  const [docError, setDocError] = useState<string | null>(null);
  const [expandedDoc, setExpandedDoc] = useState<number | null>(null);
  const [printDocs, setPrintDocs] = useState<ReportPrint<DocumentRow> | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const exportNow = async () => {
    setExporting(true);
    setExportError(null);
    try {
      await downloadReportExport('vendor-profile', {
        vendor: vendor.key,
        table: 'documents',
        ...periodQuery,
      });
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'دریافت خروجی اکسل انجام نشد.');
    } finally {
      setExporting(false);
    }
  };

  const periodQuery = periodParams(period);
  const periodKey = JSON.stringify(periodQuery);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    getPurchasing<VendorProfileData>('vendor-profile', { vendor: vendor.key, ...(JSON.parse(periodKey) as Record<string, string>) })
      .then((response) => {
        if (active) {
          setProfile(response);
          setLoading(false);
        }
      })
      .catch((caught: unknown) => {
        if (active) {
          setError(caught instanceof Error ? caught.message : 'خطایی رخ داد.');
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [vendor.key, periodKey]);

  useEffect(() => {
    let active = true;
    setDocError(null);
    getPurchasing<ReportTable<DocumentRow>>('documents', {
      vendor: vendor.key,
      ...(JSON.parse(periodKey) as Record<string, string>),
      sort: 'date',
      dir: 'desc',
      page: docPage,
      pageSize: docPageSize,
    })
      .then((response) => {
        if (active) {
          setDocs(response);
          if (response.page !== docPage) setDocPage(response.page);
        }
      })
      .catch((caught: unknown) => {
        if (active) setDocError(caught instanceof Error ? caught.message : 'خطایی رخ داد.');
      });
    return () => {
      active = false;
    };
  }, [vendor.key, periodKey, docPage, docPageSize]);

  const printDocuments = async () => {
    setPrintError(null);
    try {
      const response = await getPurchasing<ReportPrint<DocumentRow>>('documents/print', {
        vendor: vendor.key,
        ...(JSON.parse(periodKey) as Record<string, string>),
        sort: 'date',
        dir: 'desc',
      });
      setPrintDocs(response);
      window.setTimeout(() => window.print(), 50);
    } catch (caught: unknown) {
      setPrintError(caught instanceof Error ? caught.message : 'چاپ انجام نشد.');
    }
  };

  const selectedMonth = profile ? toLatinDigits(profile.period.to).slice(0, 7) : '';

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <button
            type="button"
            onClick={onBack}
            className="text-sm font-medium text-blue-800 hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
          >
            ← بازگشت به فهرست تأمین‌کنندگان
          </button>
          <h2 className="text-xl font-bold text-gray-900">{profile?.vendor.name ?? vendor.name}</h2>
          <p className="text-sm text-gray-600">
            کد تأمین‌کننده: {profile ? profile.vendor.code ?? 'داده در دسترس نیست' : '—'}
          </p>
        </div>
      </div>

      <section aria-label="دوره پروفایل" className="flex flex-wrap items-end justify-between gap-4 rounded-xl border border-gray-200 bg-white p-4 shadow-xs">
        <PeriodPicker
          value={period}
          serverToday={serverToday}
          onChange={(next) => {
            setPeriod(next);
            setDocPage(1);
          }}
          disabled={loading}
        />
      </section>

      {error && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </p>
      )}
      {loading && !profile && <p className="text-sm text-gray-600">در حال بارگذاری پروفایل...</p>}

      {profile && (
        <>
          <ReportStatusLine envelope={profile} />
          <DataQualityNotice notice={profile.dataQuality} showLink={Boolean(showLink)} />

          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
            <KpiCard label="خالص خرید" value={profile.totals.net ?? null} note={`تعداد اسناد: ${faQty(profile.totals.documentCount ?? null)}`} />
            <KpiCard label="خرید ناخالص" value={profile.totals.gross ?? null} />
            <KpiCard label="برگشت از خرید" value={profile.totals.returns ?? null} />
            <KpiCard
              label="ارزش افزوده"
              value={profile.totals.vat ?? null}
              note={`فاکتورها: ${faRialFromDecimal(profile.totals.vatInvoice)} · برگشت‌ها: ${faRialFromDecimal(profile.totals.vatReturn)}`}
            />
          </div>

          <section aria-labelledby="comparison-title" className="rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
            <h3 id="comparison-title" className="text-base font-semibold text-gray-900">
              مقایسه با دوره قبل
            </h3>
            {profile.comparison ? (
              <dl className="mt-3 grid grid-cols-1 gap-3 text-sm sm:grid-cols-4">
                <div>
                  <dt className="text-gray-600">دوره انتخابی</dt>
                  <dd className="font-semibold text-gray-900">{profile.period.label}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">دوره قبل ({profile.previousPeriod?.label ?? ''})</dt>
                  <dd className="font-semibold text-gray-900">{faRialFromDecimal(profile.comparison.previousNet)}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">تفاوت</dt>
                  <dd className="font-semibold text-gray-900">{faRialFromDecimal(profile.comparison.delta, { signed: true })}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">درصد تغییر</dt>
                  <dd className="font-semibold text-gray-900">
                    {profile.comparison.percent === null ? '— (پایه صفر)' : faPercent(profile.comparison.percent)}
                  </dd>
                </div>
              </dl>
            ) : (
              <UnavailableNote items={[{ figure: 'comparison', reason: 'no-previous-data' }]} />
            )}
          </section>

          <TrendChart
            points={profile.trend.map((point) => ({ month: point.month, net: point.net, available: point.available }))}
            selectedMonth={selectedMonth}
          />

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <section aria-labelledby="top-products-title" className="rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
              <h3 id="top-products-title" className="text-base font-semibold text-gray-900">
                کالاهای پرخرید (سقف ۱۰ مورد)
              </h3>
              {profile.topProducts.length === 0 ? (
                <p className="mt-3 text-sm text-gray-600">کالایی در این دوره خریداری نشده است.</p>
              ) : (
                <ol className="mt-3 divide-y divide-gray-100">
                  {profile.topProducts.map((product, index) => (
                    <li key={`${product.key ?? 'unknown'}-${index}`} className="flex items-center justify-between gap-3 py-2 text-sm">
                      <span className={product.unknown ? 'italic text-amber-900' : 'text-gray-800'}>{product.name}</span>
                      <span className="shrink-0 font-semibold tabular-nums text-gray-900">{faRialFromDecimal(product.net)}</span>
                    </li>
                  ))}
                </ol>
              )}
            </section>

            <section aria-labelledby="returns-title" className="rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
              <h3 id="returns-title" className="text-base font-semibold text-gray-900">
                برگشت از خرید به تفکیک کالا
              </h3>
              {profile.returns.length === 0 ? (
                <p className="mt-3 text-sm text-gray-600">برگشتی در این دوره ثبت نشده است.</p>
              ) : (
                <ul className="mt-3 divide-y divide-gray-100">
                  {profile.returns.map((item, index) => (
                    <li key={`${item.productKey ?? 'unknown'}-${index}`} className="flex items-center justify-between gap-3 py-2 text-sm">
                      <span className={item.unknown ? 'italic text-amber-900' : 'text-gray-800'}>{item.productName}</span>
                      <span className="shrink-0 tabular-nums text-gray-900">
                        {faRialFromDecimal(item.returns)} · {faQty(item.quantity)}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        </>
      )}

      <section aria-labelledby="documents-title" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h3 id="documents-title" className="text-base font-semibold text-gray-900">
            اسناد تأمین‌کننده در این دوره
          </h3>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setSaveOpen(true)}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              ذخیره گزارش
            </button>
            {canExport && (
              <button
                type="button"
                onClick={exportNow}
                disabled={exporting}
                className="rounded-lg border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-sm font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
              >
                {exporting ? 'در حال دریافت...' : 'خروجی اکسل'}
              </button>
            )}
            <button
              type="button"
              onClick={printDocuments}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              چاپ اسناد
            </button>
          </div>
        </div>
        {exportError && <p role="alert" className="text-sm text-red-700">{exportError}</p>}
        {printError && <p role="alert" className="text-sm text-red-700">{printError}</p>}
        {docError && <p role="alert" className="text-sm text-red-700">{docError}</p>}

        {saveOpen && (
          <SaveReportDialog
            open={saveOpen}
            kind="vendor-profile"
            defaultName={`اسناد ${vendor.name}`}
            period={canonicalPeriod(period)}
            filters={{ vendor: vendor.key, profileTable: 'documents' }}
            onClose={() => setSaveOpen(false)}
          />
        )}
        {docs && (
          <>
            <Table
              caption="اسناد تأمین‌کننده"
              columns={documentColumns}
              rows={docs.rows}
              rowKey={(row) => String(row.documentId)}
              sort="date"
              dir="desc"
              onSort={() => undefined}
              isUnknown={(row) => row.vendorKey === null}
              rowActionLabel="ردیف‌ها"
              rowAction={(row) => (
                <button
                  type="button"
                  aria-expanded={expandedDoc === row.documentId}
                  onClick={() => setExpandedDoc(expandedDoc === row.documentId ? null : row.documentId)}
                  className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                >
                  {expandedDoc === row.documentId ? 'بستن' : 'ردیف‌ها'}
                </button>
              )}
              expandedKey={expandedDoc === null ? null : String(expandedDoc)}
              renderExpanded={(row) => <DocumentLinesPanel documentId={row.documentId} all />}
              footerLabel="جمع کل همه اسناد این دوره"
            />
            <Pager
              page={docs.page}
              pageSize={docPageSize}
              totalRows={docs.totalRows}
              onPage={setDocPage}
              onPageSize={(size) => {
                setDocPageSize(size);
                setDocPage(1);
              }}
            />
          </>
        )}
      </section>

      <PrintReport title={`اسناد ${profile?.vendor.name ?? vendor.name}`} print={printDocs} columns={documentColumns} rowKey={(row) => String(row.documentId)} />
    </div>
  );
}
