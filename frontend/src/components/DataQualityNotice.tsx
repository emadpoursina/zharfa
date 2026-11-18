import type { DataQualityNotice as DataQualityNoticeData } from '../services/api';
import { faNum } from '../utils/format';

interface DataQualityNoticeProps {
  notice?: DataQualityNoticeData | null;
  showLink: boolean;
}

export function DataQualityNotice({ notice, showLink }: DataQualityNoticeProps) {
  if (!notice) {
    return null;
  }

  if (notice.unavailable) {
    return (
      <div
        role="status"
        className="rounded-lg border border-gray-300 bg-gray-50 p-3 text-xs text-gray-700 sm:text-sm"
      >
        بررسی کیفیت داده برای این گزارش اکنون ممکن نشد؛ ارقام گزارش تغییری نکرده‌اند.
      </div>
    );
  }

  const typesQuery = notice.issueTypes.map((t) => t.type).join(',');

  return (
    <div
      role="status"
      className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-xs text-amber-950 sm:text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="space-y-1">
          <p className="font-medium">
            هشدار کیفیت داده: ارقام این گزارش شامل {faNum(notice.affectedRecords)} رکورد دارای مشکل داده است.
          </p>
          {notice.issueTypes.length > 0 && (
            <ul className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-amber-900">
              {notice.issueTypes.map((item) => (
                <li key={item.type}>
                  <span>{item.label}:</span> <span className="font-semibold">{faNum(item.count)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
        {showLink && (
          <a
            href={`#/settings/data-quality?types=${encodeURIComponent(typesQuery)}`}
            className="rounded-md bg-amber-100 px-2.5 py-1 text-xs font-semibold text-amber-900 hover:bg-amber-200 focus:outline-hidden focus:ring-2 focus:ring-amber-500 print:hidden"
          >
            مشاهده در کیفیت داده
          </a>
        )}
      </div>
    </div>
  );
}
