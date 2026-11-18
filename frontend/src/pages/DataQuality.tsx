import { useEffect, useState } from 'react';
import { DataModeBadge } from '../components/DataModeBadge';
import { getDataQuality, ApiError, type DataQualityIssueType, type DataQualityPage } from '../services/api';
import { toPersianDigits } from '../utils/jalali';

/** Contracts/api.md §9 strings, copied exactly. */
const NO_CHECK_TEXT = 'هنوز بررسی کیفیت داده انجام نشده است.';
const NONE_TEXT = 'مشکلی یافت نشد.';
const SYNC_FAILED_TEXT = 'آخرین همگام‌سازی ناموفق بود؛ نتایج زیر مربوط به آخرین همگام‌سازی موفق است.';
const CHECK_FAILED_TEXT = 'آخرین بررسی کیفیت داده ناموفق بود؛ نتایج زیر مربوط به آخرین بررسی موفق است.';
const NOT_CHECKED_TITLE = 'بررسی نشده';
const REJECTED_TITLE = 'ردیف‌های ردشده هنگام همگام‌سازی';
const NETWORK_ERROR = 'خطا در برقراری ارتباط با سرور.';

const SYNC_STATUS_LABELS: Record<string, string> = {
  succeeded: 'موفق',
  failed: 'ناموفق',
  running: 'در حال اجرا',
  skipped: 'ردشده',
};

const REJECTED_NOTE = 'این ردیف‌ها هنگام همگام‌سازی از منبع کنار گذاشته شده‌اند و در شمارش موارد بالا نیستند.';

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : NETWORK_ERROR;
}

function countText(count: number | null): string {
  if (count === null) {
    return '—';
  }
  if (count === 0) {
    return NONE_TEXT;
  }
  return `${toPersianDigits(count)} رکورد`;
}

interface IssueCardProps {
  issue: DataQualityIssueType;
  focused: boolean;
}

