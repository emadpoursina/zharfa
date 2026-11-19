import { useEffect, useState } from 'react';
import { getPurchasing, downloadReportExport, type VendorComparison as VendorComparisonData } from '../services/api';
import { canonicalPeriod, defaultPeriod, periodParams, type PeriodValue } from '../hooks/useReportQuery';
import { DataQualityNotice } from '../components/DataQualityNotice';
import { EntityPicker, type EntityChoice } from '../components/EntityPicker';
import { MultiTrendChart, type TrendSeries } from '../components/MultiTrendChart';
import { PeriodPicker } from '../components/PeriodPicker';
import { PrintVendorComparison } from '../components/PrintReport';
import { SaveReportDialog } from '../components/SaveReportDialog';
import { ReportStatusLine } from '../components/ReportTable';
import { faNum, faQty, faRialFromDecimal } from '../utils/format';

interface VendorComparisonProps {
  serverToday: string;
  showLink?: boolean;
  canExport?: boolean;
}

const MIN_VENDORS = 2;
const MAX_VENDORS = 5;

/**
 * Side-by-side comparison of two to five vendors (spec 004 T063, US4): the period (no vendor filter, no comparison
 * toggle), the chosen vendors, five measures per vendor and a monthly trend chart. Server messages are shown as they
 * come; the page never computes a figure of its own.
 */
