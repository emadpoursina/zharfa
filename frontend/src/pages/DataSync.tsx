import { useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  getSyncJobs,
  runSyncNow,
  type SyncJob,
  type SyncJobsResponse,
  type SyncJobTable,
} from '../services/api';
import { faNum } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

/** While a run is active the page refreshes the job list at this interval. */
const POLL_INTERVAL_MS = 3000;

const TRIGGER_LABELS: Record<SyncJob['trigger'], string> = {
  manual: 'دستی',
  scheduled: 'زمان‌بندی‌شده',
  'startup-recovery': 'بازیابی پس از راه‌اندازی',
};

const MODE_LABELS: Record<SyncJob['mode'], string> = {
  incremental: 'افزایشی',
  full: 'کامل',
};

const STATUS_LABELS: Record<SyncJob['status'], string> = {
  running: 'در حال اجرا',
  succeeded: 'موفق',
  failed: 'ناموفق',
  skipped: 'رد شده',
};

const STATUS_STYLES: Record<SyncJob['status'], string> = {
  running: 'bg-blue-50 text-blue-800 border-blue-200',
  succeeded: 'bg-green-50 text-green-800 border-green-200',
  failed: 'bg-red-50 text-red-700 border-red-200',
  skipped: 'bg-amber-50 text-amber-800 border-amber-200',
};

const TABLE_LABELS: Record<string, string> = {
  vendor: 'تأمین‌کنندگان',
  product: 'کالاها',
  purchase_invoice: 'فاکتورهای خرید',
  purchase_return: 'برگشت از خرید',
  purchase_line: 'ردیف‌های خرید',
};

function messageOf(error: unknown): string {
  return error instanceof Error && error.message.trim().length > 0
    ? error.message
    : 'خطایی در پردازش درخواست رخ داد.';
}

function durationText(ms: number | null): string {
  if (ms === null) {
    return '—';
  }
  return `${faNum(ms / 1000)} ثانیه`;
}

function countText(value: number): string {
  return faNum(value);
}

function TableCounts({ tables }: { tables: SyncJobTable[] }) {
  if (tables.length === 0) {
    return <span className="text-xs text-gray-500">—</span>;
  }

  return (
    <ul className="space-y-1 text-xs text-gray-700">
      {tables.map((table) => (
        <li key={table.table}>
          <span className="font-medium text-gray-900">{TABLE_LABELS[table.table] ?? table.table}</span>
          {' — '}
          خوانده: {countText(table.read)} · افزوده: {countText(table.inserted)} · به‌روزرسانی:{' '}
          {countText(table.updated)} · حذف: {countText(table.deleted)} · رد: {countText(table.rejected)}
        </li>
      ))}
    </ul>
  );
}

function StatusBadge({ status }: { status: SyncJob['status'] }) {
  return (
    <span className={`inline-block rounded-md border px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[status]}`}>
      {STATUS_LABELS[status]}
    </span>
  );
}

/**
 * Data sync (US2, Admin only): "sync now", the running job, the job list with per-table counts,
 * source and map problems, and the next nightly run. The server enforces the Admin role; the
 * page is shown only to the Admin, and a 403 or 401 is shown as the server's message.
 */
