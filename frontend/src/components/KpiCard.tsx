import { faPercent, faRialFromDecimal } from '../utils/format';

/** Change against the preceding month. `null` percent = no percentage (zero base). */
export interface KpiComparisonView {
  delta: string;
  percent: string | null;
  previousMonthLabel: string;
}

interface KpiCardProps {
  label: string;
  /** Exact decimal string in Rial; `null` = data unavailable. */
  value: string | null;
  /** Omitted: no comparison row. `null`: the comparison is unavailable. */
  comparison?: KpiComparisonView | null;
  /** Small text under the figure (for example the VAT split). */
  note?: string;
}

const UNAVAILABLE_TEXT = 'داده در دسترس نیست';

/**
 * One purchasing figure in Rial (FR-027, FR-029). The amount comes from the server as a string and is
 * formatted without float arithmetic. Comparisons are neutral (purchases are not good or bad by themselves).
 */
export function KpiCard({ label, value, comparison, note }: KpiCardProps) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs flex flex-col gap-2">
      <h3 className="text-sm font-medium text-gray-600">{label}</h3>
      {value === null ? (
        <p className="text-base font-semibold text-gray-500">{UNAVAILABLE_TEXT}</p>
      ) : (
        <p className="text-2xl font-bold text-gray-900 tabular-nums break-words">{faRialFromDecimal(value)}</p>
      )}
      {note && <p className="text-xs text-gray-500">{note}</p>}
      {comparison !== undefined && (
        <div className="mt-auto pt-2 border-t border-gray-100 text-xs text-gray-600">
          {comparison === null ? (
            <p>مقایسه با ماه قبل: {UNAVAILABLE_TEXT}</p>
          ) : (
            <p className="space-y-0.5">
              <span className="block">
                نسبت به {comparison.previousMonthLabel}:{' '}
                <span className="font-semibold text-gray-800 tabular-nums">
                  {faRialFromDecimal(comparison.delta, { signed: true })}
                </span>
              </span>
              <span className="block">
                تغییر:{' '}
                <span className="font-semibold text-gray-800 tabular-nums">
                  {comparison.percent === null ? '—' : faPercent(comparison.percent)}
                </span>
              </span>
            </p>
          )}
        </div>
      )}
    </section>
  );
}
