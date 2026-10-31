import { useEffect, useState } from 'react';
import { apiGet, ApiError } from '../services/api';
import { faAmount } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

export interface TopDebtor {
  customerId: number;
  name: string;
  balance: string;
}

export interface ReceivablesSummary {
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
  totals: {
    receivables: string;
    paymentsReceived: string;
  };
  topDebtors: TopDebtor[];
  view: string;
}

export interface ReceivableRow {
  customerId: number;
  name: string;
  openingBalance: string;
  invoicedNet: string;
  receivedTotal: string;
  receivedInPeriod: string;
  balance: string;
}

export interface ReceivablesRows {
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
  truncated: boolean;
  rows: ReceivableRow[];
  view: string;
}

export interface ReceivablesProps {
  from: string;
  to: string;
  jalaliLabel: string;
  prevJalaliLabel?: string;
}

/**
 * Format table cell amounts with Persian digits and «ریال», displaying «—» for zero amounts.
 */
function formatZeroDash(amountStr: string | number): string {
  const num = typeof amountStr === 'string' ? parseFloat(amountStr) : amountStr;
  if (isNaN(num) || Math.abs(num) < 0.005) {
    return '—';
  }
  return faAmount(num);
}

export function Receivables({ from, to, jalaliLabel }: ReceivablesProps) {
  const [summary, setSummary] = useState<ReceivablesSummary | null>(null);
  const [rowsData, setRowsData] = useState<ReceivablesRows | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    const params = new URLSearchParams();
    params.set('from', from);
    params.set('to', to);
    const queryString = params.toString();

    Promise.all([
      apiGet<ReceivablesSummary>(`/api/receivables?${queryString}`),
      apiGet<ReceivablesRows>(`/api/receivables/rows?${queryString}`),
    ])
      .then(([summaryRes, rowsRes]) => {
        if (active) {
          setSummary(summaryRes);
          setRowsData(rowsRes);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          if (err instanceof ApiError) {
            setError(err.message || 'خطا در دریافت اطلاعات مطالبات از سرور.');
          } else {
            setError('خطا در برقراری ارتباط با سرور.');
          }
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
  }, [from, to]);

  const activePeriodLabel = summary?.period.jalaliLabel || jalaliLabel;

  return (
    <section className="space-y-6" dir="rtl" aria-label="وضعیت مطالبات و دریافتی‌ها">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 border-b border-gray-200 pb-4">
        <div>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-bold text-gray-900">وضعیت مطالبات و دریافتی‌ها</h2>
            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 border border-amber-300">
              داده نمایشی
            </span>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            تحلیل مانده بدهی مشتریان، وصولی‌های نقدی و بدهکاران عمده
          </p>
        </div>

        {summary && (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-gray-500 bg-gray-50 px-3 py-1.5 rounded-lg border border-gray-200">
              منبع داده: <code className="font-mono text-blue-700">{summary.view}</code>
            </span>
          </div>
        )}
      </div>

      {/* Loading & Error States */}
      {loading ? (
        <div className="bg-white rounded-xl border border-gray-200 p-12 text-center space-y-3">
          <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
          <p className="text-sm font-medium text-gray-600">در حال دریافت داده‌های مطالبات...</p>
        </div>
      ) : error ? (
        <div
          role="alert"
          className="rounded-xl bg-red-50 border border-red-200 p-6 text-center text-sm text-red-700 font-medium"
        >
          {error}
        </div>
      ) : summary && rowsData ? (
        <div className="space-y-6">
          {/* KPI Cards: Total Receivables vs Period Cash Received */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
            {/* Card 1: مجموع مطالبات */}
            <div className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs flex flex-col justify-between">
              <div>
                <div className="flex items-center justify-between text-xs font-semibold text-gray-500">
                  <div className="flex items-center gap-2">
                    <span>مجموع مطالبات</span>
                    <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-100 text-amber-800 border border-amber-300">
                      داده نمایشی
                    </span>
                  </div>
                  <span className="px-2 py-0.5 rounded-md bg-gray-100 text-gray-600 text-[11px]">
                    مانده تجمعی
                  </span>
                </div>
                <div className="text-2xl font-bold text-gray-900 mt-3 font-mono">
                  {faAmount(summary.totals.receivables)}
                </div>
              </div>
              <div className="mt-4 pt-3 border-t border-gray-100 text-xs text-gray-500">
                مجموع بدهی سررسیدشده مشتریان تا پایان {activePeriodLabel}
              </div>
            </div>

            {/* Card 2: پرداخت‌های دریافتی در این بازه */}
            <div className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs flex flex-col justify-between">
              <div>
                <div className="flex items-center justify-between text-xs font-semibold text-gray-500">
                  <span>پرداخت‌های دریافتی در این بازه</span>
                  <span className="px-2 py-0.5 rounded-md bg-emerald-50 text-emerald-700 text-[11px] font-medium border border-emerald-200">
                    وصولی نقدی دوره
                  </span>
                </div>
                <div className="text-2xl font-bold text-emerald-700 mt-3 font-mono">
                  {faAmount(summary.totals.paymentsReceived)}
                </div>
              </div>
              <div className="mt-4 pt-3 border-t border-gray-100 text-xs text-gray-500">
                مجموع رسیدهای وصولی نقدی در بازه {activePeriodLabel}
              </div>
            </div>
          </div>

          {/* Top Debtors List (۵ بدهکار برتر) */}
          <div className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs space-y-4">
            <div className="flex items-center justify-between border-b border-gray-100 pb-3">
              <div>
                <h3 className="text-sm font-bold text-gray-800">۵ بدهکار برتر</h3>
                <p className="text-xs text-gray-500 mt-0.5">
                  مشتریان با بالاترین مانده بدهی تجمعی
                </p>
              </div>
              <span className="text-xs text-gray-400 bg-gray-50 px-2.5 py-1 rounded-md border border-gray-200 font-mono">
                {toPersianDigits(summary.topDebtors.length)} مشتری
              </span>
            </div>

            {summary.topDebtors.length === 0 ? (
              <p className="text-xs text-gray-500 text-center py-6">
                هیچ بدهکاری با مانده مثبت ثبت نشده است.
              </p>
            ) : (
              <div className="divide-y divide-gray-100">
                {summary.topDebtors.map((debtor, idx) => (
                  <div
                    key={debtor.customerId}
                    className="flex items-center justify-between py-3 hover:bg-gray-50/70 px-2 rounded-lg transition"
                  >
                    <div className="flex items-center gap-3">
                      <span className="inline-flex items-center justify-center w-6 h-6 rounded-full bg-blue-50 text-blue-700 text-xs font-bold">
                        {toPersianDigits(idx + 1)}
                      </span>
                      <span className="text-xs font-semibold text-gray-800">
                        {debtor.name}
                      </span>
                    </div>
                    <span className="text-xs font-bold text-gray-900 font-mono">
                      {faAmount(debtor.balance)}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Supporting Rows Table (/rows) */}
          <div className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 border-b border-gray-100 pb-3">
              <div>
                <div className="flex items-center gap-2.5">
                  <h3 className="text-sm font-bold text-gray-800">ریز صورت‌حساب و مانده مشتریان</h3>
                  <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-semibold bg-amber-100 text-amber-800 border border-amber-300">
                    داده نمایشی
                  </span>
                </div>
                <p className="text-xs text-gray-500 mt-0.5">
                  گزارش تحلیلی به تفکیک مشتری شامل مانده اولیه، خالص فاکتورها، دریافتی دوره و مانده نهایی
                </p>
              </div>
              <div className="flex items-center gap-2">
                {rowsData.truncated && (
                  <span className="text-xs text-amber-700 bg-amber-50 px-2.5 py-1 rounded-md border border-amber-200">
                    نمایش ۱٬۰۰۰ ردیف نخست
                  </span>
                )}
                <span className="text-xs text-gray-400 bg-gray-50 px-2.5 py-1 rounded-md border border-gray-200 font-mono">
                  {toPersianDigits(rowsData.rows.length)} ردیف
                </span>
              </div>
            </div>

            {rowsData.rows.length === 0 ? (
              <p className="text-xs text-gray-500 text-center py-8">
                داده‌ای برای ریز مطالبات مشتریان در این بازه یافت نشد.
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs text-right">
                  <thead>
                    <tr className="bg-gray-50/80 text-gray-600 border-b border-gray-200">
                      <th className="py-2.5 px-3 font-semibold">مشتری</th>
                      <th className="py-2.5 px-3 font-semibold text-left">مانده اولیه</th>
                      <th className="py-2.5 px-3 font-semibold text-left">خالص فاکتورها</th>
                      <th className="py-2.5 px-3 font-semibold text-left">دریافتی در بازه</th>
                      <th className="py-2.5 px-3 font-semibold text-left">کل دریافتی</th>
                      <th className="py-2.5 px-3 font-semibold text-left">مانده مطالبات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {rowsData.rows.map((row) => (
                      <tr key={row.customerId} className="hover:bg-gray-50/60 transition">
                        <td className="py-2.5 px-3 font-medium text-gray-800">{row.name}</td>
                        <td className="py-2.5 px-3 text-left font-mono text-gray-600">
                          {formatZeroDash(row.openingBalance)}
                        </td>
                        <td className="py-2.5 px-3 text-left font-mono text-gray-600">
                          {formatZeroDash(row.invoicedNet)}
                        </td>
                        <td className="py-2.5 px-3 text-left font-mono text-emerald-700">
                          {formatZeroDash(row.receivedInPeriod)}
                        </td>
                        <td className="py-2.5 px-3 text-left font-mono text-gray-600">
                          {formatZeroDash(row.receivedTotal)}
                        </td>
                        <td className="py-2.5 px-3 text-left font-mono font-bold text-gray-900">
                          {formatZeroDash(row.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Mandatory Footnote (US3 AC3 / FR-012) */}
          <div className="rounded-xl bg-blue-50/70 border border-blue-200 p-4 text-xs text-blue-950">
            <div className="flex items-start gap-2.5">
              <span className="font-bold text-blue-900 shrink-0">یادداشت انضباط مالی (FR-012):</span>
              <p className="leading-relaxed text-blue-900">
                دریافتی نقدی = مجموع رسیدهای ثبت‌شده در همین بازه است؛ موجودی مطالبات تجمعی است (فاکتورها + مانده اولیه − رسیدها) و با درآمد فروش یکسان نیست.
              </p>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
