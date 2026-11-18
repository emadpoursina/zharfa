import { toPersianDigits } from '../utils/jalali';
import { PAGE_SIZES, type PageSize } from '../hooks/useReportQuery';

interface PagerProps {
  page: number;
  pageSize: PageSize;
  totalRows: number;
  onPage: (page: number) => void;
  onPageSize: (size: PageSize) => void;
  disabled?: boolean;
}

/** Page, page size (25 / 50 / 100) and total rows, in Persian digits (spec 004 T039). */
export function Pager({ page, pageSize, totalRows, onPage, onPageSize, disabled = false }: PagerProps) {
  const pageCount = Math.max(1, Math.ceil(totalRows / pageSize));
  const selectClass =
    'px-2 py-1 text-sm text-gray-800 bg-white border border-gray-300 rounded-lg focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-60';
  const buttonClass =
    'px-3 py-1 text-sm font-medium text-gray-800 bg-white border border-gray-300 rounded-lg hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer';

  return (
    <nav aria-label="صفحه‌بندی جدول" className="flex flex-wrap items-center justify-between gap-3 text-sm text-gray-700">
      <p>
        {toPersianDigits(totalRows)} ردیف
        {' · '}
        صفحه {toPersianDigits(page)} از {toPersianDigits(pageCount)}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex items-center gap-2">
          <span>ردیف در هر صفحه</span>
          <select
            className={selectClass}
            value={pageSize}
            disabled={disabled}
            onChange={(event) => onPageSize(Number(event.target.value) as PageSize)}
          >
            {PAGE_SIZES.map((size) => (
              <option key={size} value={size}>
                {toPersianDigits(size)}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className={buttonClass} disabled={disabled || page <= 1} onClick={() => onPage(page - 1)}>
          قبلی
        </button>
        <button
          type="button"
          className={buttonClass}
          disabled={disabled || page >= pageCount}
          onClick={() => onPage(page + 1)}
        >
          بعدی
        </button>
      </div>
    </nav>
  );
}
