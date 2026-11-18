import { useEffect, useState, type FormEvent } from 'react';
import { DataModeBadge } from '../components/DataModeBadge';
import { Pager } from '../components/Pager';
import type { PageSize } from '../hooks/useReportQuery';
import { ApiError, getAuditActions, getAuditLog, type AuditAction, type AuditPage } from '../services/api';
import { toPersianDigits } from '../utils/jalali';

/** S-AUDIT-EMPTY (contracts/api.md §9). */
const EMPTY_TEXT = 'رویدادی با این فیلترها یافت نشد.';
const NETWORK_ERROR = 'خطا در برقراری ارتباط با سرور.';
const INPUT_CLASS =
  'px-3 py-2 rounded-lg border border-gray-300 text-gray-900 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 disabled:bg-gray-100 disabled:cursor-not-allowed transition';
const BUTTON_CLASS =
  'px-3 py-1.5 text-xs sm:text-sm font-medium rounded-lg border transition cursor-pointer focus:outline-hidden focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-1 disabled:opacity-60 disabled:cursor-not-allowed';
const PRIMARY_BUTTON = `${BUTTON_CLASS} text-white bg-blue-600 border-blue-600 hover:bg-blue-700`;
const SECONDARY_BUTTON = `${BUTTON_CLASS} text-gray-800 bg-white border-gray-300 hover:bg-gray-100`;
const DEFAULT_PAGE_SIZE: PageSize = 50;

/** The filter values as typed (draft) and as applied to the list. */
interface AuditFilters {
  user: string;
  action: string;
  from: string;
  to: string;
}

const NO_FILTERS: AuditFilters = { user: '', action: '', from: '', to: '' };

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : NETWORK_ERROR;
}

/**
 * Audit log for the Admin (contracts/api.md §3, FR-016, FR-017): filter by user, action and Jalali date range, newest
 * first, server-paged with the total. Read only; the server writes the audit entry for the first page.
 */
