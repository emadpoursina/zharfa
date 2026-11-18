import { useId, useRef } from 'react';
import type { ReportFilterEcho } from '../services/api';
import type { ReportFilterName, ReportFilters } from '../hooks/useReportQuery';
import { EntityPicker, type EntityChoice, type LookupKind } from './EntityPicker';
import { PeriodPicker } from './PeriodPicker';

interface FilterBarProps {
  filters: ReportFilters;
  update: (changes: Partial<ReportFilters>) => void;
  setSearch: (text: string) => void;
  /** Filters this report or list declares; the bar offers no other (spec 004 T071). */
  declared: readonly ReportFilterName[];
  searchable: boolean;
  searchLabel?: string;
  serverToday: string;
  /** Labels of the active filters from the last response (envelope `filters`). */
  activeLabels?: ReportFilterEcho[];
}

const LOOKUP_FOR: Partial<Record<ReportFilterName, { kind: LookupKind; label: string }>> = {
  vendor: { kind: 'vendors', label: 'تأمین‌کننده' },
  product: { kind: 'products', label: 'کالا' },
  category: { kind: 'categories', label: 'گروه دارویی' },
};

const DOC_TYPE_OPTIONS: Array<{ value: '' | 'invoice' | 'return'; label: string }> = [
  { value: '', label: 'همه اسناد' },
  { value: 'invoice', label: 'فاکتور خرید' },
  { value: 'return', label: 'برگشت از خرید' },
];

const inputClass =
  'px-3 py-1.5 text-sm text-gray-800 bg-white border border-gray-300 rounded-lg shadow-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-60';

/**
 * The filter bar of every report, list and directory (spec 004 T038): the period, the entity pickers and the document
 * type the report declares, the comparison toggle where it applies (T067), and the search box. Active filters are
 * shown as removable chips with their Persian labels.
 */
export function FilterBar({ filters, update, setSearch, declared, searchable, searchLabel, serverToday, activeLabels = [] }: FilterBarProps) {
  const searchId = useId();
  // Removing a chip unmounts the button that had focus; focus moves to the bar so keyboard users are not dropped.
  const barRef = useRef<HTMLElement>(null);
  const has =(name: ReportFilterName) => declared.includes(name);
  const labelOf = (name: string, value: string | null): string | null => {
    if (value === null) return null;
    return activeLabels.find((item) => item.name === name)?.label ?? value;
  };

  const chips: Array<{ name: string; text: string; clear: () => void }> = [];
  const pairs: Array<[ReportFilterName, string | null, (value: null) => Partial<ReportFilters>]> = [
    ['vendor', filters.vendor, () => ({ vendor: null })],
    ['product', filters.product, () => ({ product: null })],
    ['category', filters.category, () => ({ category: null })],
  ];
  for (const [name, value, clear] of pairs) {
    if (has(name) && value) {
      const text = labelOf(name, value);
      if (text) chips.push({ name, text: `${LOOKUP_FOR[name]?.label ?? name}: ${text}`, clear: () => update(clear(null)) });
    }
  }
  if (has('docType') && filters.docType) {
    chips.push({
      name: 'docType',
      text: DOC_TYPE_OPTIONS.find((option) => option.value === filters.docType)?.label ?? filters.docType,
      clear: () => update({ docType: null }),
    });
  }

  return (
    <section
      ref={barRef}
      tabIndex={-1}
      aria-label="فیلترهای گزارش"
      className="space-y-4 rounded-xl border border-gray-200 bg-white p-4 shadow-xs focus:outline-none"
    >
      <PeriodPicker value={filters.period} serverToday={serverToday} onChange={(period) => update({ period })} />

      <div className="flex flex-wrap items-end gap-4">
        {(['vendor', 'product', 'category'] as const)
          .filter((name) => has(name))
          .map((name) => {
            const lookup = LOOKUP_FOR[name];
            if (!lookup) return null;
            return (
              <EntityPicker
                key={name}
                kind={lookup.kind}
                label={lookup.label}
                includeUnknown
                onSelect={(choice: EntityChoice) => update({ [name]: choice.key } as Partial<ReportFilters>)}
              />
            );
          })}

        {has('docType') && (
          <label className="flex flex-col gap-1 text-sm text-gray-600">
            <span>نوع سند</span>
            <select
              className={inputClass}
              value={filters.docType ?? ''}
              onChange={(event) => update({ docType: event.target.value === '' ? null : (event.target.value as 'invoice' | 'return') })}
            >
              {DOC_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        )}

        {has('compare') && (
          <label className="flex items-center gap-2 text-sm font-medium text-gray-800 pb-1.5">
            <input
              type="checkbox"
              checked={filters.compare}
              onChange={(event) => update({ compare: event.target.checked })}
              className="h-4 w-4 rounded border-gray-300 text-blue-700 focus-visible:ring-2 focus-visible:ring-blue-600"
            />
            مقایسه با دوره قبل
          </label>
        )}

        {searchable && (
          <div className="flex flex-col gap-1 text-sm text-gray-600">
            <label htmlFor={searchId}>جستجو</label>
            <input
              id={searchId}
              type="search"
              className={`${inputClass} w-72 max-w-full`}
              value={filters.q}
              placeholder={searchLabel ?? 'جستجو...'}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
        )}
      </div>

      {chips.length > 0 && (
        <ul className="flex flex-wrap gap-2" aria-label="فیلترهای فعال">
          {chips.map((chip) => (
            <li key={chip.name} className="inline-flex items-center gap-2 rounded-full bg-blue-50 px-3 py-0.5 text-xs text-blue-900">
              <span>{chip.text}</span>
              <button
                type="button"
                onClick={() => {
                  barRef.current?.focus();
                  chip.clear();
                }}
                aria-label={`حذف فیلتر ${chip.text}`}
                className="rounded-full px-1 text-blue-900 hover:bg-blue-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
