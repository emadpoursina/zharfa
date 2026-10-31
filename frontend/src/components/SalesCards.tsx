import { useRef } from 'react';
import { faAmount, faNum, faPercent, faQty } from '../utils/format';
import { formatJalaliDate, toPersianDigits } from '../utils/jalali';

export interface SalesOverview {
  demoData: boolean;
  period: {
    from: string;
    to: string;
    jalaliLabel: string;
    prev: {
      from: string;
      to: string;
      jalaliLabel: string;
    };
  };
  filters: {
    categoryId: number | null;
  };
  totals: {
    revenue: string;
    prevRevenue: string;
    momDelta: string;
    momPct: string | null;
  };
  trend: Array<{
    bucket: string;
    revenue: string;
  }>;
  topProducts: Array<{
    productId: number;
    name: string;
    revenue: string;
  }>;
  topCustomers: Array<{
    customerId: number;
    name: string;
    revenue: string;
  }>;
  view: string;
}

export interface SalesLine {
  invoiceId: number;
  date: string;
  customer: string;
  product: string;
  qty: string;
  unitPrice: string;
  lineDiscount: string;
  net: string;
}

export interface SalesLines {
  demoData: boolean;
  period: {
    from: string;
    to: string;
    jalaliLabel: string;
    prev: {
      from: string;
      to: string;
      jalaliLabel: string;
    };
  };
  filters: {
    categoryId: number | null;
  };
  truncated: boolean;
  lines: SalesLine[];
  view: string;
}

interface SalesCardsProps {
  overview: SalesOverview;
  lines: SalesLines;
}

