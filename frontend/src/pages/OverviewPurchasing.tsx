import { useEffect, useState } from 'react';
import { DataModeBadge } from '../components/DataModeBadge';
import { JalaliMonthPicker } from '../components/JalaliMonthPicker';
import { KpiCard, type KpiComparisonView } from '../components/KpiCard';
import { TopList } from '../components/TopList';
import { TrendChart } from '../components/TrendChart';
import { getPurchasingOverview, type PurchasingOverview } from '../services/api';
import { faNum, faRialFromDecimal } from '../utils/format';
import { jalaliMonthKeyLabel } from '../utils/jalali';

interface OverviewPurchasingProps {
  /** Server's Jalali date today («YYYY-MM-DD»), from GET /api/system/status. */
  serverJalaliToday: string;
}

const UNAVAILABLE_TEXT = 'داده در دسترس نیست';

const REASON_TEXT: Record<string, string> = {
  'no-successful-sync': 'هنوز همگام‌سازی موفقی انجام نشده است',
  'source-item-unverified': 'منبع داده این مورد هنوز تأیید نشده است',
  'source-item-unmapped': 'منبع داده این مورد نگاشت نشده است',
  'no-previous-data': 'ماه قبل پیش از نخستین ماه همگام‌سازی‌شده است',
};

const FIGURE_TEXT: Record<string, string> = {
  net: 'خرید خالص',
  gross: 'خرید ناخالص',
  returns: 'برگشت از خرید',
  vat: 'ارزش افزوده',
  comparison: 'مقایسه با ماه قبل',
  trend: 'روند ۱۲ ماهه',
  activeVendors: 'تعداد تأمین‌کنندگان فعال',
  topVendors: 'پنج تأمین‌کننده برتر',
  topProducts: 'پنج کالای برتر',
};

const BASIS_TEXT: Record<string, string> = {
  line: 'تخفیف هر ردیف فاکتور',
  lineAndDocument: 'تخفیف ردیف و تخفیف سند',
};

function messageOf(error: unknown): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : 'خطایی در پردازش درخواست رخ داد.';
}

/** «1405-08-26» → «1405-08» */
function monthKeyOf(jalaliDate: string): string {
  const [year, month] = jalaliDate.split('-');
  return `${year}-${month}`;
}

function comparisonView(overview: PurchasingOverview): KpiComparisonView | null {
  const comparison = overview.comparison;
  if (comparison === null) {
    return null;
  }
  return {
    delta: comparison.delta,
    percent: comparison.percent,
    previousMonthLabel: jalaliMonthKeyLabel(comparison.previousMonth),
  };
}

/**
 * Manager's Overview of real purchasing (spec 003 US5, FR-025..FR-029). Every figure is computed on the
 * server and only rendered here. Changing the Jalali month refetches the whole Overview.
 */
export function OverviewPurchasing({ serverJalaliToday }: OverviewPurchasingProps) {
  const [month, setMonth] = useState(() => monthKeyOf(serverJalaliToday));
  const [overview, setOverview] = useState<PurchasingOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    getPurchasingOverview(month)
      .then((data) => {
        if (active) {
          setOverview(data);
          setError(null);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setOverview(null);
          setError(messageOf(err));
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [month]);

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4 border-b border-gray-200 pb-4">
        <div>
          <h2 className="text-xl font-bold text-gray-900">نمای کلی خرید</h2>
          <p className="text-sm text-gray-600 mt-1">خرید خالص، ناخالص، برگشت از خرید و ارزش افزوده برای ماه انتخاب‌شده</p>
        </div>
        <div className="flex flex-col sm:flex-row sm:items-center gap-3">
          <JalaliMonthPicker
            value={month}
            serverToday={serverJalaliToday}
            onChange={setMonth}
            disabled={loading && overview === null}
          />
          {overview && (
            <DataModeBadge dataLabel={overview.dataLabel} className="self-start sm:self-auto" />
          )}
        </div>
      </div>

      {error && (
        <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
          {error}
        </p>
      )}

      {!overview && !error && (
        <p className="text-sm text-gray-600">در حال بارگذاری نمای کلی خرید...</p>
      )}

      {overview && (
        <div className={`space-y-6 transition-opacity ${loading ? 'opacity-60' : ''}`} aria-busy={loading}>
          <p className="text-sm text-gray-700">
            <span className="font-semibold">{overview.monthLabel}</span>
            <span className="text-gray-400"> · </span>
            <span>مبالغ به {overview.currencyLabel}</span>
            <span className="text-gray-400"> · </span>
            <span>{BASIS_TEXT[overview.discountBasis] ?? overview.discountBasis}</span>
          </p>

          {overview.unavailable.length > 0 && (
            <div className="text-sm text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3 space-y-1">
              <p className="font-semibold">برخی ارقام در دسترس نیست:</p>
              <ul className="list-disc pr-5">
                {overview.unavailable.map((item) => (
                  <li key={`${item.figure}-${item.reason}`}>
                    {FIGURE_TEXT[item.figure] ?? item.figure}: {REASON_TEXT[item.reason] ?? item.reason}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
            <KpiCard label="خرید خالص" value={overview.totals.net} comparison={comparisonView(overview)} note="فاکتورها منهای برگشت‌ها، بدون ارزش افزوده" />
            <KpiCard label="خرید ناخالص" value={overview.totals.gross} note="جمع ردیف‌های فاکتور خرید" />
            <KpiCard label="برگشت از خرید" value={overview.totals.returns} note="جمع ردیف‌های برگشت از خرید" />
            <KpiCard
              label="ارزش افزوده خالص"
              value={overview.totals.vat}
              note={
                overview.totals.vatInvoice === null || overview.totals.vatReturn === null
                  ? undefined
                  : `فاکتورها ${faRialFromDecimal(overview.totals.vatInvoice)} · برگشت ${faRialFromDecimal(overview.totals.vatReturn)}`
              }
            />
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
            <div className="xl:col-span-2">
              <TrendChart points={overview.trend} selectedMonth={overview.month} />
            </div>
            <section className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs flex flex-col gap-2">
              <h3 className="text-sm font-medium text-gray-600">تعداد تأمین‌کنندگان فعال</h3>
              <p className="text-2xl font-bold text-gray-900 tabular-nums">
                {overview.activeVendors === null ? UNAVAILABLE_TEXT : faNum(overview.activeVendors)}
              </p>
              <p className="text-xs text-gray-500">تأمین‌کنندگانی که در این ماه حداقل یک سند خرید یا برگشت داشته‌اند.</p>
            </section>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <TopList title="پنج تأمین‌کننده برتر بر اساس خرید خالص" items={overview.topVendors} />
            <TopList title="پنج کالای برتر بر اساس خرید خالص" items={overview.topProducts} />
          </div>
        </div>
      )}
    </div>
  );
}
