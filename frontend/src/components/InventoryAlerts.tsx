import { useEffect, useState } from 'react';
import { apiGet, ApiError } from '../services/api';
import { faQty } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

export interface InventoryAlertItem {
  productId: number;
  name: string;
  category: string;
  warehouse: string;
  onHand: string;
  hasBatch: boolean;
}

export interface InventoryAlertsData {
  demoData: boolean;
  filters: {
    categoryId: number | null;
  };
  threshold: number;
  lowStock: InventoryAlertItem[];
  outOfStock: InventoryAlertItem[];
  view: string;
}

export interface InventoryAlertsProps {
  categoryId: number | null;
  categories?: Array<{ categoryId: number; name: string }>;
}

export function InventoryAlerts({ categoryId }: InventoryAlertsProps) {
  const [data, setData] = useState<InventoryAlertsData | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    const query = categoryId !== null ? `?categoryId=${categoryId}` : '';

    apiGet<InventoryAlertsData>(`/api/inventory/alerts${query}`)
      .then((res) => {
        if (active) {
          setData(res);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          if (err instanceof ApiError) {
            setError(err.message || 'خطا در دریافت هشدارهای موجودی از سرور.');
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
  }, [categoryId]);

  return (
    <section className="space-y-6" dir="rtl" aria-label="وضعیت فعلی موجودی">
      {/* Section Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 border-b border-gray-200 pb-4">
        <div>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-bold text-gray-900">وضعیت فعلی موجودی</h2>
            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 border border-amber-300">
              داده نمایشی
            </span>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            وضعیت لحظه‌ای کالاهای نیازمند توجه در انبار (مستقل از بازه زمانی)
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {data && (
            <span className="text-xs font-medium text-gray-700 bg-gray-100 px-3 py-1.5 rounded-lg border border-gray-200">
              آستانه هشدار: {toPersianDigits(data.threshold)}
            </span>
          )}
          {data && (
            <div className="text-xs text-gray-500 bg-gray-50 px-3 py-1.5 rounded-lg border border-gray-200">
              منبع داده: <code className="font-mono text-blue-700">{data.view}</code>
            </div>
          )}
        </div>
      </div>

      {/* Loading & Error States */}
      {loading ? (
        <div className="bg-white rounded-xl border border-gray-200 p-12 text-center space-y-3">
          <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
          <p className="text-sm font-medium text-gray-600">در حال دریافت هشدارهای موجودی...</p>
        </div>
      ) : error ? (
        <div
          role="alert"
          className="rounded-xl bg-red-50 border border-red-200 p-6 text-center text-sm text-red-700 font-medium"
        >
          {error}
        </div>
      ) : data ? (
        <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
          {/* Low Stock List */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-xs overflow-hidden flex flex-col justify-between">
            <div>
              <div className="p-5 border-b border-gray-100 flex items-center justify-between bg-amber-50/40">
                <div className="flex items-center gap-2.5">
                  <span className="w-2.5 h-2.5 rounded-full bg-amber-500" />
                  <h3 className="text-base font-bold text-gray-900">
                    کالای کم‌موجودی ({toPersianDigits(data.lowStock.length)})
                  </h3>
                  <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-semibold bg-amber-100 text-amber-800 border border-amber-300">
                    داده نمایشی
                  </span>
                </div>
                <span className="text-xs text-amber-800 font-medium bg-amber-100/80 px-2 py-0.5 rounded">
                  موجودی کمتر یا مساوی آستانه ({toPersianDigits(data.threshold)})
                </span>
              </div>

              {data.lowStock.length === 0 ? (
                <div className="py-12 text-center text-sm text-gray-500">
                  کالایی با موجودی کم وجود ندارد.
                </div>
              ) : (
                <div className="overflow-x-auto max-h-[480px]">
                  <table className="w-full text-right text-sm">
                    <thead className="bg-gray-50 text-xs text-gray-600 sticky top-0 border-b border-gray-200 z-10">
                      <tr>
                        <th className="px-3.5 py-3 font-semibold">ردیف</th>
                        <th className="px-3.5 py-3 font-semibold">نام محصول</th>
                        <th className="px-3.5 py-3 font-semibold">دسته‌بندی</th>
                        <th className="px-3.5 py-3 font-semibold">انبار</th>
                        <th className="px-3.5 py-3 font-semibold text-center">موجودی</th>
                        <th className="px-3.5 py-3 font-semibold text-center">شناسه بچ</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {data.lowStock.map((item, index) => (
                        <tr key={item.productId} className="hover:bg-amber-50/30 transition-colors">
                          <td className="px-3.5 py-3 text-xs text-gray-400 font-mono">
                            {toPersianDigits(index + 1)}
                          </td>
                          <td className="px-3.5 py-3 text-gray-900 font-medium whitespace-nowrap">
                            {item.name}
                          </td>
                          <td className="px-3.5 py-3 text-gray-600 text-xs whitespace-nowrap">
                            {item.category}
                          </td>
                          <td className="px-3.5 py-3 text-gray-600 text-xs whitespace-nowrap">
                            {item.warehouse}
                          </td>
                          <td className="px-3.5 py-3 text-center font-mono text-xs font-bold text-amber-700 whitespace-nowrap">
                            {faQty(item.onHand)}
                          </td>
                          <td className="px-3.5 py-3 text-center whitespace-nowrap">
                            {item.hasBatch ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                                دارای بچ
                              </span>
                            ) : (
                              <span className="text-gray-400 text-xs">—</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>

          {/* Out of Stock List */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-xs overflow-hidden flex flex-col justify-between">
            <div>
              <div className="p-5 border-b border-gray-100 flex items-center justify-between bg-rose-50/40">
                <div className="flex items-center gap-2.5">
                  <span className="w-2.5 h-2.5 rounded-full bg-rose-500" />
                  <h3 className="text-base font-bold text-gray-900">
                    ناموجود ({toPersianDigits(data.outOfStock.length)})
                  </h3>
                  <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-semibold bg-amber-100 text-amber-800 border border-amber-300">
                    داده نمایشی
                  </span>
                </div>
                <span className="text-xs text-rose-800 font-medium bg-rose-100/80 px-2 py-0.5 rounded">
                  موجودی صفر یا منفی
                </span>
              </div>

              {data.outOfStock.length === 0 ? (
                <div className="py-12 text-center text-sm text-gray-500">
                  کالایی ناموجود نیست.
                </div>
              ) : (
                <div className="overflow-x-auto max-h-[480px]">
                  <table className="w-full text-right text-sm">
                    <thead className="bg-gray-50 text-xs text-gray-600 sticky top-0 border-b border-gray-200 z-10">
                      <tr>
                        <th className="px-3.5 py-3 font-semibold">ردیف</th>
                        <th className="px-3.5 py-3 font-semibold">نام محصول</th>
                        <th className="px-3.5 py-3 font-semibold">دسته‌بندی</th>
                        <th className="px-3.5 py-3 font-semibold">انبار</th>
                        <th className="px-3.5 py-3 font-semibold text-center">موجودی</th>
                        <th className="px-3.5 py-3 font-semibold text-center">شناسه بچ</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {data.outOfStock.map((item, index) => (
                        <tr key={item.productId} className="hover:bg-rose-50/30 transition-colors">
                          <td className="px-3.5 py-3 text-xs text-gray-400 font-mono">
                            {toPersianDigits(index + 1)}
                          </td>
                          <td className="px-3.5 py-3 text-gray-900 font-medium whitespace-nowrap">
                            {item.name}
                          </td>
                          <td className="px-3.5 py-3 text-gray-600 text-xs whitespace-nowrap">
                            {item.category}
                          </td>
                          <td className="px-3.5 py-3 text-gray-600 text-xs whitespace-nowrap">
                            {item.warehouse}
                          </td>
                          <td className="px-3.5 py-3 text-center font-mono text-xs font-bold text-rose-700 whitespace-nowrap">
                            {faQty(item.onHand)}
                          </td>
                          <td className="px-3.5 py-3 text-center whitespace-nowrap">
                            {item.hasBatch ? (
                              <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                                دارای بچ
                              </span>
                            ) : (
                              <span className="text-gray-400 text-xs">—</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
