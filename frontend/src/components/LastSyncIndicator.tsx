import type { LastSuccessfulSync } from '../services/api';
import { toPersianDigits } from '../utils/jalali';

interface LastSyncIndicatorProps {
  lastSuccessfulSync: LastSuccessfulSync | null;
  /**
   * True when the system status could not be loaded, so the last sync is not known.
   * Shows neutral text instead of implying that no sync has ever succeeded.
   */
  isUnknown?: boolean;
}

/**
 * Last successful Mabna sync, shown in the header of every view (FR-012).
 * The server sends the Jalali time already; Persian digits are enforced here too.
 */
export function LastSyncIndicator({ lastSuccessfulSync, isUnknown = false }: LastSyncIndicatorProps) {
  if (isUnknown) {
    return (
      <p className="text-xs text-gray-500">
        وضعیت آخرین همگام‌سازی در دسترس نیست
      </p>
    );
  }

  if (lastSuccessfulSync === null) {
    return (
      <p className="text-xs text-amber-800 bg-amber-50 border border-amber-200 rounded-md px-2 py-1">
        هنوز همگام‌سازی موفقی انجام نشده است
      </p>
    );
  }

  return (
    <p className="text-xs text-gray-600">
      آخرین همگام‌سازی موفق:{' '}
      <time dateTime={lastSuccessfulSync.at} className="font-medium text-gray-800">
        {toPersianDigits(lastSuccessfulSync.atJalali)}
      </time>
    </p>
  );
}
