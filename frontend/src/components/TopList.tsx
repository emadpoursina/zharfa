import type { PurchasingRankedItem } from '../services/api';
import { faRialFromDecimal } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

interface TopListProps {
  title: string;
  /** Up to five ranked rows, highest net first; `null` = data unavailable. */
  items: PurchasingRankedItem[] | null;
}

const MAX_ROWS = 5;

/**
 * Top 5 vendors or products by net purchases (FR-025). The unknown bucket («نامشخص») is styled apart
 * and never silently dropped. Fewer rows are shown when fewer exist.
 */
export function TopList({ title, items }: TopListProps) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs">
      <h3 className="text-base font-semibold text-gray-900">{title}</h3>
      {items === null ? (
        <p className="mt-3 text-sm text-gray-500">داده در دسترس نیست</p>
      ) : items.length === 0 ? (
        <p className="mt-3 text-sm text-gray-500">برای این ماه خریدی ثبت نشده است.</p>
      ) : (
        <ol className="mt-3 divide-y divide-gray-100">
          {items.slice(0, MAX_ROWS).map((item, index) => (
            <li key={`${item.key ?? 'unknown'}-${index}`} className="flex items-center justify-between gap-3 py-2.5">
              <span className="flex items-center gap-3 min-w-0">
                <span className="w-6 shrink-0 text-center text-xs font-semibold text-gray-500">
                  {toPersianDigits(index + 1)}
                </span>
                <span className={`truncate text-sm ${item.unknown ? 'italic text-amber-800' : 'text-gray-800'}`}>
                  {item.name}
                </span>
              </span>
              <span className="shrink-0 text-sm font-semibold text-gray-900 tabular-nums">
                {faRialFromDecimal(item.net)}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
