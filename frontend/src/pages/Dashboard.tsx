import { useEffect, useState } from 'react';
import { AiChat } from '../components/AiChat';
import { InventoryAlerts } from '../components/InventoryAlerts';
import { Receivables } from '../components/Receivables';
import { SalesCards, type SalesOverview, type SalesLines } from '../components/SalesCards';
import { apiGet, ApiError } from '../services/api';
import {
  currentJalaliMonthToDate,
  formatJalaliRange,
  getJalaliMonthRange,
  parseJalaliInput,
  toGregorian,
  toJalali,
  toPersianDigits,
} from '../utils/jalali';

type PeriodPreset = 'current' | 'prev' | 'prev2' | 'custom';

interface CategoryItem {
  categoryId: number;
  name: string;
}

interface CategoriesResponse {
  demoData: boolean;
  categories: CategoryItem[];
  view: string;
}

export function Dashboard() {
  const [activePreset, setActivePreset] = useState<PeriodPreset>('current');
  const [range, setRange] = useState<{ from: string; to: string }>(() =>
    currentJalaliMonthToDate()
  );

  // Custom date range inputs (in Jalali format, e.g. 1405/08/01)
  const [customFromInput, setCustomFromInput] = useState('');
  const [customToInput, setCustomToInput] = useState('');
  const [customError, setCustomError] = useState<string | null>(null);

  // Category selection
  const [categories, setCategories] = useState<CategoryItem[]>([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState<number | null>(null);

  // Sales data state
  const [overview, setOverview] = useState<SalesOverview | null>(null);
  const [lines, setLines] = useState<SalesLines | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Fetch categories on mount
  useEffect(() => {
    let active = true;
    apiGet<CategoriesResponse>('/api/meta/categories')
      .then((res) => {
        if (active && res && Array.isArray(res.categories)) {
          setCategories(res.categories);
        }
      })
      .catch(() => {
        // Categories fetch failure is non-fatal for dashboard load
      });

    return () => {
      active = false;
    };
  }, []);

  // Update date range when changing preset chips
  const handlePresetSelect = (preset: PeriodPreset) => {
    setActivePreset(preset);
    setCustomError(null);

    if (preset === 'current') {
      setRange(currentJalaliMonthToDate());
    } else if (preset === 'prev') {
      setRange(getJalaliMonthRange(1));
    } else if (preset === 'prev2') {
      setRange(getJalaliMonthRange(2));
    } else if (preset === 'custom') {
      // Pre-fill inputs with current range converted to Jalali if empty
      if (!customFromInput || !customToInput) {
        try {
          const jFrom = toJalali(range.from);
          const endObj = new Date(range.to);
          endObj.setUTCDate(endObj.getUTCDate() - 1);
          const jEnd = toJalali(endObj.toISOString().slice(0, 10));

          const fStr = `${jFrom.jy}/${String(jFrom.jm).padStart(2, '0')}/${String(jFrom.jd).padStart(2, '0')}`;
          const tStr = `${jEnd.jy}/${String(jEnd.jm).padStart(2, '0')}/${String(jEnd.jd).padStart(2, '0')}`;

          setCustomFromInput(toPersianDigits(fStr));
          setCustomToInput(toPersianDigits(tStr));
        } catch {
          // ignore
        }
      }
    }
  };

  // Apply custom Jalali date inputs
  const handleApplyCustomRange = () => {
    const fromParts = parseJalaliInput(customFromInput);
    if (!fromParts) {
      setCustomError('تاریخ شروع نامعتبر است. لطفاً در قالب معتبر مانند ۱۴۰۵/۰۸/۰۱ وارد کنید.');
      return;
    }

    const toParts = parseJalaliInput(customToInput);
    if (!toParts) {
      setCustomError('تاریخ پایان نامعتبر است. لطفاً در قالب معتبر مانند ۱۴۰۵/۰۸/۰۹ وارد کنید.');
      return;
    }

    const fromIso = toGregorian(fromParts.jy, fromParts.jm, fromParts.jd);
    const toInclusiveIso = toGregorian(toParts.jy, toParts.jm, toParts.jd);

    if (fromIso > toInclusiveIso) {
      setCustomError('تاریخ شروع باید قبل یا مساوی تاریخ پایان باشد.');
      return;
    }

    // Half-open range [from, to): to is toInclusive + 1 day
    const toDate = new Date(toInclusiveIso);
    toDate.setUTCDate(toDate.getUTCDate() + 1);
    const toIsoExclusive = toDate.toISOString().slice(0, 10);

    setCustomError(null);
    setRange({ from: fromIso, to: toIsoExclusive });
  };

  // Single effect driving overview + lines fetching consistently (FR-004 / SC-002)
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    const params = new URLSearchParams();
    params.set('from', range.from);
    params.set('to', range.to);
    if (selectedCategoryId !== null) {
      params.set('categoryId', String(selectedCategoryId));
    }

    const queryString = params.toString();

    Promise.all([
      apiGet<SalesOverview>(`/api/sales/overview?${queryString}`),
      apiGet<SalesLines>(`/api/sales/lines?${queryString}`),
    ])
      .then(([overviewData, linesData]) => {
        if (active) {
          setOverview(overviewData);
          setLines(linesData);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          if (err instanceof ApiError) {
            setError(err.message || 'خطا در دریافت اطلاعات فروش از سرور.');
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
  }, [range.from, range.to, selectedCategoryId]);

  const currentPeriodLabel =
    overview?.period.jalaliLabel ?? formatJalaliRange(range.from, range.to);

  return (
    <div className="space-y-8" dir="rtl">
      {/* Print-only Report Header (FR-014 / US6 AC3) */}
      <div className="print-header hidden">
        <span className="font-bold text-base text-gray-900">زارفا — گزارش داشبورد</span>
        <span className="text-xs text-gray-700">دوره: {currentPeriodLabel}</span>
      </div>

      {/* Dashboard Screen Header (Interactive) */}
      <div className="flex items-center justify-between no-print">
        <div>
          <h2 className="text-lg font-bold text-gray-900">داشبورد شاخص‌های کلیدی</h2>
          <p className="text-xs text-gray-500">گزارش عملکرد فروش، هشدارهای موجودی کالا و مطالبات مشتریان</p>
        </div>
        <button
          type="button"
          onClick={() => window.print()}
          className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold text-gray-700 bg-white hover:bg-gray-100 border border-gray-300 shadow-xs transition cursor-pointer"
          aria-label="چاپ گزارش"
        >
          <svg
            className="w-4 h-4 text-gray-600"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <polyline points="6 9 6 2 18 2 18 9" />
            <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
            <rect x="6" y="14" width="12" height="8" />
          </svg>
          <span>چاپ</span>
        </button>
      </div>

      {/* Universal Filter Bar */}
      <section className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs no-print">
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-5">
          {/* Period Presets & Custom Range */}
          <div className="space-y-3">
            <div className="text-xs font-semibold text-gray-500">بازه زمانی گزارش:</div>
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => handlePresetSelect('current')}
                className={`px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
                  activePreset === 'current'
                    ? 'bg-blue-600 text-white shadow-xs'
                    : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                }`}
              >
                ماه جاری
              </button>

              <button
                type="button"
                onClick={() => handlePresetSelect('prev')}
                className={`px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
                  activePreset === 'prev'
                    ? 'bg-blue-600 text-white shadow-xs'
                    : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                }`}
              >
                ماه قبل
              </button>

              <button
                type="button"
                onClick={() => handlePresetSelect('prev2')}
                className={`px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
                  activePreset === 'prev2'
                    ? 'bg-blue-600 text-white shadow-xs'
                    : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                }`}
              >
                دو ماه قبل
              </button>

              <button
                type="button"
                onClick={() => handlePresetSelect('custom')}
                className={`px-3.5 py-1.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
                  activePreset === 'custom'
                    ? 'bg-blue-600 text-white shadow-xs'
                    : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                }`}
              >
                بازه دلخواه
              </button>
            </div>
          </div>

          {/* Category Select Filter */}
          <div className="space-y-1.5 min-w-[220px]">
            <label htmlFor="category-select" className="block text-xs font-semibold text-gray-500">
              دسته‌بندی محصول:
            </label>
            <select
              id="category-select"
              value={selectedCategoryId !== null ? String(selectedCategoryId) : ''}
              onChange={(e) => {
                const val = e.target.value;
                setSelectedCategoryId(val ? parseInt(val, 10) : null);
              }}
              className="w-full px-3 py-2 bg-gray-50 border border-gray-300 rounded-lg text-xs font-medium text-gray-800 focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 transition"
            >
              <option value="">همه دسته‌ها</option>
              {categories.map((c) => (
                <option key={c.categoryId} value={c.categoryId}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
        </div>

        {/* Custom Range Expansion */}
        {activePreset === 'custom' && (
          <div className="mt-4 pt-4 border-t border-gray-100 space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-end gap-3">
              <div className="space-y-1">
                <label className="block text-xs font-semibold text-gray-600">از تاریخ:</label>
                <input
                  type="text"
                  dir="ltr"
                  value={customFromInput}
                  onChange={(e) => setCustomFromInput(e.target.value)}
                  placeholder="۱۴۰۵/۰۸/۰۱"
                  className="w-36 px-3 py-1.5 text-xs rounded-lg border border-gray-300 text-gray-900 focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 font-mono text-center"
                />
              </div>

              <div className="space-y-1">
                <label className="block text-xs font-semibold text-gray-600">تا تاریخ:</label>
                <input
                  type="text"
                  dir="ltr"
                  value={customToInput}
                  onChange={(e) => setCustomToInput(e.target.value)}
                  placeholder="۱۴۰۵/۰۸/۰۹"
                  className="w-36 px-3 py-1.5 text-xs rounded-lg border border-gray-300 text-gray-900 focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 font-mono text-center"
                />
              </div>

              <button
                type="button"
                onClick={handleApplyCustomRange}
                className="px-4 py-1.5 rounded-lg text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 transition cursor-pointer"
              >
                اعمال بازه
              </button>
            </div>

            {customError && (
              <p className="text-xs text-rose-600 font-medium">{customError}</p>
            )}
          </div>
        )}
      </section>

      {/* Main Content Area */}
      {loading ? (
        <div className="bg-white rounded-xl border border-gray-200 p-16 text-center space-y-3">
          <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
          <p className="text-sm font-medium text-gray-600">در حال دریافت داده‌های فروش...</p>
        </div>
      ) : error ? (
        <div
          role="alert"
          className="rounded-xl bg-red-50 border border-red-200 p-6 text-center text-sm text-red-700 font-medium"
        >
          {error}
        </div>
      ) : overview && lines ? (
        <SalesCards overview={overview} lines={lines} />
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 p-16 text-center text-sm text-gray-500">
          داده‌ای برای این بازه وجود ندارد.
        </div>
      )}

      {/*
        PLACEHOLDER BLOCKS FOR SUBSEQUENT STORIES:
        - T016 (User Story 2): Inventory Alerts UI (low-stock & out-of-stock lists)
        - T019 (User Story 3): Receivables UI (totals, top debtors, receipts)
        - T023 (User Story 4): AI Business Analyst Persian Chat
      */}
      <div className="space-y-6 pt-6 border-t border-gray-200">
        {/* Inventory Alerts (T016 / US2) */}
        <InventoryAlerts categoryId={selectedCategoryId} categories={categories} />

        {/* Receivables (T019 / US3) */}
        <Receivables
          from={range.from}
          to={range.to}
          jalaliLabel={overview?.period.jalaliLabel ?? formatJalaliRange(range.from, range.to)}
          prevJalaliLabel={overview?.period.prev.jalaliLabel}
        />

        {/* AI Business Analyst (T023 / US4) */}
        <AiChat
          from={range.from}
          to={range.to}
          jalaliLabel={overview?.period.jalaliLabel ?? formatJalaliRange(range.from, range.to)}
          categoryId={selectedCategoryId}
          categories={categories}
        />
      </div>
    </div>
  );
}
