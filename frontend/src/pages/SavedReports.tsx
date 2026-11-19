import { useEffect, useState } from 'react';
import {
  getSavedReports,
  openSavedReport,
  deleteSavedReport,
  ApiError,
  type SavedReportItem,
  type SavedReportOpenResponse,
} from '../services/api';
import { RankingBarChart } from '../components/RankingBarChart';
import { faRialFromDecimal } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

interface SavedReportsProps {
  permissions: string[];
  serverToday?: string;
}

interface StandardColumn {
  id: string;
  header: string;
  isAmount: boolean;
}

function getStandardReportColumns(kind: string, data: any): StandardColumn[] {
  if (kind === 'vendor-financials') {
    return [
      { id: 'name', header: 'تأمین‌کننده', isAmount: false },
      { id: 'code', header: 'کد', isAmount: false },
      { id: 'vendorBalance', header: 'مانده تأمین‌کننده', isAmount: true },
      { id: 'outstandingPayments', header: 'پرداخت‌های معوق', isAmount: true },
    ];
  }
  if (kind === 'vendor-financials-history') {
    return [
      { id: 'paidAtJalali', header: 'تاریخ پرداخت', isAmount: false },
      { id: 'reference', header: 'مرجع', isAmount: false },
      { id: 'instrumentTypeLabel', header: 'نوع سند', isAmount: false },
      { id: 'amount', header: 'مبلغ', isAmount: true },
    ];
  }
  if (kind === 'vendor-financials-outstanding') {
    return [
      { id: 'dateJalali', header: 'تاریخ', isAmount: false },
      { id: 'reference', header: 'مرجع', isAmount: false },
      { id: 'instrumentTypeLabel', header: 'نوع سند', isAmount: false },
      { id: 'unpaidAmount', header: 'مبلغ معوق', isAmount: true },
    ];
  }
  if (kind === 'vendor-financials-purchases-vs-payments') {
    return [
      { id: 'netPurchases', header: 'خالص خرید', isAmount: true },
      { id: 'payments', header: 'پرداخت‌ها', isAmount: true },
      { id: 'purchasesMinusPayments', header: 'خرید منهای پرداخت', isAmount: true },
    ];
  }
  if (kind === 'vendors-compare') {
    return [
      { id: 'name', header: 'تأمین‌کننده', isAmount: false },
      { id: 'net', header: 'خالص خرید', isAmount: true },
      { id: 'netQuantity', header: 'تعداد خالص', isAmount: false },
      { id: 'documentCount', header: 'تعداد اسناد', isAmount: false },
      { id: 'returns', header: 'برگشت از خرید', isAmount: true },
    ];
  }
  if (kind === 'vendor-profile') {
    if (data?.topProducts && Array.isArray(data.topProducts) && data.topProducts.length > 0) {
      return [
        { id: 'name', header: 'کالا', isAmount: false },
        { id: 'net', header: 'خالص خرید', isAmount: true },
      ];
    }
    if (data?.returns && Array.isArray(data.returns) && data.returns.length > 0) {
      return [
        { id: 'productName', header: 'کالا', isAmount: false },
        { id: 'returns', header: 'مبلغ برگشت', isAmount: true },
        { id: 'quantity', header: 'تعداد', isAmount: false },
      ];
    }
    return [
      { id: 'number', header: 'شماره سند', isAmount: false },
      { id: 'dateJalali', header: 'تاریخ', isAmount: false },
      { id: 'docTypeLabel', header: 'نوع سند', isAmount: false },
      { id: 'signedNet', header: 'خالص خرید', isAmount: true },
      { id: 'quantity', header: 'تعداد', isAmount: false },
    ];
  }
  if (kind === 'trend') {
    return [
      { id: 'label', header: 'ماه', isAmount: false },
      { id: 'net', header: 'خالص خرید', isAmount: true },
      { id: 'netQuantity', header: 'تعداد خالص', isAmount: false },
    ];
  }
  if (kind === 'returns') {
    return [
      { id: 'productName', header: 'کالا', isAmount: false },
      { id: 'vendorName', header: 'تأمین‌کننده', isAmount: false },
      { id: 'returns', header: 'مبلغ برگشت', isAmount: true },
      { id: 'quantity', header: 'تعداد', isAmount: false },
    ];
  }
  if (kind === 'transactions' || kind === 'drill-down') {
    return [
      { id: 'number', header: 'شماره سند', isAmount: false },
      { id: 'dateJalali', header: 'تاریخ', isAmount: false },
      { id: 'docTypeLabel', header: 'نوع سند', isAmount: false },
      { id: 'vendorName', header: 'تأمین‌کننده', isAmount: false },
      { id: 'signedNet', header: 'خالص خرید', isAmount: true },
      { id: 'quantity', header: 'تعداد', isAmount: false },
    ];
  }
  if (kind === 'vendors') {
    return [
      { id: 'name', header: 'نام تأمین‌کننده', isAmount: false },
      { id: 'code', header: 'کد', isAmount: false },
      { id: 'net', header: 'خالص خرید', isAmount: true },
      { id: 'lastPurchaseDateJalali', header: 'آخرین خرید', isAmount: false },
    ];
  }
  const nameHeader = kind === 'by-vendor' ? 'تأمین‌کننده' : (kind === 'by-category' ? 'گروه کالا' : 'کالا');
  return [
    { id: 'name', header: nameHeader, isAmount: false },
    { id: 'net', header: 'خالص خرید', isAmount: true },
    { id: 'gross', header: 'ناخالص', isAmount: true },
    { id: 'returns', header: 'برگشت', isAmount: true },
    { id: 'vat', header: 'ارزش افزوده', isAmount: true },
    { id: 'documentCount', header: 'تعداد اسناد', isAmount: false },
  ];
}

