import { faRialFromDecimal } from '../utils/format';
import { JALALI_MONTH_NAMES, toPersianDigits } from '../utils/jalali';

export interface TrendSeries {
  key: string;
  name: string;
  /** One value per month; null = no data for that month (drawn as a gap, never as zero). */
  values: Array<string | null>;
  /** Per month: the month is only partly inside a custom range. */
  partial: boolean[];
}

interface MultiTrendChartProps {
  /** Latin Jalali month keys «YYYY-MM», oldest first. */
  months: string[];
  series: TrendSeries[];
}

const WIDTH = 720;
const HEIGHT = 300;
const PAD_LEFT = 64;
const PAD_RIGHT = 16;
const PAD_TOP = 16;
const PAD_BOTTOM = 56;

// Colours at WCAG AA contrast on white; each series also has its own dash pattern and marker shape, so the series
// stay distinguishable without colour (WCAG 1.4.1).
const COLORS = ['#1d4ed8', '#b45309', '#047857', '#7e22ce', '#be123c'];
const DASHES = ['', '7 4', '2 3', '10 4 2 4', '1 4'];

function monthLabel(key: string): string {
  const [year, month] = key.split('-');
  const name = JALALI_MONTH_NAMES[parseInt(month ?? '', 10) - 1] ?? key;
  return `${name} ${toPersianDigits(year ?? '')}`;
}

/**
 * Multi-series monthly trend as an inline SVG line chart (spec 004 T062): one line per vendor, distinguished by colour,
 * dash pattern and marker; gaps for months without data; hollow markers for partial months. A legend names the series.
 */
export function MultiTrendChart({ months, series }: MultiTrendChartProps) {
  const numbers = series.flatMap((item) => item.values.filter((v): v is string => v !== null).map((v) => Number(v)));
  const maxValue = Math.max(0, ...numbers);
  const minValue = Math.min(0, ...numbers);
  const range = maxValue - minValue || 1;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const plotWidth = WIDTH - PAD_LEFT - PAD_RIGHT;
  const step = months.length > 1 ? plotWidth / (months.length - 1) : 0;

  const xOf = (index: number) => PAD_LEFT + (months.length > 1 ? index * step : plotWidth / 2);
  const yOf = (value: number) => PAD_TOP + ((maxValue - value) / range) * plotHeight;
  const zeroY = yOf(0);
  const ticks = [maxValue, (maxValue + minValue) / 2, minValue];

  return (
    <figure className="space-y-3">
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="h-auto w-full" role="img" aria-label={`روند ماهانه خرید خالص برای ${series.length} تأمین‌کننده`}>
        {ticks.map((tick) => (
          <g key={tick}>
            <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={yOf(tick)} y2={yOf(tick)} className="stroke-gray-200" strokeWidth={1} />
            <text x={PAD_LEFT - 6} y={yOf(tick) + 4} textAnchor="end" fontSize={10} className="fill-gray-600">
              {toPersianDigits(Math.round(tick).toLocaleString('en-US'))}
            </text>
          </g>
        ))}
        <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={zeroY} y2={zeroY} className="stroke-gray-400" strokeWidth={1} />

        {series.map((item, seriesIndex) => {
          const color = COLORS[seriesIndex % COLORS.length];
          const dash = DASHES[seriesIndex % DASHES.length];
          // Break the polyline at every gap so a missing month is never drawn as zero.
          const segments: string[] = [];
          let current: string[] = [];
          item.values.forEach((value, index) => {
            if (value === null) {
              if (current.length > 1) segments.push(current.join(' '));
              current = [];
              return;
            }
            current.push(`${xOf(index)},${yOf(Number(value))}`);
          });
          if (current.length > 1) segments.push(current.join(' '));

          return (
            <g key={item.key}>
              {segments.map((points) => (
                <polyline
                  key={points}
                  points={points}
                  fill="none"
                  stroke={color}
                  strokeWidth={2.5}
                  strokeDasharray={dash || undefined}
                />
              ))}
              {item.values.map((value, index) =>
                value === null ? null : (
                  <circle
                    key={`${item.key}-${months[index]}`}
                    cx={xOf(index)}
                    cy={yOf(Number(value))}
                    r={4}
                    fill={item.partial[index] ? '#ffffff' : color}
                    stroke={color}
                    strokeWidth={2}
                  >
                    <title>{`${item.name} — ${monthLabel(months[index] ?? '')}: ${faRialFromDecimal(value)}${item.partial[index] ? ' (ماه نیمه‌کامل)' : ''}`}</title>
                  </circle>
                ),
              )}
            </g>
          );
        })}

        {months.map((month, index) => (
          <text key={month} x={xOf(index)} y={HEIGHT - PAD_BOTTOM + 18} textAnchor="middle" fontSize={10} className="fill-gray-700">
            {monthLabel(month)}
          </text>
        ))}
      </svg>

      <figcaption>
        <ul className="flex flex-wrap gap-x-5 gap-y-2 text-sm text-gray-800" aria-label="راهنمای نمودار">
          {series.map((item, seriesIndex) => (
            <li key={item.key} className="flex items-center gap-2">
              <svg width="36" height="10" aria-hidden="true">
                <line
                  x1="0"
                  x2="36"
                  y1="5"
                  y2="5"
                  stroke={COLORS[seriesIndex % COLORS.length]}
                  strokeWidth={2.5}
                  strokeDasharray={DASHES[seriesIndex % DASHES.length] || undefined}
                />
              </svg>
              <span>{item.name}</span>
            </li>
          ))}
        </ul>
        <p className="mt-2 text-xs text-gray-600">ماه‌های بدون داده خالی نمایش داده می‌شوند؛ دایره توخالی یعنی ماه نیمه‌کامل بازه.</p>
      </figcaption>
    </figure>
  );
}
