import { JALALI_MONTH_NAMES, toLatinDigits, toPersianDigits } from '../utils/jalali';

interface JalaliMonthPickerProps {
  /** Selected Jalali month, «YYYY-MM». */
  value: string;
  /** Server's Jalali date today, «YYYY-MM-DD» (sync time zone). Months after it are not offered. */
  serverToday: string;
  onChange: (month: string) => void;
  disabled?: boolean;
}

/** How many Jalali years back the picker offers (the synced copy holds several years of purchases). */
const YEARS_BACK = 5;

function parseMonth(value: string): { year: number; month: number } {
  const [year, month] = toLatinDigits(value).split('-').map((part) => parseInt(part, 10));
  return { year, month };
}

function pad2(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * Jalali year and month selects with Persian names and digits. Defaults come from the server's
 * today (FR-025); future months are disabled. The value is always a Latin «YYYY-MM» key.
 */
export function JalaliMonthPicker({ value, serverToday, onChange, disabled = false }: JalaliMonthPickerProps) {
  const today = parseMonth(serverToday);
  const selected = parseMonth(value);

  const years: number[] = [];
  for (let year = today.year - YEARS_BACK; year <= today.year; year++) {
    years.push(year);
  }

  const isFuture = (year: number, month: number) =>
    year > today.year || (year === today.year && month > today.month);

  const emit = (year: number, month: number) => {
    const clampedMonth = isFuture(year, month) ? today.month : month;
    onChange(`${year}-${pad2(clampedMonth)}`);
  };

  const selectClass =
    'px-3 py-1.5 text-sm font-medium text-gray-800 bg-white border border-gray-300 rounded-lg shadow-xs focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-60';

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="انتخاب ماه جلالی">
      <label className="text-sm text-gray-600" htmlFor="overview-month">
        ماه
      </label>
      <select
        id="overview-month"
        className={selectClass}
        value={selected.month}
        disabled={disabled}
        onChange={(event) => emit(selected.year, parseInt(event.target.value, 10))}
      >
        {JALALI_MONTH_NAMES.map((name, index) => {
          const month = index + 1;
          return (
            <option key={month} value={month} disabled={isFuture(selected.year, month)}>
              {name}
            </option>
          );
        })}
      </select>
      <label className="sr-only" htmlFor="overview-year">
        سال
      </label>
      <select
        id="overview-year"
        className={selectClass}
        value={selected.year}
        disabled={disabled}
        onChange={(event) => emit(parseInt(event.target.value, 10), selected.month)}
      >
        {years.map((year) => (
          <option key={year} value={year}>
            {toPersianDigits(year)}
          </option>
        ))}
      </select>
    </div>
  );
}
