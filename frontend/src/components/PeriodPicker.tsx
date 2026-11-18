import { useState } from 'react';
import type { PeriodKind } from '../services/api';
import type { PeriodValue } from '../hooks/useReportQuery';
import { JalaliMonthPicker } from './JalaliMonthPicker';
import {
  JALALI_QUARTER_NAMES,
  jalaliDayKey,
  toLatinDigits,
  toPersianDigits,
} from '../utils/jalali';

interface PeriodPickerProps {
  value: PeriodValue;
  /** Server's Jalali today «YYYY-MM-DD»; future months are not offered. */
  serverToday: string;
  onChange: (next: PeriodValue) => void;
  disabled?: boolean;
}

const KIND_LABELS: Record<PeriodKind, string> = {
  month: 'ماه',
  quarter: 'فصل',
  year: 'سال',
  custom: 'بازه دلخواه',
};

const YEARS_BACK = 5;
const CUSTOM_DAY_ERROR = 'تاریخ شمسی معتبر نیست؛ به صورت ۱۴۰۵/۰۸/۰۹ وارد کنید.';

const selectClass =
  'px-3 py-1.5 text-sm font-medium text-gray-800 bg-white border border-gray-300 rounded-lg shadow-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-60';
const inputClass =
  'w-36 px-3 py-1.5 text-sm text-gray-800 bg-white border border-gray-300 rounded-lg shadow-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-60';

function yearOptions(serverToday: string): number[] {
  const year = parseInt(toLatinDigits(serverToday).slice(0, 4), 10);
  const years: number[] = [];
  for (let y = year - YEARS_BACK; y <= year; y++) {
    years.push(y);
  }
  return years;
}

function parseQuarter(key: string): { year: number; quarter: number } {
  const match = /^(\d{4})-Q([1-4])$/i.exec(toLatinDigits(key));
  return match ? { year: parseInt(match[1], 10), quarter: parseInt(match[2], 10) } : { year: 0, quarter: 1 };
}

/**
 * Period of a report (spec 004 T037): month (the Overview's month picker), quarter, year or a custom Jalali day range.
 * The server validates ranges and answers with Persian messages; the custom inputs only check the digits and shape.
 */
export function PeriodPicker({ value, serverToday, onChange, disabled = false }: PeriodPickerProps) {
  const years = yearOptions(serverToday);
  const quarter = parseQuarter(value.quarter);
  const [fromText, setFromText] = useState(() => toPersianDigits(value.from.replace(/-/g, '/')));
  const [toText, setToText] = useState(() => toPersianDigits(value.to.replace(/-/g, '/')));
  const [dayError, setDayError] = useState<string | null>(null);

  const commitDay = (which: 'from' | 'to', text: string) => {
    const key = jalaliDayKey(text);
    if (key === null) {
      setDayError(CUSTOM_DAY_ERROR);
      return;
    }
    setDayError(null);
    onChange({ ...value, [which]: key });
  };

  return (
    <div className="flex flex-wrap items-end gap-3" role="group" aria-label="بازه گزارش">
      <label className="flex flex-col gap-1 text-sm text-gray-600">
        <span>نوع بازه</span>
        <select
          className={selectClass}
          value={value.kind}
          disabled={disabled}
          onChange={(event) => onChange({ ...value, kind: event.target.value as PeriodKind })}
        >
          {(Object.keys(KIND_LABELS) as PeriodKind[]).map((kind) => (
            <option key={kind} value={kind}>
              {KIND_LABELS[kind]}
            </option>
          ))}
        </select>
      </label>

      {value.kind === 'month' && (
        <JalaliMonthPicker
          value={value.month}
          serverToday={toLatinDigits(serverToday).slice(0, 10)}
          onChange={(month) => onChange({ ...value, month })}
          disabled={disabled}
        />
      )}

      {value.kind === 'quarter' && (
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1 text-sm text-gray-600">
            <span>فصل</span>
            <select
              className={selectClass}
              value={quarter.quarter}
              disabled={disabled}
              onChange={(event) =>
                onChange({ ...value, quarter: `${quarter.year}-Q${event.target.value}` })
              }
            >
              {JALALI_QUARTER_NAMES.map((name, index) => (
                <option key={name} value={index + 1}>
                  {name}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1 text-sm text-gray-600">
            <span>سال</span>
            <select
              className={selectClass}
              value={quarter.year}
              disabled={disabled}
              onChange={(event) =>
                onChange({ ...value, quarter: `${event.target.value}-Q${quarter.quarter}` })
              }
            >
              {years.map((year) => (
                <option key={year} value={year}>
                  {toPersianDigits(year)}
                </option>
              ))}
            </select>
          </label>
        </div>
      )}

      {value.kind === 'year' && (
        <label className="flex flex-col gap-1 text-sm text-gray-600">
          <span>سال</span>
          <select
            className={selectClass}
            value={value.year}
            disabled={disabled}
            onChange={(event) => onChange({ ...value, year: event.target.value })}
          >
            {years.map((year) => (
              <option key={year} value={year}>
                {toPersianDigits(year)}
              </option>
            ))}
          </select>
        </label>
      )}

      {value.kind === 'custom' && (
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1 text-sm text-gray-600">
            <span>از تاریخ (شمسی)</span>
            <input
              type="text"
              inputMode="numeric"
              dir="ltr"
              className={inputClass}
              value={fromText}
              disabled={disabled}
              placeholder="۱۴۰۵/۰۷/۱۰"
              onChange={(event) => setFromText(event.target.value)}
              onBlur={() => commitDay('from', fromText)}
              aria-invalid={dayError !== null}
            />
          </label>
          <label className="flex flex-col gap-1 text-sm text-gray-600">
            <span>تا تاریخ (شمسی)</span>
            <input
              type="text"
              inputMode="numeric"
              dir="ltr"
              className={inputClass}
              value={toText}
              disabled={disabled}
              placeholder="۱۴۰۵/۰۸/۲۴"
              onChange={(event) => setToText(event.target.value)}
              onBlur={() => commitDay('to', toText)}
              aria-invalid={dayError !== null}
            />
          </label>
          {dayError && (
            <p role="alert" className="text-sm text-red-700">
              {dayError}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