export function VendorComparison({ serverToday, showLink, canExport }: VendorComparisonProps) {
  const [period, setPeriod] = useState<PeriodValue>(() => defaultPeriod(serverToday));
  const [chosen, setChosen] = useState<EntityChoice[]>([]);
  const [data, setData] = useState<VendorComparisonData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [print, setPrint] = useState<VendorComparisonData | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const keys = chosen.map((choice) => choice.key);
  const runnable = keys.length >= MIN_VENDORS;
  const periodKey = JSON.stringify(periodParams(period));
  const vendorsKey = keys.join(',');

  const exportNow = async () => {
    setExporting(true);
    setExportError(null);
    try {
      await downloadReportExport('vendors/compare', {
        vendors: vendorsKey,
        ...(JSON.parse(periodKey) as Record<string, string>),
      });
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'دریافت خروجی اکسل انجام نشد.');
    } finally {
      setExporting(false);
    }
  };

  useEffect(() => {
    if (!runnable) {
      setData(null);
      setError(null);
      return;
    }
    let active = true;
    setLoading(true);
    setError(null);
    getPurchasing<VendorComparisonData>('vendors/compare', {
      vendors: vendorsKey,
      ...(JSON.parse(periodKey) as Record<string, string>),
    })
      .then((response) => {
        if (active) {
          setData(response);
          setLoading(false);
        }
      })
      .catch((caught: unknown) => {
        if (active) {
          setData(null);
          setError(caught instanceof Error ? caught.message : 'خطایی رخ داد.');
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [runnable, vendorsKey, periodKey]);

  const addVendor = (choice: EntityChoice) => {
    setChosen((current) => {
      if (current.some((item) => item.key === choice.key) || current.length >= MAX_VENDORS) {
        return current;
      }
      return [...current, choice];
    });
  };

  const removeVendor = (key: string) => {
    setChosen((current) => current.filter((item) => item.key !== key));
  };

  const printNow = async () => {
    setPrintError(null);
    try {
      const response = await getPurchasing<VendorComparisonData>('vendors/compare/print', {
        vendors: vendorsKey,
        ...(JSON.parse(periodKey) as Record<string, string>),
      });
      setPrint(response);
      window.setTimeout(() => window.print(), 50);
    } catch (caught: unknown) {
      setPrintError(caught instanceof Error ? caught.message : 'چاپ انجام نشد.');
    }
  };

  const series: TrendSeries[] = data
    ? data.vendors.map((vendor) => ({
        key: vendor.key,
        name: vendor.name,
        values: vendor.trend.map((point) => (point.available ? point.net : null)),
        partial: vendor.trend.map((point) => point.partial),
      }))
    : [];

  return (
    <section aria-labelledby="compare-title" className="space-y-5">
      <header className="space-y-1">
        <h2 id="compare-title" className="text-lg font-bold text-gray-900">
          مقایسه تأمین‌کنندگان
        </h2>
        <p className="text-sm text-gray-600">
          دو تا پنج تأمین‌کننده را در یک دوره مقایسه کنید. هر عدد با پروفایل همان تأمین‌کننده برای همان دوره برابر است.
        </p>
      </header>

      <div className="space-y-4 rounded-xl border border-gray-200 bg-white p-4 shadow-xs">
        <PeriodPicker value={period} serverToday={serverToday} onChange={setPeriod} />

        <div className="flex flex-wrap items-end gap-4">
          <EntityPicker
            kind="vendors"
            label={`افزودن تأمین‌کننده (${faNum(chosen.length)} از ${faNum(MAX_VENDORS)})`}
            onSelect={addVendor}
            disabled={chosen.length >= MAX_VENDORS}
            placeholder="نام یا کد تأمین‌کننده"
          />
          {chosen.length >= MAX_VENDORS && (
            <p className="pb-2 text-sm text-amber-900" role="status">
              حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد.
            </p>
          )}
        </div>

        {chosen.length > 0 && (
          <ul className="flex flex-wrap gap-2" aria-label="تأمین‌کنندگان انتخاب‌شده">
            {chosen.map((choice) => (
              <li key={choice.key} className="inline-flex items-center gap-2 rounded-full bg-blue-50 px-3 py-0.5 text-sm text-blue-900">
                <span>{choice.name}</span>
                <button
                  type="button"
                  onClick={() => removeVendor(choice.key)}
                  aria-label={`حذف ${choice.name} از مقایسه`}
                  className="rounded-full px-1 hover:bg-blue-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}

        {!runnable && (
          <p className="text-sm text-gray-700" role="status">
            برای مقایسه دست‌کم دو تأمین‌کننده لازم است.
          </p>
        )}
      </div>

      {error && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </p>
      )}
      {loading && <p className="text-sm text-gray-600">در حال محاسبه مقایسه...</p>}

      {data && (
        <>
          <ReportStatusLine envelope={data} />
          <DataQualityNotice notice={data.dataQuality} showLink={Boolean(showLink)} />

          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setSaveOpen(true)}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              ذخیره گزارش
            </button>
            {canExport && (
              <button
                type="button"
                onClick={exportNow}
                disabled={exporting}
                className="rounded-lg border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-sm font-medium text-emerald-800 hover:bg-emerald-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-600 disabled:opacity-50 cursor-pointer"
              >
                {exporting ? 'در حال دریافت...' : 'خروجی اکسل'}
              </button>
            )}
            <button
              type="button"
              onClick={printNow}
              className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
            >
              چاپ مقایسه
            </button>
          </div>
          {exportError && <p role="alert" className="text-sm text-red-700">{exportError}</p>}
          {printError && <p role="alert" className="text-sm text-red-700">{printError}</p>}

          {saveOpen && (
            <SaveReportDialog
              open={saveOpen}
              kind="vendors-compare"
              defaultName="مقایسه تأمین‌کنندگان"
              period={canonicalPeriod(period)}
              filters={{ vendors: keys }}
              onClose={() => setSaveOpen(false)}
            />
          )}

          <div className="overflow-x-auto rounded-xl border border-gray-200 bg-white shadow-xs">
            <table className="min-w-full text-sm">
              <caption className="sr-only">پنج معیار هر تأمین‌کننده در دوره</caption>
              <thead className="bg-gray-50">
                <tr>
                  <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">تأمین‌کننده</th>
                  <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-700">خالص خرید</th>
                  <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-700">تعداد خالص (واحد ثبت‌شده)</th>
                  <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-700">تعداد اسناد</th>
                  <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-700">برگشت از خرید</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.vendors.map((vendor) => (
                  <tr key={vendor.key}>
                    <td className="px-3 py-2 text-right text-gray-900">
                      {vendor.name}
                      {vendor.code && <span className="text-xs text-gray-500"> ({vendor.code})</span>}
                    </td>
                    <td className="px-3 py-2 text-left tabular-nums">{faRialFromDecimal(vendor.net)}</td>
                    <td className="px-3 py-2 text-left tabular-nums">{vendor.netQuantity === null ? '—' : faQty(vendor.netQuantity)}</td>
                    <td className="px-3 py-2 text-left tabular-nums">{vendor.documentCount === null ? '—' : faNum(vendor.documentCount)}</td>
                    <td className="px-3 py-2 text-left tabular-nums">{faRialFromDecimal(vendor.returns)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <section aria-labelledby="compare-trend-title" className="rounded-xl border border-gray-200 bg-white p-5 shadow-xs">
            <h3 id="compare-trend-title" className="text-base font-semibold text-gray-900">
              روند ماهانه خالص خرید
            </h3>
            <div className="mt-3">
              <MultiTrendChart months={data.months} series={series} />
            </div>
          </section>
        </>
      )}

      <PrintVendorComparison title="مقایسه تأمین‌کنندگان" comparison={print} />
    </section>
  );
}