function getStandardReportRows(kind: string, data: any): any[] {
  if (!data) return [];
  if (kind === 'vendor-financials-purchases-vs-payments') {
    return data.purchasesVsPayments ? [data.purchasesVsPayments] : [];
  }
  if (kind === 'vendors-compare') return data.vendors || [];
  if (kind === 'vendor-profile') {
    if (data.topProducts && Array.isArray(data.topProducts) && data.topProducts.length > 0) return data.topProducts;
    if (data.returns && Array.isArray(data.returns) && data.returns.length > 0) return data.returns;
    return data.documents?.rows || [];
  }
  return data.rows || [];
}

/**
 * Saved reports page (spec 007 FR-007..FR-013, contracts/api.md §2).
 * Lists the caller's saved reports, permits reopening on current data,
 * deletion with confirmation, and Excel export when permitted.
 */
export function SavedReports({ permissions }: SavedReportsProps) {
  const [reports, setReports] = useState<SavedReportItem[]>([]);
  const [dataLabel, setDataLabel] = useState<string>('real');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Active opened report state
  const [openedItem, setOpenedItem] = useState<SavedReportItem | null>(null);
  const [openedData, setOpenedData] = useState<SavedReportOpenResponse | null>(null);
  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState<string | null>(null);

  // Delete confirmation
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);

  const canExport = permissions.includes('export');
  const hasAiQuery = permissions.includes('ai.query');

  const loadReports = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await getSavedReports();
      setReports(res.items);
      setDataLabel(res.dataLabel);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message || 'خطا در بارگذاری فهرست گزارش‌های ذخیره شده.');
      } else {
        setError('خطا در برقراری ارتباط با سرور.');
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadReports();
  }, []);

  const handleOpen = async (item: SavedReportItem) => {
    const hasVendorFinancials = permissions.includes('vendor.financials');
    if (
      (item.kind.startsWith('vendor-financials') || item.requiredPermission === 'vendor.financials') &&
      !hasVendorFinancials
    ) {
      setOpenError('برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.');
      setOpenedItem(item);
      setOpenedData(null);
      return;
    }

    // Permission-removed check for generated
    if (item.kind === 'generated' && !hasAiQuery) {
      setOpenError('دسترسی پرسش از دستیار هوشمند (ai.query) برای این گزارش لازم است.');
      setOpenedItem(item);
      setOpenedData(null);
      return;
    }

    setOpening(true);
    setOpenError(null);
    setOpenedItem(item);
    setOpenedData(null);

    try {
      const res = await openSavedReport(item.id);
      setOpenedData(res);
    } catch (err) {
      if (err instanceof ApiError) {
        setOpenError(err.message || 'خطا در باز کردن گزارش ذخیره شده.');
      } else {
        setOpenError('خطا در برقراری ارتباط با سرور.');
      }
    } finally {
      setOpening(false);
    }
  };

  const handleDelete = async (id: number) => {
    setDeleteLoading(true);
    try {
      await deleteSavedReport(id);
      setDeletingId(null);
      if (openedItem?.id === id) {
        setOpenedItem(null);
        setOpenedData(null);
      }
      await loadReports();
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message || 'خطا در حذف گزارش.');
      }
    } finally {
      setDeleteLoading(false);
    }
  };

  const handleExportSaved = (id: number) => {
    window.location.href = `/api/saved-reports/${id}/export`;
  };

  return (
    <div className="space-y-6 max-w-6xl mx-auto" dir="rtl">
      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-gray-900">گزارش‌های ذخیره‌شده</h1>
          <p className="text-xs text-gray-600 mt-1">
            مشخصات ذخیره‌شده از گزارش‌های استاندارد و گزارش‌های تحلیلی دستیار هوشمند؛ باز کردن گزارش همواره روی داده‌های جاری محاسبه می‌شود.
          </p>
        </div>

        <div className="flex items-center gap-3">
          {dataLabel === 'demo' && (
            <span className="px-2.5 py-1 text-xs font-semibold bg-amber-100 text-amber-900 rounded-lg border border-amber-200">
              داده‌های نمایشی (دمو)
            </span>
          )}
          <span className="text-xs text-gray-500 font-medium bg-gray-100 px-3 py-1.5 rounded-lg border border-gray-200">
            {toPersianDigits(reports.length)} از ۵۰ گزارش
          </span>
        </div>
      </header>

      {/* Main error */}
      {error && (
        <div role="alert" className="p-4 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-800 font-medium">
          {error}
        </div>
      )}

      {/* Opened Report View */}
      {openedItem && (
        <section className="bg-white rounded-2xl border border-blue-200 p-6 shadow-sm space-y-5">
          <header className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-100 pb-4">
            <div>
              <div className="flex items-center gap-2">
                <span className="px-2 py-0.5 text-xs font-semibold bg-blue-50 text-blue-700 rounded-md">
                  {openedItem.kindLabel}
                </span>
                <h2 className="text-lg font-bold text-gray-900">{openedItem.name}</h2>
              </div>
              <p className="text-xs text-gray-500 mt-1">
                آخرین به‌روزرسانی مشخصات: {openedItem.updatedAtJalali}
              </p>
            </div>

            <div className="flex items-center gap-2">
              {openedItem.kind !== 'generated' && !openError && !opening && (
                <button
                  type="button"
                  onClick={() => {
                    if (openedItem.kind === 'vendors-compare') {
                      window.location.hash = '#/compare';
                    } else if (openedItem.kind === 'vendors' || openedItem.kind === 'vendor-profile') {
                      window.location.hash = '#/vendors';
                    } else {
                      window.location.hash = '#/reports';
                    }
                  }}
                  className="px-3.5 py-1.5 text-xs font-semibold text-blue-700 bg-blue-50 hover:bg-blue-100 border border-blue-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
                >
                  مشاهده در گزارش زنده
                </button>
              )}
              {canExport && !openError && !opening && (
                <button
                  type="button"
                  onClick={() => handleExportSaved(openedItem.id)}
                  className="px-3.5 py-1.5 text-xs font-semibold text-emerald-800 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-emerald-600"
                >
                  خروجی اکسل
                </button>
              )}
              <button
                type="button"
                onClick={() => {
                  setOpenedItem(null);
                  setOpenedData(null);
                  setOpenError(null);
                }}
                className="px-3.5 py-1.5 text-xs font-semibold text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-gray-400"
              >
                بستن گزارش
              </button>
            </div>
          </header>

          {opening && (
            <div className="py-12 text-center space-y-3">
              <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
              <p className="text-xs font-medium text-gray-600">در حال بازخوانی و محاسبه بر اساس داده‌های جاری...</p>
            </div>
          )}

          {openError && (
            <div role="alert" className="p-4 bg-rose-50 border border-rose-200 text-rose-800 text-xs rounded-xl font-medium">
              {openError}
            </div>
          )}

          {!opening && !openError && openedData && (
            <div className="space-y-5">
              {/* Generated Report View */}
              {openedItem.kind === 'generated' && openedData.visual && (
                <div className="space-y-4">
                  <div className="bg-gray-50 border border-gray-200 rounded-xl p-3 text-xs text-gray-700 flex flex-wrap gap-4">
                    <span>دوره: <strong className="text-gray-900">{toPersianDigits(openedData.visual.captions.period)}</strong></span>
                    <span>فیلترها: <strong className="text-gray-900">{openedData.visual.captions.filters}</strong></span>
                    <span>شاخص: <strong className="text-gray-900">{openedData.visual.captions.metrics}</strong></span>
                  </div>

                  {openedData.visual.dataQuality && openedData.visual.dataQuality.affectedRecords > 0 && (
                    <div role="status" className="p-3 bg-amber-50 border border-amber-200 text-amber-900 text-xs rounded-lg font-medium">
                      توجه کیفیت داده: {toPersianDigits(openedData.visual.dataQuality.affectedRecords)} رکورد پرچم‌دار در این بازه وجود دارد.
                    </div>
                  )}

                  <div className="overflow-x-auto border border-gray-200 rounded-xl">
                    <table className="w-full text-xs text-right divide-y divide-gray-200">
                      <thead className="bg-gray-50 text-gray-700 font-semibold">
                        <tr>
                          <th scope="col" className="px-4 py-3 w-12 text-center">ردیف</th>
                          {openedData.visual.table.columns.map((col) => (
                            <th key={col.id} scope="col" className="px-4 py-3">{col.header}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100 bg-white">
                        {openedData.visual.table.rows.length === 0 ? (
                          <tr>
                            <td colSpan={openedData.visual.table.columns.length + 1} className="px-4 py-6 text-center text-gray-500 font-medium">
                              هیچ رکوردی برای این بازه یافت نشد.
                            </td>
                          </tr>
                        ) : (
                          openedData.visual.table.rows.map((row, idx) => (
                            <tr key={row.key || idx} className="hover:bg-gray-50/80 transition-colors">
                              <td className="px-4 py-2.5 text-center text-gray-500 font-medium">
                                {toPersianDigits(idx + 1)}
                              </td>
                              {openedData.visual!.table.columns.map((col) => {
                                const val = row[col.id];
                                const isAmount = col.id === 'net' || col.id === 'gross' || col.id === 'returns' || col.id === 'vat' || col.id === 'vendorBalance' || col.id === 'outstandingPayments';
                                return (
                                  <td key={col.id} className="px-4 py-2.5 text-gray-900">
                                    {isAmount && val ? faRialFromDecimal(val) : (val ? toPersianDigits(val) : '—')}
                                  </td>
                                );
                              })}
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>

                  {openedData.visual.chart && openedData.visual.table.rows.length >= 2 && (
                    <RankingBarChart chart={openedData.visual.chart} rows={openedData.visual.table.rows} />
                  )}
                </div>
              )}

              {/* Standard report re-run response view */}
              {openedItem.kind !== 'generated' && Boolean(openedData.data) && (
                <div className="space-y-4">
                  {/* Meta bar */}
                  <div className="bg-gray-50 border border-gray-200 rounded-xl p-3 text-xs text-gray-700 flex flex-wrap gap-4 items-center justify-between">
                    <div className="flex flex-wrap gap-4 items-center">
                      {(openedData.data as any)?.period?.label && (
                        <span>دوره: <strong className="text-gray-900">{toPersianDigits((openedData.data as any).period.label)}</strong></span>
                      )}
                      {(openedData.data as any)?.filters && (openedData.data as any).filters.length > 0 && (
                        <span>فیلترها: <strong className="text-gray-900">{(openedData.data as any).filters.map((f: any) => `${f.name}: ${f.label}`).join('، ')}</strong></span>
                      )}
                      <span>
                        آخرین همگام‌سازی موفق: <strong className="text-gray-900">{toPersianDigits((openedData.data as any)?.lastSuccessfulSync?.atJalali || 'همگام‌سازی موفقی ثبت نشده است')}</strong>
                      </span>
                    </div>
                  </div>

                  {(openedData.data as any)?.dataQuality && (openedData.data as any).dataQuality.affectedRecords > 0 && (
                    <div role="status" className="p-3 bg-amber-50 border border-amber-200 text-amber-900 text-xs rounded-lg font-medium">
                      توجه کیفیت داده: {toPersianDigits((openedData.data as any).dataQuality.affectedRecords)} رکورد پرچم‌دار در این بازه وجود دارد.
                    </div>
                  )}

                  {(() => {
                    const columns = getStandardReportColumns(openedItem.kind, openedData.data);
                    const rows = getStandardReportRows(openedItem.kind, openedData.data);

                    return (
                      <div className="overflow-x-auto border border-gray-200 rounded-xl">
                        <table className="w-full text-xs text-right divide-y divide-gray-200">
                          <thead className="bg-gray-50 text-gray-700 font-semibold">
                            <tr>
                              <th scope="col" className="px-4 py-3 w-12 text-center">ردیف</th>
                              {columns.map((col) => (
                                <th key={col.id} scope="col" className="px-4 py-3">{col.header}</th>
                              ))}
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-gray-100 bg-white">
                            {rows.length === 0 ? (
                              <tr>
                                <td colSpan={columns.length + 1} className="px-4 py-6 text-center text-gray-500 font-medium">
                                  هیچ رکوردی برای این بازه یافت نشد.
                                </td>
                              </tr>
                            ) : (
                              rows.map((row: any, idx: number) => (
                                <tr key={row.key || row.id || row.documentId || idx} className="hover:bg-gray-50/80 transition-colors">
                                  <td className="px-4 py-2.5 text-center text-gray-500 font-medium">
                                    {toPersianDigits(idx + 1)}
                                  </td>
                                  {columns.map((col) => {
                                    const val = row[col.id];
                                    return (
                                      <td key={col.id} className="px-4 py-2.5 text-gray-900">
                                        {col.isAmount && val ? faRialFromDecimal(val) : (val != null ? toPersianDigits(String(val)) : '—')}
                                      </td>
                                    );
                                  })}
                                </tr>
                              ))
                            )}
                          </tbody>
                        </table>
                      </div>
                    );
                  })()}
                </div>
              )}
            </div>
          )}
        </section>
      )}

      {/* Reports List Table */}
      <section className="bg-white rounded-2xl border border-gray-200 overflow-hidden shadow-xs">
        {loading ? (
          <div className="p-12 text-center">
            <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
            <p className="text-xs text-gray-500 mt-2">در حال بارگذاری گزارش‌ها...</p>
          </div>
        ) : reports.length === 0 ? (
          <div className="p-12 text-center space-y-2">
            <p className="text-sm font-semibold text-gray-800">هنوز گزارشی ذخیره نشده است.</p>
            <p className="text-xs text-gray-500">
              می‌توانید با استفاده از دکمه «ذخیره گزارش» در صفحات گزارش‌ها، یا «ذخیره در گزارش‌ها» در دستیار هوشمند، مشخصات گزارش‌های مورد علاقه خود را ذخیره کنید.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-xs text-right divide-y divide-gray-200">
              <thead className="bg-gray-50 text-gray-700 font-semibold">
                <tr>
                  <th scope="col" className="px-4 py-3.5 w-12 text-center">ردیف</th>
                  <th scope="col" className="px-4 py-3.5">نام گزارش</th>
                  <th scope="col" className="px-4 py-3.5">نوع گزارش</th>
                  <th scope="col" className="px-4 py-3.5">دسترسی لازم</th>
                  <th scope="col" className="px-4 py-3.5">آخرین ذخیره</th>
                  <th scope="col" className="px-4 py-3.5 text-center w-48">عملیات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {reports.map((item, idx) => {
                  const cannotOpenGenerated = item.kind === 'generated' && !hasAiQuery;

                  return (
                    <tr key={item.id} className="hover:bg-gray-50/70 transition-colors">
                      <td className="px-4 py-3 text-center text-gray-500 font-medium">
                        {toPersianDigits(idx + 1)}
                      </td>
                      <td className="px-4 py-3 font-semibold text-gray-900">
                        {item.name}
                      </td>
                      <td className="px-4 py-3 text-gray-700">
                        <span className="px-2 py-0.5 rounded-md bg-gray-100 text-gray-800 font-medium">
                          {item.kindLabel}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-gray-500 font-mono text-[11px]">
                        {item.requiredPermission}
                      </td>
                      <td className="px-4 py-3 text-gray-600">
                        {item.updatedAtJalali}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <div className="flex items-center justify-center gap-2">
                          <button
                            type="button"
                            onClick={() => handleOpen(item)}
                            disabled={cannotOpenGenerated}
                            title={cannotOpenGenerated ? 'شما دسترسی پرسش از هوش مصنوعی ندارید' : 'باز کردن'}
                            className={`px-3 py-1 font-semibold rounded-lg transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600 ${
                              cannotOpenGenerated
                                ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                                : 'bg-blue-50 text-blue-700 hover:bg-blue-100'
                            }`}
                          >
                            باز کردن
                          </button>

                          {canExport && (
                            <button
                              type="button"
                              onClick={() => handleExportSaved(item.id)}
                              disabled={cannotOpenGenerated}
                              title={cannotOpenGenerated ? 'شما دسترسی به این گزارش ندارید' : 'خروجی اکسل'}
                              className={`px-2.5 py-1 font-semibold rounded-lg transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-emerald-600 ${
                                cannotOpenGenerated
                                  ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                                  : 'bg-emerald-50 text-emerald-800 hover:bg-emerald-100'
                              }`}
                            >
                              اکسل
                            </button>
                          )}

                          <button
                            type="button"
                            onClick={() => setDeletingId(item.id)}
                            className="px-2.5 py-1 font-semibold text-rose-700 bg-rose-50 hover:bg-rose-100 rounded-lg transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-rose-600"
                          >
                            حذف
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Delete Confirmation Modal */}
      {deletingId !== null && (
        <div
          role="dialog"
          aria-modal="true"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-gray-900/50 backdrop-blur-xs"
        >
          <div className="w-full max-w-sm bg-white rounded-2xl shadow-xl border border-gray-200 p-6 space-y-4">
            <h3 className="text-base font-bold text-gray-900">تأیید حذف گزارش ذخیره‌شده</h3>
            <p className="text-xs text-gray-600 leading-relaxed">
              آیا از حذف این گزارش مطمئن هستید؟ این عملیات قابل بازگشت نیست.
            </p>
            <div className="flex items-center justify-end gap-3 pt-2">
              <button
                type="button"
                onClick={() => setDeletingId(null)}
                disabled={deleteLoading}
                className="px-4 py-2 text-xs font-semibold text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-gray-400"
              >
                انصراف
              </button>
              <button
                type="button"
                onClick={() => handleDelete(deletingId)}
                disabled={deleteLoading}
                className="px-4 py-2 text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-rose-600"
              >
                {deleteLoading ? 'در حال حذف...' : 'حذف گزارش'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