export function SalesCards({ overview, lines }: SalesCardsProps) {
  const tableRef = useRef<HTMLDivElement>(null);

  const scrollToTable = () => {
    tableRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  // Delta parsing for coloring & direction
  const deltaNum = parseFloat(overview.totals.momDelta) || 0;
  const isDeltaPositive = deltaNum > 0;
  const isDeltaNegative = deltaNum < 0;

  // Bar chart calculations
  const trendPoints = overview.trend || [];
  const maxRevenue = trendPoints.reduce((max, item) => {
    const val = parseFloat(item.revenue) || 0;
    return val > max ? val : max;
  }, 0);

  const svgWidth = 840;
  const svgHeight = 220;
  const chartMargin = { top: 20, right: 30, bottom: 45, left: 100 };
  const chartWidth = svgWidth - chartMargin.left - chartMargin.right;
  const chartHeight = svgHeight - chartMargin.top - chartMargin.bottom;

  const barStep = trendPoints.length > 0 ? chartWidth / trendPoints.length : chartWidth;
  const barWidth = Math.max(Math.min(barStep * 0.72, 28), 3);

  // Top products & customers maximums for relative visual bar
  const maxProdRevenue = overview.topProducts?.length > 0
    ? Math.max(...overview.topProducts.map((p) => parseFloat(p.revenue) || 0))
    : 1;

  const maxCustRevenue = overview.topCustomers?.length > 0
    ? Math.max(...overview.topCustomers.map((c) => parseFloat(c.revenue) || 0))
    : 1;

  return (
    <div className="space-y-8" dir="rtl">
      {/* Section Header with FR-007 demonstration data chip */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 border-b border-gray-200 pb-4">
        <div>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-bold text-gray-900">نمای کلی فروش</h2>
            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 border border-amber-300">
              داده نمایشی
            </span>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            دوره آماری: <span className="font-semibold text-gray-800">{overview.period.jalaliLabel}</span>
          </p>
        </div>

        <div className="text-xs text-gray-500 bg-gray-50 px-3 py-1.5 rounded-lg border border-gray-200">
          منبع داده: <code className="font-mono text-blue-700">{overview.view}</code>
        </div>
      </div>

      {/* Metric Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Total Sales Card */}
        <div
          role="button"
          tabIndex={0}
          onClick={scrollToTable}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              scrollToTable();
            }
          }}
          className="group bg-white rounded-xl border border-gray-200 p-6 shadow-xs hover:border-blue-400 hover:shadow-md transition cursor-pointer text-right"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-gray-500 tracking-wider">
              مجموع فروش دوره
            </span>
            <span className="text-xs font-medium text-blue-600 group-hover:underline">
              مشاهده اقلام فاکتورها ←
            </span>
          </div>

          <div className="mt-3 text-3xl font-extrabold text-gray-950 tracking-tight">
            {faAmount(overview.totals.revenue)}
          </div>

          <div className="mt-3 text-xs text-gray-600 flex items-center justify-between pt-2 border-t border-gray-100">
            <span>دوره گزارش:</span>
            <span className="font-semibold text-gray-800">{overview.period.jalaliLabel}</span>
          </div>
        </div>

        {/* Month-over-Month Comparison Card */}
        <div
          role="button"
          tabIndex={0}
          onClick={scrollToTable}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              scrollToTable();
            }
          }}
          className="group bg-white rounded-xl border border-gray-200 p-6 shadow-xs hover:border-blue-400 hover:shadow-md transition cursor-pointer text-right"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-gray-500 tracking-wider">
              مقایسه با دوره قبل
            </span>
            <span className="text-xs font-medium text-blue-600 group-hover:underline">
              مشاهده سوابق فروش ←
            </span>
          </div>

          <div className="mt-3 flex items-baseline gap-3">
            <div className="text-3xl font-extrabold text-gray-950 tracking-tight">
              {faAmount(overview.totals.momDelta)}
            </div>

            <div
              className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-bold ${
                isDeltaPositive
                  ? 'bg-emerald-100 text-emerald-800'
                  : isDeltaNegative
                  ? 'bg-rose-100 text-rose-800'
                  : 'bg-gray-100 text-gray-700'
              }`}
            >
              {isDeltaPositive ? '▲ ' : isDeltaNegative ? '▼ ' : ''}
              {faPercent(overview.totals.momPct)}
            </div>
          </div>

          <div className="mt-3 text-xs text-gray-600 pt-2 border-t border-gray-100">
            <span>مقایسه </span>
            <span className="font-semibold text-gray-800">{overview.period.jalaliLabel}</span>
            <span> با دوره قبلی (</span>
            <span className="font-semibold text-gray-800">{overview.period.prev.jalaliLabel}</span>
            <span> با فروش {faAmount(overview.totals.prevRevenue)})</span>
          </div>
        </div>
      </div>

      {/* Sales Trend Bar Chart (Inline SVG — No External Library) */}
      <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h3 className="text-base font-bold text-gray-900">روند فروش دوره</h3>
            <p className="text-xs text-gray-500 mt-0.5">
              توزیع درآمد حاصل از فروش به تفکیک روز یا هفته
            </p>
          </div>
          <span className="text-xs text-gray-400">نمودار ستونی بومی</span>
        </div>

        {trendPoints.length === 0 ? (
          <div className="py-12 text-center text-sm text-gray-500">
            داده‌ای برای ترسیم نمودار در این بازه وجود ندارد.
          </div>
        ) : (
          <div className="w-full overflow-x-auto">
            <svg
              viewBox={`0 0 ${svgWidth} ${svgHeight}`}
              className="w-full h-auto min-w-[620px] select-none"
            >
              {/* Grid lines & Y-axis labels */}
              {[0, 0.5, 1].map((ratio) => {
                const y = chartMargin.top + chartHeight * (1 - ratio);
                const labelVal = maxRevenue * ratio;
                return (
                  <g key={ratio}>
                    <line
                      x1={chartMargin.left}
                      y1={y}
                      x2={svgWidth - chartMargin.right}
                      y2={y}
                      stroke="#e5e7eb"
                      strokeDasharray={ratio > 0 ? '4 4' : undefined}
                    />
                    <text
                      x={chartMargin.left - 10}
                      y={y + 4}
                      textAnchor="end"
                      fontSize="10"
                      fill="#6b7280"
                      className="font-mono"
                    >
                      {faAmount(labelVal)}
                    </text>
                  </g>
                );
              })}

              {/* Bars */}
              {trendPoints.map((item, index) => {
                const val = Math.max(parseFloat(item.revenue) || 0, 0);
                const barH = maxRevenue > 0 ? (val / maxRevenue) * chartHeight : 0;
                const x = chartMargin.left + index * barStep + (barStep - barWidth) / 2;
                const y = chartMargin.top + chartHeight - barH;

                return (
                  <g key={item.bucket} className="group">
                    <rect
                      x={x}
                      y={y}
                      width={barWidth}
                      height={barH}
                      rx="3"
                      fill="#2563eb"
                      className="hover:fill-blue-500 transition-colors cursor-pointer"
                    >
                      <title>
                        {formatJalaliDate(item.bucket)}: {faAmount(item.revenue)}
                      </title>
                    </rect>
                  </g>
                );
              })}

              {/* X-axis hints (First, middle, last) */}
              {trendPoints.length > 0 && (
                <>
                  <text
                    x={chartMargin.left}
                    y={svgHeight - 12}
                    textAnchor="start"
                    fontSize="10"
                    fill="#6b7280"
                  >
                    {formatJalaliDate(trendPoints[0].bucket)}
                  </text>

                  {trendPoints.length > 2 && (
                    <text
                      x={chartMargin.left + chartWidth / 2}
                      y={svgHeight - 12}
                      textAnchor="middle"
                      fontSize="10"
                      fill="#6b7280"
                    >
                      {formatJalaliDate(
                        trendPoints[Math.floor(trendPoints.length / 2)].bucket
                      )}
                    </text>
                  )}

                  <text
                    x={svgWidth - chartMargin.right}
                    y={svgHeight - 12}
                    textAnchor="end"
                    fontSize="10"
                    fill="#6b7280"
                  >
                    {formatJalaliDate(trendPoints[trendPoints.length - 1].bucket)}
                  </text>
                </>
              )}
            </svg>
          </div>
        )}
      </div>

      {/* Top 5 Products & Top 5 Customers */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Top 5 Products */}
        <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between pb-3 border-b border-gray-100">
              <h3 className="text-base font-bold text-gray-900">
                ۵ محصول پرفروش دوره
              </h3>
              <span className="text-xs text-gray-500">بر اساس درآمد خالص</span>
            </div>

            {(!overview.topProducts || overview.topProducts.length === 0) ? (
              <div className="py-8 text-center text-sm text-gray-500">
                داده‌ای برای محصولات در این بازه وجود ندارد.
              </div>
            ) : (
              <div className="divide-y divide-gray-100 mt-2">
                {overview.topProducts.map((p, idx) => {
                  const rev = parseFloat(p.revenue) || 0;
                  const sharePct = maxProdRevenue > 0 ? (rev / maxProdRevenue) * 100 : 0;
                  return (
                    <div key={p.productId} className="py-3.5 space-y-1.5">
                      <div className="flex items-center justify-between text-sm">
                        <div className="flex items-center gap-2.5">
                          <span className="w-5 h-5 rounded-full bg-blue-50 text-blue-700 text-xs font-bold flex items-center justify-center">
                            {toPersianDigits(idx + 1)}
                          </span>
                          <span className="font-semibold text-gray-800">
                            {p.name}
                          </span>
                        </div>
                        <span className="font-mono text-xs font-semibold text-gray-900">
                          {faAmount(p.revenue)}
                        </span>
                      </div>
                      <div className="w-full bg-gray-100 h-1.5 rounded-full overflow-hidden">
                        <div
                          className="bg-blue-600 h-full rounded-full transition-all"
                          style={{ width: `${sharePct}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>

        {/* Top 5 Customers */}
        <div className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between pb-3 border-b border-gray-100">
              <h3 className="text-base font-bold text-gray-900">
                ۵ مشتری برتر دوره
              </h3>
              <span className="text-xs text-gray-500">بر اساس حجم خرید</span>
            </div>

            {(!overview.topCustomers || overview.topCustomers.length === 0) ? (
              <div className="py-8 text-center text-sm text-gray-500">
                داده‌ای برای مشتریان در این بازه وجود ندارد.
              </div>
            ) : (
              <div className="divide-y divide-gray-100 mt-2">
                {overview.topCustomers.map((c, idx) => {
                  const rev = parseFloat(c.revenue) || 0;
                  const sharePct = maxCustRevenue > 0 ? (rev / maxCustRevenue) * 100 : 0;
                  return (
                    <div key={c.customerId} className="py-3.5 space-y-1.5">
                      <div className="flex items-center justify-between text-sm">
                        <div className="flex items-center gap-2.5">
                          <span className="w-5 h-5 rounded-full bg-indigo-50 text-indigo-700 text-xs font-bold flex items-center justify-center">
                            {toPersianDigits(idx + 1)}
                          </span>
                          <span className="font-semibold text-gray-800">
                            {c.name}
                          </span>
                        </div>
                        <span className="font-mono text-xs font-semibold text-gray-900">
                          {faAmount(c.revenue)}
                        </span>
                      </div>
                      <div className="w-full bg-gray-100 h-1.5 rounded-full overflow-hidden">
                        <div
                          className="bg-indigo-600 h-full rounded-full transition-all"
                          style={{ width: `${sharePct}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Supporting Rows Table */}
      <div ref={tableRef} className="bg-white rounded-xl border border-gray-200 shadow-xs overflow-hidden">
        <div className="p-6 border-b border-gray-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
          <div>
            <div className="flex items-center gap-3">
              <h3 className="text-base font-bold text-gray-900">
                اقلام پشتیبان فروش (سطرهای فاکتور)
              </h3>
              <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 border border-amber-300">
                داده نمایشی
              </span>
            </div>
            <p className="text-xs text-gray-500 mt-0.5">
              فهرست کامل اقلام فاکتورهای فروش تأییدشده در بازه انتخابی
            </p>
          </div>

          <div className="flex items-center gap-3">
            {lines.truncated && (
              <span className="inline-flex items-center px-2.5 py-1 rounded-md text-xs font-semibold bg-amber-50 text-amber-800 border border-amber-200">
                نمایش حداکثر ۱٬۰۰۰ سطر
              </span>
            )}
            <span className="text-xs text-gray-500">
              تعداد سطرها: <strong className="text-gray-900">{faNum(lines.lines.length)}</strong>
            </span>
          </div>
        </div>

        {lines.lines.length === 0 ? (
          <div className="py-16 text-center text-sm text-gray-500">
            داده‌ای برای این بازه وجود ندارد.
          </div>
        ) : (
          <div className="overflow-x-auto max-h-[540px]">
            <table className="w-full text-right text-sm">
              <thead className="bg-gray-50 text-xs text-gray-600 sticky top-0 border-b border-gray-200 z-10">
                <tr>
                  <th className="px-4 py-3 font-semibold">ردیف</th>
                  <th className="px-4 py-3 font-semibold">شماره فاکتور</th>
                  <th className="px-4 py-3 font-semibold">تاریخ صدور</th>
                  <th className="px-4 py-3 font-semibold">نام خریدار</th>
                  <th className="px-4 py-3 font-semibold">نام محصول</th>
                  <th className="px-4 py-3 font-semibold text-center">تعداد</th>
                  <th className="px-4 py-3 font-semibold text-left">قیمت واحد</th>
                  <th className="px-4 py-3 font-semibold text-left">تخفیف سطر</th>
                  <th className="px-4 py-3 font-semibold text-left">مبلغ خالص</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {lines.lines.map((line, index) => (
                  <tr key={`${line.invoiceId}-${index}`} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3 text-xs text-gray-400 font-mono">
                      {toPersianDigits(index + 1)}
                    </td>
                    <td className="px-4 py-3 text-xs font-mono font-semibold text-gray-700">
                      {toPersianDigits(line.invoiceId)}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-600 whitespace-nowrap">
                      {formatJalaliDate(line.date)}
                    </td>
                    <td className="px-4 py-3 text-gray-900 font-medium whitespace-nowrap">
                      {line.customer}
                    </td>
                    <td className="px-4 py-3 text-gray-800 whitespace-nowrap">
                      {line.product}
                    </td>
                    <td className="px-4 py-3 text-center font-mono text-gray-900 whitespace-nowrap">
                      {faQty(line.qty)}
                    </td>
                    <td className="px-4 py-3 text-left font-mono text-xs text-gray-600 whitespace-nowrap">
                      {faAmount(line.unitPrice)}
                    </td>
                    <td className="px-4 py-3 text-left font-mono text-xs text-rose-600 whitespace-nowrap">
                      {parseFloat(line.lineDiscount) > 0 ? faAmount(line.lineDiscount) : '—'}
                    </td>
                    <td className="px-4 py-3 text-left font-mono text-xs font-bold text-gray-950 whitespace-nowrap">
                      {faAmount(line.net)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
