import type { ReactNode } from 'react';
import type { ReportEnvelope, ReportPrint, VendorComparison } from '../services/api';
import { faNum, faRialFromDecimal } from '../utils/format';
import { DataQualityNotice } from './DataQualityNotice';
import { DataLabelBadge, type ReportColumn } from './ReportTable';

interface PrintReportProps<TRow> {
  /** Report name in the header. */
  title: string;
  /** The print response; the component renders nothing until it is loaded. */
  print: ReportPrint<TRow> | null;
  columns: ReportColumn<TRow>[];
  rowKey: (row: TRow) => string;
  isUnknown?: (row: TRow) => boolean;
  /** Landscape A4 for wide reports (transactions). */
  landscape?: boolean;
  /** Extra header note (for example the vendor's name). */
  subtitle?: string;
}

const TRUNCATION_NOTE = 'فقط ۱٬۰۰۰ ردیف اول چاپ شده است؛ برای چاپ کامل، بازه یا فیلترها را کوچک‌تر کنید.';

function PrintHeader({ envelope, title, subtitle }: { envelope: ReportEnvelope; title: string; subtitle?: string }) {
  return (
    <header className="space-y-1 border-b border-gray-400 pb-2">
      <h1 className="text-lg font-bold">{title}</h1>
      {subtitle && <p className="text-sm">{subtitle}</p>}
      <p className="text-sm">
        دوره: {envelope.period.label}
        {envelope.previousPeriod ? ` — دوره قبل: ${envelope.previousPeriod.label}` : ''}
      </p>
      {envelope.filters.length > 0 && (
        <p className="text-sm">فیلترها: {envelope.filters.map((filter) => filter.label).join('، ')}</p>
      )}
      {envelope.dataQuality && (
        <div className="pt-1">
          <DataQualityNotice notice={envelope.dataQuality} showLink={false} />
        </div>
      )}
      <p className="flex flex-wrap items-center gap-3 text-xs">
        <DataLabelBadge dataLabel={envelope.dataLabel} />
        <span>آخرین همگام‌سازی موفق: {envelope.lastSuccessfulSync?.atJalali ?? 'هنوز انجام نشده'}</span>
      </p>
    </header>
  );
}

/**
 * Hidden print container for a report's print twin (spec 004 T068): header with the period, comparison period and
 * every active filter, the data label and last sync, the full table with a repeated header row, the totals, and the
 * truncation notice. Visible only in print media (index.css).
 */
export function PrintReport<TRow>({ title, print, columns, rowKey, isUnknown, landscape = false, subtitle }: PrintReportProps<TRow>) {
  if (!print) {
    return null;
  }
  const hasFooter = columns.some((column) => column.footer !== undefined);
  return (
    <section className={`print-only report-print ${landscape ? 'print-landscape' : ''}`} dir="rtl" aria-hidden="true">
      <PrintHeader envelope={print} title={title} subtitle={subtitle} />
      <table className="mt-3 w-full border-collapse text-xs">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.id} className="border border-gray-500 px-1 py-1 text-right">
                {column.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {print.rows.map((row) => (
            <tr key={rowKey(row)} className={isUnknown?.(row) ? 'italic' : undefined}>
              {columns.map((column) => (
                <td key={column.id} className={`border border-gray-400 px-1 py-0.5 align-top ${column.numeric ? 'text-left' : 'text-right'}`}>
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
        {hasFooter && (
          <tfoot>
            <tr className="font-bold">
              {columns.map((column, index) => (
                <td key={column.id} className="border border-gray-500 px-1 py-1 text-right">
                  {index === 0 ? 'جمع کل' : column.footer?.() ?? ''}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
      <p className="mt-2 text-xs">
        {faNum(print.rows.length)} ردیف از {faNum(print.totalRows)} ردیف
      </p>
      {print.truncated && <p className="mt-1 text-xs font-semibold">{TRUNCATION_NOTE}</p>}
    </section>
  );
}

/**
 * Print layout of the vendor comparison (spec 004 T068): five measures per vendor and a month × vendor net table.
 */
export function PrintVendorComparison({
  comparison,
  title,
}: {
  comparison: VendorComparison | null;
  title: string;
}): ReactNode {
  if (!comparison) {
    return null;
  }
  return (
    <section className="print-only report-print" dir="rtl" aria-hidden="true">
      <PrintHeader envelope={comparison} title={title} />
      <table className="mt-3 w-full border-collapse text-xs">
        <thead>
          <tr>
            <th className="border border-gray-500 px-1 py-1 text-right">تأمین‌کننده</th>
            <th className="border border-gray-500 px-1 py-1 text-right">خالص</th>
            <th className="border border-gray-500 px-1 py-1 text-right">تعداد خالص</th>
            <th className="border border-gray-500 px-1 py-1 text-right">تعداد اسناد</th>
            <th className="border border-gray-500 px-1 py-1 text-right">برگشت از خرید</th>
          </tr>
        </thead>
        <tbody>
          {comparison.vendors.map((vendor) => (
            <tr key={vendor.key}>
              <td className="border border-gray-400 px-1 py-0.5 text-right">{vendor.name}</td>
              <td className="border border-gray-400 px-1 py-0.5 text-left">{faRialFromDecimal(vendor.net)}</td>
              <td className="border border-gray-400 px-1 py-0.5 text-left">{vendor.netQuantity ?? '—'}</td>
              <td className="border border-gray-400 px-1 py-0.5 text-left">{vendor.documentCount ?? '—'}</td>
              <td className="border border-gray-400 px-1 py-0.5 text-left">{faRialFromDecimal(vendor.returns)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <h2 className="mt-4 text-sm font-semibold">روند خالص ماهانه</h2>
      <table className="mt-1 w-full border-collapse text-xs">
        <thead>
          <tr>
            <th className="border border-gray-500 px-1 py-1 text-right">ماه</th>
            {comparison.vendors.map((vendor) => (
              <th key={vendor.key} className="border border-gray-500 px-1 py-1 text-right">
                {vendor.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {comparison.months.map((month, index) => (
            <tr key={month}>
              <td className="border border-gray-400 px-1 py-0.5 text-right">{month}</td>
              {comparison.vendors.map((vendor) => {
                const point = vendor.trend[index];
                return (
                  <td key={vendor.key} className="border border-gray-400 px-1 py-0.5 text-left">
                    {point && point.available && point.net !== null ? faRialFromDecimal(point.net) : 'داده در دسترس نیست'}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      {comparison.truncated && <p className="mt-1 text-xs font-semibold">{TRUNCATION_NOTE}</p>}
    </section>
  );
}
