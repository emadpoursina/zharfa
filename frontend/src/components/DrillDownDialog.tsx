import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import {
  getPurchasing,
  downloadReportExport,
  type DocumentLines,
  type DocumentRow,
  type PurchasingQueryValue,
  type ReportDrill,
  type ReportTable,
} from '../services/api';
import { DataQualityNotice } from './DataQualityNotice';
import { faQty, faRialFromDecimal } from '../utils/format';
import { Pager } from './Pager';
import { SaveReportDialog } from './SaveReportDialog';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from './ReportTable';
import { UnavailableNote } from './UnavailableNote';
import type { PageSize } from '../hooks/useReportQuery';

interface DrillDownDialogProps {
  open: boolean;
  title: string;
  /** Period and the screen's vendor, product and category filters (no paging, no search). */
  baseParams: Record<string, PurchasingQueryValue>;
  /** The row's drill-down filters (contracts/api.md §3). */
  drill: ReportDrill | null;
  onClose: () => void;
  showLink?: boolean;
  canExport?: boolean;
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

function drillParams(drill: ReportDrill): Record<string, PurchasingQueryValue> {
  const params: Record<string, PurchasingQueryValue> = {};
  if (drill.vendor) params.vendor = drill.vendor;
  if (drill.product) params.product = drill.product;
  if (drill.category) params.category = drill.category;
  if (drill.month) params.month = drill.month;
  if (drill.match) params.match = drill.match;
  if (drill.docType) params.docType = drill.docType;
  return params;
}

/**
 * Documents behind one report row (spec 004 T057): a modal dialog with focus trap, Escape to close and focus returned to
 * the opening control. The list is server-paged; each document expands to its in-scope lines. The dialog states the
 * period and filters it applies.
 */
function drillBaseToCanonicalPeriod(params: Record<string, PurchasingQueryValue>) {
  const kind = (params.kind as string) || (params.period as string) || 'month';
  if (kind === 'custom') {
    return { kind: 'custom', from: String(params.from || ''), to: String(params.to || '') };
  }
  if (kind === 'quarter') {
    return { kind: 'quarter', quarter: String(params.quarter || '') };
  }
  if (kind === 'year') {
    return { kind: 'year', year: String(params.year || '') };
  }
  return { kind: 'month', month: String(params.month || '') };
}

export function DrillDownDialog({ open, title, baseParams, drill, onClose, showLink, canExport }: DrillDownDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const openerRef = useRef<HTMLElement | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(25);
  const [data, setData] = useState<ReportTable<DocumentRow> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [lines, setLines] = useState<DocumentLines | null>(null);
  const [linesError, setLinesError] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const exportNow = async () => {
    setExporting(true);
    setExportError(null);
    try {
      const combinedParams = {
        ...baseParams,
        ...(drill ? drillParams(drill) : {}),
      } as Record<string, string>;
      await downloadReportExport('documents', combinedParams);
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'دریافت خروجی اکسل انجام نشد.');
    } finally {
      setExporting(false);
    }
  };

  // Remember the opener and focus the dialog's first control; restore focus on close.
  useEffect(() => {
    if (!open) {
      return;
    }
    openerRef.current = document.activeElement as HTMLElement | null;
    setPage(1);
    setExpandedId(null);
    const first = dialogRef.current?.querySelector<HTMLElement>(FOCUSABLE);
    first?.focus();
    return () => {
      openerRef.current?.focus();
    };
  }, [open]);

  const params = { ...baseParams, ...(drill ? drillParams(drill) : {}), page, pageSize, sort: 'date', dir: 'desc' as const };
  const paramKey = JSON.stringify(params);

