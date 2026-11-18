import type { ReactNode } from 'react';
import type { DataLabel, ReportEnvelope } from '../services/api';
import { SortHeader } from './SortHeader';
import { UnavailableNote } from './UnavailableNote';

export interface ReportColumn<TRow> {
  id: string;
  label: string;
  /** Sort key of the report's allowlist; omitted = not sortable. */
  sortKey?: string;
  /** Numeric columns are right-aligned with tabular digits. */
  numeric?: boolean;
  render: (row: TRow) => ReactNode;
  /** The column's cell of the totals footer (over every filtered row, not the page). */
  footer?: () => ReactNode;
}

export interface ReportTableProps<TRow> {
  caption: string;
  columns: ReportColumn<TRow>[];
  rows: TRow[];
  rowKey: (row: TRow) => string;
  sort: string;
  dir: 'asc' | 'desc';
  onSort: (key: string) => void;
  /** Rows of the unknown bucket («نامشخص») are styled apart. */
  isUnknown?: (row: TRow) => boolean;
  /** Extra cell at the end of each row (drill-down button, expand control). */
  rowAction?: (row: TRow) => ReactNode;
  /** Header of the extra cell; empty string = no visible text. */
  rowActionLabel?: string;
  /** Rendered below the row when `expandedKey` equals the row key. */
  expandedKey?: string | null;
  renderExpanded?: (row: TRow) => ReactNode;
  footerLabel?: string;
  emptyText?: string;
}

const DEFAULT_FOOTER_LABEL = 'جمع کل همه ردیف‌های فیلترشده (نه فقط این صفحه)';

/**
 * A server-paged table (spec 004 T040): sortable headers with `aria-sort`, an unknown-row style, an empty state (not an
 * error), an optional row action or expansion, and a totals footer over the full filtered result.
 */
export function ReportTable<TRow>({
  caption,
  columns,
  rows,
  rowKey,
  sort,
  dir,
  onSort,
  isUnknown,
  rowAction,
  rowActionLabel = '',
  expandedKey = null,
  renderExpanded,
  footerLabel = DEFAULT_FOOTER_LABEL,
  emptyText = 'ردیفی برای این فیلترها یافت نشد.',
}: ReportTableProps<TRow>) {
  const hasFooter = columns.some((column) => column.footer !== undefined);
  const colSpan = columns.length + (rowAction ? 1 : 0);

  return (
    <div className="overflow-x-auto rounded-xl border border-gray-200 bg-white shadow-xs">
      <table className="min-w-full text-sm border-collapse">
        <caption className="sr-only">{caption}</caption>
        <thead className="bg-gray-50 border-b border-gray-200">
          <tr>
            {columns.map((column) =>
              column.sortKey ? (
                <SortHeader
                  key={column.id}
                  label={column.label}
                  sortKey={column.sortKey}
                  activeSort={sort}
                  dir={dir}
                  onSort={onSort}
                  align={column.numeric ? 'end' : 'start'}
                />
              ) : (
                <th
                  key={column.id}
                  scope="col"
                  className="px-3 py-2 text-right font-semibold text-gray-700 whitespace-nowrap"
                >
                  {column.label}
                </th>
              ),
            )}
            {rowAction && (
              <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">
                <span className={rowActionLabel ? '' : 'sr-only'}>{rowActionLabel || 'عملیات'}</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {rows.length === 0 && (
            <tr>
              <td colSpan={colSpan} className="px-3 py-6 text-center text-gray-600">
                {emptyText}
              </td>
            </tr>
          )}
          {rows.map((row) => {
            const key = rowKey(row);
            const unknown = isUnknown?.(row) ?? false;
            const expanded = expandedKey !== null && expandedKey === key;
            return (
              <FragmentRow key={key}>
                <tr className={unknown ? 'bg-amber-50/60' : undefined}>
                  {columns.map((column) => (
                    <td
                      key={column.id}
                      className={`px-3 py-2 align-top ${column.numeric ? 'text-left tabular-nums' : 'text-right'} ${
                        unknown ? 'italic text-amber-900' : 'text-gray-900'
                      }`}
                    >
                      {column.render(row)}
                    </td>
                  ))}
                  {rowAction && <td className="px-3 py-2 align-top text-right">{rowAction(row)}</td>}
                </tr>
                {expanded && renderExpanded && (
                  <tr>
                    <td colSpan={colSpan} className="bg-gray-50 px-3 py-3">
                      {renderExpanded(row)}
                    </td>
                  </tr>
                )}
              </FragmentRow>
            );
          })}
        </tbody>
        {hasFooter && rows.length > 0 && (
          <tfoot className="bg-gray-50 border-t-2 border-gray-300 font-semibold text-gray-900">
            <tr>
              {columns.map((column, index) => (
                <td key={column.id} className={`px-3 py-2 ${column.numeric ? 'text-left tabular-nums' : 'text-right'}`}>
                  {index === 0 ? footerLabel : column.footer?.() ?? ''}
                </td>
              ))}
              {rowAction && <td />}
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}

/** A keyed wrapper so a row and its expansion share one React key. */
function FragmentRow({ children }: { children: ReactNode }) {
  return <>{children}</>;
}

const DATA_LABEL_TEXT: Record<DataLabel, string> = {
  real: 'داده‌های واقعی شرکت',
  demo: 'داده‌های نمایشی',
};

export function DataLabelBadge({ dataLabel }: { dataLabel: DataLabel }) {
  const isDemo = dataLabel === 'demo';
  return (
    <span
      className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold ${
        isDemo ? 'border-amber-300 bg-amber-50 text-amber-900' : 'border-emerald-300 bg-emerald-50 text-emerald-900'
      }`}
    >
      {DATA_LABEL_TEXT[dataLabel]}
    </span>
  );
}

interface ReportStatusLineProps {
  envelope: Pick<ReportEnvelope, 'dataLabel' | 'lastSuccessfulSync' | 'period' | 'previousPeriod' | 'filters' | 'unavailable'>;
}

/**
 * The statement of the report: data label, last successful sync, the period (and the comparison period), active
 * filters and unavailable figures (spec 004 T040, FR-005).
 */
export function ReportStatusLine({ envelope }: ReportStatusLineProps) {
  return (
    <section aria-label="وضعیت گزارش" className="space-y-2 rounded-xl border border-gray-200 bg-white p-4 text-sm text-gray-700 shadow-xs">
      <div className="flex flex-wrap items-center gap-3">
        <DataLabelBadge dataLabel={envelope.dataLabel} />
        <span>
          آخرین همگام‌سازی موفق:{' '}
          <span className="font-semibold text-gray-900">{envelope.lastSuccessfulSync?.atJalali ?? 'هنوز انجام نشده'}</span>
        </span>
      </div>
      <p>
        <span className="font-semibold text-gray-900">دوره انتخابی:</span> {envelope.period.label}
        {envelope.period.trimmedTo && <span className="text-amber-800"> (تا امروز محدود شد)</span>}
      </p>
      {envelope.previousPeriod && (
        <p>
          <span className="font-semibold text-gray-900">دوره قبل:</span> {envelope.previousPeriod.label}
        </p>
      )}
      {envelope.filters.length > 0 && (
        <ul className="flex flex-wrap gap-2" aria-label="فیلترهای فعال">
          {envelope.filters.map((filter) => (
            <li key={`${filter.name}-${filter.value}`} className="rounded-full bg-blue-50 px-2.5 py-0.5 text-xs text-blue-900">
              {filter.label}
            </li>
          ))}
        </ul>
      )}
      <UnavailableNote items={envelope.unavailable} />
    </section>
  );
}
