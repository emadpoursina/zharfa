import type { VisualChart } from '../services/api';
import { faRialFromDecimal } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

interface RankingBarChartProps {
  chart: VisualChart;
  rows: Record<string, string | null | undefined>[];
}

const WIDTH = 720;
const ROW_HEIGHT = 36;
const PAD_TOP = 20;
const PAD_BOTTOM = 24;
const PAD_LEFT = 16;
const PAD_RIGHT = 16;
const LABEL_WIDTH = 220;
const VALUE_WIDTH = 130;

/**
 * RTL horizontal bar chart for top-N ranking comparisons (spec 007 FR-001, research R4).
 * Pure inline SVG (same family as TrendChart.tsx), no external npm libraries.
 * At most 20 categories, rank 1 at the top, bars grow from right to left.
 */
export function RankingBarChart({ chart, rows }: RankingBarChartProps) {
  const categoryId = chart.categoryId || 'name';
  const series = chart.series?.[0];
  const metricLabel = series?.label || 'خالص خرید';

  const categories = rows.map((r) => String(r[categoryId] ?? ''));
  const rawValues = series?.values ?? rows.map((r) => (r[series?.metric ?? 'net'] as string | null | undefined));
  const numValues = rawValues.map((v) => Math.max(0, parseFloat(v ?? '') || 0));

  const maxVal = Math.max(0, ...numValues);
  const count = Math.min(categories.length, 20);

  if (count < 2) {
    return null;
  }

  const height = PAD_TOP + count * ROW_HEIGHT + PAD_BOTTOM;
  const barAreaWidth = WIDTH - PAD_LEFT - PAD_RIGHT - LABEL_WIDTH - VALUE_WIDTH;
  const barAreaRight = WIDTH - PAD_RIGHT - LABEL_WIDTH;

  return (
    <section className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs mt-4" dir="rtl">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-gray-900">
          نمودار مقایسه‌ای: {metricLabel}
        </h4>
        <span className="text-xs text-gray-500">مبالغ به ریال</span>
      </div>

      <svg
        viewBox={`0 0 ${WIDTH} ${height}`}
        className="mt-3 w-full h-auto"
        role="img"
        aria-label={`نمودار مقایسه ${toPersianDigits(count)} مورد`}
      >
        {categories.slice(0, count).map((category, index) => {
          const val = numValues[index] ?? 0;
          const rawVal = rawValues[index];
          const barWidth = maxVal > 0 ? (val / maxVal) * barAreaWidth : 0;
          const y = PAD_TOP + index * ROW_HEIGHT;
          const barY = y + 8;
          const barHeight = ROW_HEIGHT - 16;
          const rankText = toPersianDigits(index + 1);
          const amountText = rawVal != null ? faRialFromDecimal(rawVal) : 'داده در دسترس نیست';

          return (
            <g key={`${category}-${index}`} className="group">
              <title>{`${rankText}. ${category}: ${amountText}`}</title>

              {/* Rank and Category label (Right side) */}
              <text
                x={WIDTH - PAD_RIGHT}
                y={y + ROW_HEIGHT / 2 + 4}
                textAnchor="start"
                className="text-xs fill-gray-700 font-medium"
                style={{ fontSize: '11px' }}
              >
                {rankText}. {category.length > 28 ? `${category.slice(0, 26)}…` : category}
              </text>

              {/* Bar background track */}
              <rect
                x={barAreaRight - barAreaWidth}
                y={barY}
                width={barAreaWidth}
                height={barHeight}
                rx={4}
                className="fill-gray-100"
              />

              {/* Bar value (grows leftward from barAreaRight) */}
              {barWidth > 0 && (
                <rect
                  x={barAreaRight - barWidth}
                  y={barY}
                  width={barWidth}
                  height={barHeight}
                  rx={4}
                  className="fill-blue-600 group-hover:fill-blue-700 transition-colors"
                />
              )}

              {/* Value label (Left side) */}
              <text
                x={PAD_LEFT}
                y={y + ROW_HEIGHT / 2 + 4}
                textAnchor="start"
                className="text-xs fill-gray-900 font-medium"
                style={{ fontSize: '11px' }}
              >
                {amountText}
              </text>
            </g>
          );
        })}
      </svg>
    </section>
  );
}