  useEffect(() => {
    if (!open) {
      return;
    }
    let active = true;
    setLoading(true);
    setError(null);
    getPurchasing<ReportTable<DocumentRow>>('documents', JSON.parse(paramKey) as Record<string, PurchasingQueryValue>)
      .then((response) => {
        if (active) {
          setData(response);
          setLoading(false);
          if (response.page !== page) {
            setPage(response.page);
          }
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
    // The key already encodes every parameter; `page` is covered by it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, paramKey]);

  useEffect(() => {
    if (expandedId === null) {
      setLines(null);
      return;
    }
    let active = true;
    setLinesError(null);
    const lineParams: Record<string, PurchasingQueryValue> = {};
    const product = drill?.product ?? baseParams.product;
    const category = drill?.category ?? baseParams.category;
    if (product) lineParams.product = product;
    if (category) lineParams.category = category;
    if (drill?.match) lineParams.match = drill.match;
    getPurchasing<DocumentLines>(`documents/${expandedId}/lines`, lineParams)
      .then((response) => {
        if (active) setLines(response);
      })
      .catch((caught: unknown) => {
        if (active) setLinesError(caught instanceof Error ? caught.message : 'خطایی رخ داد.');
      });
    return () => {
      active = false;
    };
  }, [expandedId, drill, baseParams.product, baseParams.category]);

  if (!open) {
    return null;
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      onClose();
      return;
    }
    if (event.key !== 'Tab' || !dialogRef.current) {
      return;
    }
    const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE));
    if (focusable.length === 0) {
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  const columns: ReportColumn<DocumentRow>[] = [
    { id: 'number', label: 'شماره سند', render: (row) => row.number },
    { id: 'date', label: 'تاریخ', render: (row) => row.dateJalali },
    { id: 'type', label: 'نوع سند', render: (row) => row.docTypeLabel },
    { id: 'vendor', label: 'تأمین‌کننده', render: (row) => row.vendorName },
    { id: 'net', label: 'خالص', numeric: true, render: (row) => faRialFromDecimal(row.signedNet), footer: () => faRialFromDecimal(data?.totals.net) },
    { id: 'qty', label: 'تعداد (واحد ثبت‌شده)', numeric: true, render: (row) => faQty(row.quantity) },
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-gray-900/50 p-4">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="drill-title"
        dir="rtl"
        onKeyDown={onKeyDown}
        className="flex max-h-[90vh] w-full max-w-5xl flex-col gap-4 overflow-hidden rounded-xl bg-white p-5 shadow-xl"
      >
        <header className="flex items-start justify-between gap-4">
          <div>
            <h2 id="drill-title" className="text-lg font-bold text-gray-900">
              {title}
            </h2>
            <p className="text-sm text-gray-600">اسناد پشت این ردیف، با همان دوره و فیلترهای گزارش</p>
          </div>
          <div className="flex gap-2">
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
              onClick={onClose}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              بستن
            </button>
          </div>
        </header>
        {exportError && <p role="alert" className="text-sm text-red-700 px-1">{exportError}</p>}

        <div className="space-y-4 overflow-y-auto">
          {data && <ReportStatusLine envelope={data} />}
          <DataQualityNotice notice={data?.dataQuality} showLink={Boolean(showLink)} />
          {error && (
            <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
              {error}
            </p>
          )}
          {data && data.unavailable.length > 0 && <UnavailableNote items={data.unavailable} />}
          {loading && !data && <p className="text-sm text-gray-600">در حال بارگذاری...</p>}

          {data && (
            <>
              <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                <div>
                  <dt className="text-gray-600">خالص همه اسناد</dt>
                  <dd className="font-semibold text-gray-900">{faRialFromDecimal(data.totals.net)}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">فاکتور خرید</dt>
                  <dd className="font-semibold text-gray-900">{faRialFromDecimal(data.totals.invoiceAmount)}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">برگشت از خرید</dt>
                  <dd className="font-semibold text-gray-900">{faRialFromDecimal(data.totals.returnAmount)}</dd>
                </div>
                <div>
                  <dt className="text-gray-600">تعداد خالص</dt>
                  <dd className="font-semibold text-gray-900">{faQty(data.totals.netQuantity)}</dd>
                </div>
              </dl>

              <Table
                caption="اسناد ردیف گزارش"
                columns={columns}
                rows={data.rows}
                rowKey={(row) => String(row.documentId)}
                sort="date"
                dir="desc"
                onSort={() => undefined}
                isUnknown={(row) => row.vendorKey === null}
                rowActionLabel="ردیف‌ها"
                rowAction={(row) => (
                  <button
                    type="button"
                    aria-expanded={expandedId === row.documentId}
                    onClick={() => setExpandedId(expandedId === row.documentId ? null : row.documentId)}
                    className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                  >
                    {expandedId === row.documentId ? 'بستن ردیف‌ها' : 'نمایش ردیف‌ها'}
                  </button>
                )}
                expandedKey={expandedId === null ? null : String(expandedId)}
                renderExpanded={() => <LinesBlock lines={lines} error={linesError} />}
              />

              <Pager
                page={data.page}
                pageSize={pageSize}
                totalRows={data.totalRows}
                onPage={setPage}
                onPageSize={(size) => {
                  setPageSize(size);
                  setPage(1);
                }}
                disabled={loading}
              />
            </>
          )}
        </div>
      </div>

      {saveOpen && (
        <SaveReportDialog
          open={saveOpen}
          kind="drill-down"
          defaultName={title}
          period={drillBaseToCanonicalPeriod(baseParams)}
          filters={drill ? drillParams(drill) : {}}
          onClose={() => setSaveOpen(false)}
        />
      )}
    </div>
  );
}

function LinesBlock({ lines, error }: { lines: DocumentLines | null; error: string | null }) {
  if (error) {
    return <p className="text-sm text-red-700">{error}</p>;
  }
  if (!lines) {
    return <p className="text-sm text-gray-600">در حال بارگذاری ردیف‌ها...</p>;
  }
  if (lines.lines.length === 0) {
    return <p className="text-sm text-gray-600">ردیفی از این سند در فیلترهای فعال نیست.</p>;
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <caption className="sr-only">ردیف‌های سند {lines.document.number}</caption>
        <thead>
          <tr className="text-gray-700">
            <th scope="col" className="px-2 py-1 text-right">کالا</th>
            <th scope="col" className="px-2 py-1 text-left">تعداد</th>
            <th scope="col" className="px-2 py-1 text-left">مبلغ پیش از تخفیف</th>
            <th scope="col" className="px-2 py-1 text-left">تخفیف</th>
            <th scope="col" className="px-2 py-1 text-left">خالص ردیف</th>
            <th scope="col" className="px-2 py-1 text-left">علامت‌دار</th>
          </tr>
        </thead>
        <tbody>
          {lines.lines.map((line) => (
            <tr key={line.lineId} className={line.unknownProduct ? 'italic text-amber-900' : undefined}>
              <td className="px-2 py-1 text-right">{line.productName}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faQty(line.quantity)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.preDiscount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.discount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.lineAmount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.signedAmount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-2 text-xs text-gray-600">
        جمع ردیف‌ها: {faRialFromDecimal(lines.totals.signedAmount)} (علامت‌دار)
      </p>
    </div>
  );
}