export function AuditLog() {
  const [draft, setDraft] = useState<AuditFilters>(NO_FILTERS);
  const [applied, setApplied] = useState<AuditFilters>(NO_FILTERS);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(DEFAULT_PAGE_SIZE);
  const [actions, setActions] = useState<AuditAction[]>([]);
  const [data, setData] = useState<AuditPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // The action catalog is loaded once; a failure only leaves the action filter with «همه».
  useEffect(() => {
    let active = true;
    getAuditActions()
      .then((loaded) => {
        if (active) {
          setActions(loaded);
        }
      })
      .catch(() => {
        if (active) {
          setActions([]);
        }
      });
    return () => {
      active = false;
    };
  }, []);

  // One request per filter, page or page size. A stale answer is ignored.
  useEffect(() => {
    let active = true;
    setLoading(true);
    getAuditLog({
      user: applied.user.trim(),
      action: applied.action,
      from: applied.from.trim(),
      to: applied.to.trim(),
      page,
      pageSize,
    })
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
  }, [applied, page, pageSize]);

  const handleApply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setApplied(draft);
    setPage(1);
  };

  const handleClear = () => {
    setDraft(NO_FILTERS);
    setApplied(NO_FILTERS);
    setPage(1);
  };

  const rows = data?.rows ?? [];

  return (
    <div className="space-y-6" dir="rtl">
      <section aria-labelledby="audit-heading" className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 id="audit-heading" className="text-lg font-semibold text-gray-900">
              گزارش رویدادها
            </h2>
            <p className="mt-1 text-sm text-gray-600">
              ورودها، باز کردن و چاپ گزارش‌ها، تغییرات مدیریتی و رد دسترسی‌ها. ردیف‌ها از جدیدترین به قدیمی‌ترین مرتب شده‌اند.
            </p>
          </div>
          {data && <DataModeBadge dataLabel={data.dataLabel} />}
        </div>

        <form onSubmit={handleApply} className="grid gap-4 sm:grid-cols-4 items-end" noValidate>
          <div className="space-y-1.5">
            <label htmlFor="audit-user" className="block text-sm font-semibold text-gray-700">
              کاربر
            </label>
            <input
              id="audit-user"
              type="text"
              dir="ltr"
              autoComplete="off"
              value={draft.user}
              onChange={(event) => setDraft({ ...draft, user: event.target.value })}
              className={`${INPUT_CLASS} w-full`}
            />
          </div>
          <div className="space-y-1.5">
            <label htmlFor="audit-action" className="block text-sm font-semibold text-gray-700">
              عملیات
            </label>
            <select
              id="audit-action"
              value={draft.action}
              onChange={(event) => setDraft({ ...draft, action: event.target.value })}
              className={`${INPUT_CLASS} w-full`}
            >
              <option value="">همه</option>
              {actions.map((action) => (
                <option key={action.code} value={action.code}>
                  {action.label}
                </option>
              ))}
            </select>
          </div>
          <div className="space-y-1.5">
            <label htmlFor="audit-from" className="block text-sm font-semibold text-gray-700">
              از تاریخ (شمسی)
            </label>
            <input
              id="audit-from"
              type="text"
              dir="ltr"
              inputMode="numeric"
              placeholder="۱۴۰۵-۰۸-۰۱"
              value={draft.from}
              onChange={(event) => setDraft({ ...draft, from: event.target.value })}
              className={`${INPUT_CLASS} w-full`}
            />
          </div>
          <div className="space-y-1.5">
            <label htmlFor="audit-to" className="block text-sm font-semibold text-gray-700">
              تا تاریخ (شمسی)
            </label>
            <input
              id="audit-to"
              type="text"
              dir="ltr"
              inputMode="numeric"
              placeholder="۱۴۰۵-۰۸-۲۷"
              value={draft.to}
              onChange={(event) => setDraft({ ...draft, to: event.target.value })}
              className={`${INPUT_CLASS} w-full`}
            />
          </div>
          <div className="flex flex-wrap gap-2 sm:col-span-4">
            <button type="submit" disabled={loading} className={PRIMARY_BUTTON}>
              اعمال فیلتر
            </button>
            <button type="button" onClick={handleClear} disabled={loading} className={SECONDARY_BUTTON}>
              پاک کردن فیلترها
            </button>
          </div>
        </form>

        {error && (
          <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
            {error}
          </p>
        )}

        {loading && !data && <p className="text-sm text-gray-600">در حال بارگذاری...</p>}

        {data && (
          <>
            <Pager
              page={page}
              pageSize={pageSize}
              totalRows={data.total}
              disabled={loading}
              onPage={setPage}
              onPageSize={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />

            {rows.length === 0 ? (
              <p className="text-sm text-gray-700 bg-gray-50 border border-gray-200 rounded-lg p-4">{EMPTY_TEXT}</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full text-sm">
                  <caption className="sr-only">رویدادهای ثبت‌شده</caption>
                  <thead className="bg-gray-50 text-gray-700">
                    <tr>
                      <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                        زمان
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                        کاربر
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-semibold whitespace-nowrap">
                        عملیات
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-semibold">
                        هدف
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-semibold">
                        جزئیات
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {rows.map((row) => (
                      <tr key={row.id} className="align-top">
                        <td className="px-3 py-2 whitespace-nowrap text-gray-800">{toPersianDigits(row.atJalali)}</td>
                        <td className="px-3 py-2 whitespace-nowrap text-gray-800" dir="ltr">
                          {row.actor ?? '—'}
                        </td>
                        <td className="px-3 py-2 whitespace-nowrap text-gray-900 font-medium">{row.actionLabel}</td>
                        <td className="px-3 py-2 text-gray-700 break-all" dir="ltr">
                          {row.target ?? '—'}
                        </td>
                        <td className="px-3 py-2 text-gray-700 break-all" dir="ltr">
                          {row.detail ?? '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  );
}