export function DataSync() {
  const [data, setData] = useState<SyncJobsResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await getSyncJobs(50);
      setData(response);
      setLoadError(null);
    } catch (error: unknown) {
      setLoadError(messageOf(error));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const isRunning = Boolean(data?.running);

  // Poll only while a run is active; the interval stops as soon as the list shows no running job.
  useEffect(() => {
    if (!isRunning) {
      return;
    }

    const timer = window.setInterval(() => {
      void load();
    }, POLL_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [isRunning, load]);

  const handleRunNow = async () => {
    setStarting(true);
    setNotice(null);
    try {
      await runSyncNow();
      setNotice('همگام‌سازی آغاز شد. نتیجه در فهرست زیر نمایش داده می‌شود.');
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 409) {
        setNotice('یک همگام‌سازی در حال اجراست؛ اجرای جدیدی آغاز نشد.');
      } else {
        setNotice(messageOf(error));
      }
    } finally {
      setStarting(false);
      await load();
    }
  };

  const running = data?.running ?? null;
  const schedule = data?.schedule ?? null;
  const source = data?.source ?? null;

  return (
    <div className="space-y-6" dir="rtl">
      <section className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">همگام‌سازی داده</h2>
            <p className="mt-1 text-sm text-gray-600">
              خواندن تأمین‌کنندگان، کالاها، فاکتورها، برگشت‌ها و ردیف‌های خرید از منبع فقط‌خواندنی.
            </p>
          </div>

          <button
            type="button"
            onClick={handleRunNow}
            disabled={starting || isRunning || source?.configured === false || source?.mapValid === false}
            className="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg transition disabled:opacity-60 disabled:cursor-not-allowed cursor-pointer"
          >
            {starting ? 'در حال ارسال...' : 'همگام‌سازی اکنون'}
          </button>
        </div>

        {notice && (
          <p role="status" className="text-sm text-blue-800 bg-blue-50 border border-blue-200 rounded-lg p-3">
            {notice}
          </p>
        )}

        {loadError && (
          <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
            {loadError}
          </p>
        )}

        {running && (
          <div className="text-sm text-blue-900 bg-blue-50 border border-blue-200 rounded-lg p-3">
            همگام‌سازی در حال اجرا است (از {toPersianDigits(running.startedAtJalali)}؛ نوع:{' '}
            {TRIGGER_LABELS[running.trigger]}). این فهرست هر ۳ ثانیه به‌روز می‌شود.
          </div>
        )}

        {schedule && (
          <p className="text-sm text-gray-700">
            همگام‌سازی شبانه ساعت{' '}
            <span className="font-medium">{toPersianDigits(schedule.nightlyTime)}</span> ({schedule.timeZone}). اجرای بعدی:{' '}
            <span className="font-medium">{schedule.nextRunAtJalali ? toPersianDigits(schedule.nextRunAtJalali) : '—'}</span>
          </p>
        )}

        {source && source.configured === false && (
          <p role="alert" className="text-sm text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3">
            همگام‌سازی پیکربندی نشده است. مدیر فنی باید منبع و نقشه منبع را تنظیم کند.
          </p>
        )}

        {source && source.configured && source.mapValid === false && (
          <div role="alert" className="text-sm text-amber-900 bg-amber-50 border border-amber-200 rounded-lg p-3 space-y-2">
            <p className="font-medium">نقشه منبع معتبر نیست؛ همگام‌سازی آغاز نمی‌شود.</p>
            <ul className="list-disc pr-5 space-y-1">
              {source.mapProblems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          </div>
        )}
      </section>

      <section className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-4">
        <h3 className="text-base font-semibold text-gray-900">تاریخچه اجراها</h3>

        {data && data.jobs.length === 0 && (
          <p className="text-sm text-gray-600">هنوز همگام‌سازی اجرا نشده است.</p>
        )}

        {data && data.jobs.length > 0 && (
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm text-right">
              <thead className="text-xs text-gray-600 border-b border-gray-200">
                <tr>
                  <th className="py-2 px-2 font-medium">شناسه</th>
                  <th className="py-2 px-2 font-medium">نوع اجرا</th>
                  <th className="py-2 px-2 font-medium">حالت</th>
                  <th className="py-2 px-2 font-medium">وضعیت</th>
                  <th className="py-2 px-2 font-medium">شروع</th>
                  <th className="py-2 px-2 font-medium">پایان</th>
                  <th className="py-2 px-2 font-medium">مدت</th>
                  <th className="py-2 px-2 font-medium">جدول‌ها</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.jobs.map((job) => (
                  <tr key={job.id} className="align-top">
                    <td className="py-2 px-2 text-gray-700">{toPersianDigits(job.id)}</td>
                    <td className="py-2 px-2 text-gray-700">
                      {TRIGGER_LABELS[job.trigger]}
                      {job.requestedBy && <div className="text-xs text-gray-500">{job.requestedBy}</div>}
                    </td>
                    <td className="py-2 px-2 text-gray-700">{MODE_LABELS[job.mode]}</td>
                    <td className="py-2 px-2">
                      <StatusBadge status={job.status} />
                      {job.skipReason && <div className="mt-1 text-xs text-gray-600">{job.skipReason}</div>}
                      {job.error && <div className="mt-1 text-xs text-red-700">{job.error}</div>}
                    </td>
                    <td className="py-2 px-2 text-gray-700 whitespace-nowrap">{toPersianDigits(job.startedAtJalali)}</td>
                    <td className="py-2 px-2 text-gray-700 whitespace-nowrap">
                      {job.finishedAtJalali ? toPersianDigits(job.finishedAtJalali) : '—'}
                    </td>
                    <td className="py-2 px-2 text-gray-700 whitespace-nowrap">{durationText(job.durationMs)}</td>
                    <td className="py-2 px-2">
                      <TableCounts tables={job.tables} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
