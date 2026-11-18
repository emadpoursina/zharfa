import type { ReportUnavailable } from '../services/api';

const REASON_TEXT: Record<string, string> = {
  'no-successful-sync': 'هنوز همگام‌سازی موفقی انجام نشده است',
  'source-item-unverified': 'این بخش هنوز در منبع تأیید نشده است',
  'source-item-unmapped': 'این بخش در تنظیمات منبع نگاشت نشده است',
  'no-previous-data': 'داده دوره قبل همگام‌سازی نشده است',
};

const FIGURE_TEXT: Record<string, string> = {
  category: 'گروه دارویی',
  vendorCode: 'کد تأمین‌کننده',
  quantity: 'تعداد',
  comparison: 'مقایسه با دوره قبل',
  net: 'خرید خالص',
  vendorReport: 'گزارش تأمین‌کنندگان',
  productReport: 'گزارش کالاها',
  returnsReport: 'گزارش برگشت از خرید',
  transactions: 'فهرست اسناد',
  topProducts: 'پرفروش‌ترین کالاها',
};

interface UnavailableNoteProps {
  /** The unavailable entries of the response (envelope `unavailable`). */
  items: ReportUnavailable[] | undefined;
  /** Show only the entries for this figure; omitted = all entries. */
  figure?: string;
}

/**
 * «داده در دسترس نیست» with the reason in words (spec 004 T039). Renders nothing when the figure is available.
 */
export function UnavailableNote({ items, figure }: UnavailableNoteProps) {
  const matching = (items ?? []).filter((item) => figure === undefined || item.figure === figure);
  if (matching.length === 0) {
    return null;
  }

  return (
    <div role="status" className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900">
      <p className="font-semibold">داده در دسترس نیست</p>
      <ul className="mt-1 list-disc ps-5 text-xs">
        {matching.map((item) => (
          <li key={`${item.figure}-${item.reason}`}>
            {FIGURE_TEXT[item.figure] ?? item.figure}: {REASON_TEXT[item.reason] ?? item.reason}
          </li>
        ))}
      </ul>
    </div>
  );
}
