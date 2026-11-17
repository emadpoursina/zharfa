import type { PurchasingTrendPoint } from '../services/api';
import { faRialFromDecimal } from '../utils/format';
import { JALALI_MONTH_NAMES, toPersianDigits } from '../utils/jalali';

interface TrendChartProps {
  /** Twelve points, oldest first, ending at the selected month. */
  points: PurchasingTrendPoint[];
  /** Selected month «YYYY-MM», drawn in the stronger colour. */
  selectedMonth: string;
}

const WIDTH = 720;
const HEIGHT = 280;
const PAD_LEFT = 16;
const PAD_RIGHT = 16;
const PAD_TOP = 16;
const PAD_BOTTOM = 52;

function monthNumber(key: string): number {
  return parseInt(key.split('-')[1] ?? '', 10);
}

function yearNumber(key: string): string {
  return toPersianDigits(key.split('-')[0] ?? '');
}

/**
 * Twelve-month net purchases as an inline SVG bar chart (FR-025). Oldest month is on the right (RTL).
 * A month without synced data is a gap with a dash, never a zero bar.
 */
export function TrendChart({ points, selectedMonth }: TrendChartProps) {
  const values = points.filter((p) => p.available && p.net !== null).map((p) => Number(p.net));
  const maxValue = Math.max(0, ...values);
  const minValue = Math.min(0, ...values);
  const range = maxValue - minValue || 1;

  const plotTop = PAD_TOP;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const slot = (WIDTH - PAD_LEFT - PAD_RIGHT) / Math.max(points.length, 1);
  const barWidth = slot * 0.56;

  const yOf = (value: number) => plotTop + ((maxValue - value) / range) * plotHeight;
  const zeroY = yOf(0);

  const summary = points.length === 0
    ? 'روند خرید خالص برای ۱۲ ماه'
    : `روند خرید خالص برای ${points.length} ماه`;

  return (
    <section className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs">
      <h3 className="text-base font-semibold text-gray-900">روند خرید خالص ۱۲ ماهه</h3>
      <p className="text-xs text-gray-500 mt-1">مبلغ‌ها به ریال؛ ماه‌های بدون داده همگام‌سازی‌شده خالی نمایش داده می‌شوند.</p>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="mt-4 w-full h-auto"
        role="img"
        aria-label={summary}
      >
        <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={zeroY} y2={zeroY} className="stroke-gray-300" strokeWidth={1} />
        {points.map((point, index) => {
          const centerX = WIDTH - PAD_RIGHT - (index + 0.5) * slot;
          const isSelected = point.month === selectedMonth;
          const label = monthNumber(point.month);
          const showYear = index === 0 || label === 1;
          const labelY = HEIGHT - PAD_BOTTOM + 18;
          const amountText = point.available && point.net !== null
            ? faRialFromDecimal(point.net)
            : 'داده در دسترس نیست';

          return (
            <g key={point.month}>
              <title>{`${JALALI_MONTH_NAMES[label - 1] ?? point.month} ${yearNumber(point.month)}: ${amountText}`}</title>
              {point.available && point.net !== null ? (
                <rect
                  x={centerX - barWidth / 2}
                  y={Math.min(yOf(Number(point.net)), zeroY)}
                  width={barWidth}
                  height={Math.max(Math.abs(yOf(Number(point.net)) - zeroY), 1)}
                  rx={3}
                  className={isSelected ? 'fill-blue-700' : 'fill-blue-300'}
                />
              ) : (
                <text x={centerX} y={zeroY - 6} textAnchor="middle" fontSize={13} className="fill-gray-400">
                  —
                </text>
              )}
              <text x={centerX} y={labelY} textAnchor="middle" fontSize={12} className={isSelected ? 'fill-gray-900 font-semibold' : 'fill-gray-600'}>
                {JALALI_MONTH_NAMES[label - 1] ?? ''}
              </text>
              {showYear && (
                <text x={centerX} y={labelY + 15} textAnchor="middle" fontSize={11} className="fill-gray-500">
                  {yearNumber(point.month)}
                </text>
              )}
            </g>
          );
        })}
      </svg>
    </section>
  );
}