/** One issue type: label, description, heuristic note, count, examples, affected reports and deferred sub-checks. */
function IssueCard({ issue, focused }: IssueCardProps) {
  const headingId = `dq-${issue.type}-heading`;
  return (
    <section
      id={`dq-${issue.type}`}
      tabIndex={-1}
      aria-labelledby={headingId}
      className={`bg-white border rounded-xl p-5 shadow-xs space-y-4 focus:outline-hidden focus-visible:ring-2 focus-visible:ring-blue-600 ${
        focused ? 'border-blue-600 ring-2 ring-blue-300' : 'border-gray-200'
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <h3 id={headingId} className="text-base font-semibold text-gray-900">
            {issue.label}
          </h3>
          <p className="text-sm text-gray-600">{issue.description}</p>
        </div>
        <p className="text-sm font-semibold text-gray-900 whitespace-nowrap">{countText(issue.count)}</p>
      </div>

      {issue.heuristic && (
        <p className="text-sm text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3">{issue.heuristic}</p>
      )}

      {issue.examples.length > 0 && (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <caption className="sr-only">نمونه‌های {issue.label}</caption>
            <thead className="bg-gray-50 text-gray-700">
              <tr>
                <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                  شماره سند
                </th>
                <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                  تاریخ
                </th>
                <th scope="col" className="px-3 py-2 text-right font-semibold">
                  تأمین‌کننده
                </th>
                <th scope="col" className="px-3 py-2 text-right font-semibold">
                  کالا
                </th>
                <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                  فیلد
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {issue.examples.map((example, index) => (
                <tr key={`${example.recordKind}-${index}`} className="align-top">
                  <td className="px-3 py-2 whitespace-nowrap text-gray-800" dir="ltr">
                    {example.documentNumber ? toPersianDigits(example.documentNumber) : '—'}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap text-gray-800">
                    {example.dateJalali ? toPersianDigits(example.dateJalali) : '—'}
                  </td>
                  <td className="px-3 py-2 text-gray-800">{example.vendor ?? '—'}</td>
                  <td className="px-3 py-2 text-gray-800">{example.product ?? '—'}</td>
                  <td className="px-3 py-2 whitespace-nowrap text-gray-800">{example.fieldLabel}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="space-y-2">
        <p className="text-sm font-semibold text-gray-700">گزارش‌های تحت تأثیر</p>
        <ul className="flex flex-wrap gap-2" aria-label="گزارش‌های تحت تأثیر">
          {issue.affectedReports.map((report) => (
            <li
              key={report.key}
              className="text-xs font-medium text-gray-700 bg-gray-100 border border-gray-200 rounded-md px-2 py-0.5"
            >
              {report.label}
            </li>
          ))}
        </ul>
      </div>

      {issue.notChecked.length > 0 && (
        <div className="space-y-2">
          <h4 className="text-sm font-semibold text-gray-700">{NOT_CHECKED_TITLE}</h4>
          <ul className="list-disc pr-5 space-y-1 text-sm text-gray-700">
            {issue.notChecked.map((item) => (
              <li key={item.code}>{item.reason}</li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

interface DataQualityProps {
  /** Issue type ids named in the hash (`#/settings/data-quality?types=a,b`); the first is scrolled to and focused. */
  focusTypes: string[];
}

/**
 * Data quality for the Admin (contracts/api.md §4, FR-021–FR-025): the seven issue types with counts, examples, the
 * reports they affect, the deferred sub-checks, the rejected-at-sync box and the status of the last sync and check.
 */
export function DataQuality({ focusTypes }: DataQualityProps) {
  const [data, setData] = useState<DataQualityPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const focusKey = focusTypes.join(',');

  // One request per load; the server writes the audit entry for it.
  useEffect(() => {
    let active = true;
    setLoading(true);
    getDataQuality(focusKey ? focusKey.split(',') : [])
      .then((result) => {
        if (active) {
          setData(result);
          setError(null);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setData(null);
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
  }, [focusKey]);

  // The first named card is brought into view and focused once the results are shown.
  useEffect(() => {
    if (!data || data.focus.length === 0) {
      return;
    }
    const target = document.getElementById(`dq-${data.focus[0]}`);
    if (target) {
      target.scrollIntoView({ block: 'start' });
      target.focus();
    }
  }, [data]);

  const syncStatus = data?.latestSync ? SYNC_STATUS_LABELS[data.latestSync.status] ?? data.latestSync.status : null;

  return (
    <div className="space-y-6" dir="rtl">
      <section aria-labelledby="dq-heading" className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 id="dq-heading" className="text-lg font-semibold text-gray-900">
              کیفیت داده
            </h2>
            <p className="mt-1 text-sm text-gray-600">
              مشکلات داده‌ای که پس از هر همگام‌سازی در نسخهٔ همگام‌شدهٔ خرید بررسی می‌شوند. این بررسی ارقام گزارش‌ها را تغییر نمی‌دهد.
            </p>
          </div>
          {data && <DataModeBadge dataLabel={data.dataLabel} />}
        </div>

        {error && (
          <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
            {error}
          </p>
        )}

        {loading && !data && <p className="text-sm text-gray-600">در حال بارگذاری...</p>}

        {data && (
          <div className="space-y-3 text-sm text-gray-700">
            {data.latestSync ? (
              <p>
                آخرین همگام‌سازی: {syncStatus}
                {data.latestSync.finishedAtJalali ? ` · ${toPersianDigits(data.latestSync.finishedAtJalali)}` : ''}
              </p>
            ) : (
              <p>هنوز همگام‌سازی ثبت نشده است.</p>
            )}

            {data.latestSyncFailed && (
              <p role="status" className="text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3">
                {SYNC_FAILED_TEXT}
              </p>
            )}

            {data.latestCheckFailed && (
              <p role="status" className="text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3">
                {CHECK_FAILED_TEXT}
              </p>
            )}

            {data.check ? (
              <p>آخرین بررسی: {toPersianDigits(data.check.checkedAtJalali)}</p>
            ) : (
              <p className="text-gray-800 bg-gray-50 border border-gray-200 rounded-lg p-3">{NO_CHECK_TEXT}</p>
            )}
          </div>
        )}
      </section>

      {data &&
        data.issueTypes.map((issue) => (
          <IssueCard key={issue.type} issue={issue} focused={data.focus.includes(issue.type)} />
        ))}

      {data && (
        <section aria-labelledby="dq-rejected-heading" className="bg-white border border-gray-200 rounded-xl p-5 shadow-xs space-y-3">
          <h3 id="dq-rejected-heading" className="text-base font-semibold text-gray-900">
            {REJECTED_TITLE}
          </h3>
          <p className="text-sm text-gray-600">{REJECTED_NOTE}</p>
          {data.rejectedAtSync.length === 0 ? (
            <p className="text-sm text-gray-700">{NONE_TEXT}</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <caption className="sr-only">ردیف‌های ردشده هنگام همگام‌سازی</caption>
                <thead className="bg-gray-50 text-gray-700">
                  <tr>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">
                      جدول
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">
                      دلیل
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">
                      تعداد
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {data.rejectedAtSync.map((row) => (
                    <tr key={`${row.table}-${row.reason}`}>
                      <td className="px-3 py-2 text-gray-800" dir="ltr">
                        {row.table}
                      </td>
                      <td className="px-3 py-2 text-gray-800" dir="ltr">
                        {row.reason}
                      </td>
                      <td className="px-3 py-2 text-gray-800">{toPersianDigits(row.count)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
