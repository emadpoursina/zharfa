import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import {
  saveReport,
  ApiError,
  type SavedReportItem,
} from '../services/api';

interface SaveReportDialogProps {
  open: boolean;
  kind: string;
  defaultName?: string;
  period: unknown;
  filters?: unknown;
  generated?: unknown;
  onClose: () => void;
  onSaved?: (item: SavedReportItem) => void;
}

const FOCUSABLE = 'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal dialog for saving a standard or generated report (spec 007 FR-007, FR-011, research R7).
 * Replaces or creates; prompts for overwrite confirmation on 409 saved-report-exists;
 * surfaces cap refusal on 409 saved-report-cap. Accessible with focus trap and Escape.
 */
export function SaveReportDialog({
  open,
  kind,
  defaultName = '',
  period,
  filters,
  generated,
  onClose,
  onSaved,
}: SaveReportDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const openerRef = useRef<HTMLElement | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const [name, setName] = useState(defaultName);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [confirmOverwrite, setConfirmOverwrite] = useState(false);

  useEffect(() => {
    if (!open) {
      return;
    }
    openerRef.current = document.activeElement as HTMLElement | null;
    setName(defaultName);
    setError(null);
    setConfirmOverwrite(false);

    const timer = setTimeout(() => {
      inputRef.current?.focus();
      inputRef.current?.select();
    }, 50);

    return () => {
      clearTimeout(timer);
      openerRef.current?.focus();
    };
  }, [open, defaultName]);

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation();
      onClose();
      return;
    }

    if (e.key !== 'Tab') {
      return;
    }

    const items = dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE);
    if (!items || items.length === 0) {
      return;
    }

    const first = items[0];
    const last = items[items.length - 1];

    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const executeSave = async (overwrite: boolean) => {
    const trimmed = name.trim();
    if (!trimmed) {
      setError('نام گزارش نمی‌تواند خالی باشد.');
      inputRef.current?.focus();
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const res = await saveReport({
        name: trimmed,
        kind,
        period,
        filters,
        generated,
        overwrite,
      });

      onSaved?.(res);
      onClose();
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409 && err.code === 'saved-report-exists') {
          setConfirmOverwrite(true);
        } else if (err.status === 409 && err.code === 'saved-report-cap') {
          setError(err.message || 'حداکثر ۵۰ گزارش ذخیره شده مجاز است. ابتدا یکی را حذف کنید.');
        } else {
          setError(err.message || 'خطا در ذخیره گزارش.');
        }
      } else {
        setError('خطا در برقراری ارتباط با سرور.');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (confirmOverwrite) {
      executeSave(true);
    } else {
      executeSave(false);
    }
  };

  if (!open) {
    return null;
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="save-report-title"
      onKeyDown={handleKeyDown}
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-gray-900/50 backdrop-blur-xs"
      dir="rtl"
    >
      <div
        ref={dialogRef}
        className="w-full max-w-md bg-white rounded-2xl shadow-xl border border-gray-200 overflow-hidden"
      >
        <header className="px-6 py-4 border-b border-gray-100 flex items-center justify-between">
          <h3 id="save-report-title" className="text-base font-bold text-gray-900">
            {confirmOverwrite ? 'تأیید جایگزینی گزارش' : 'ذخیره گزارش'}
          </h3>
          <button
            type="button"
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 rounded-lg p-1 transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
            aria-label="بستن"
          >
            ✕
          </button>
        </header>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {confirmOverwrite ? (
            <div className="space-y-3">
              <p className="text-sm text-gray-800 leading-relaxed">
                گزارشی با نام <strong className="text-gray-900">«{name.trim()}»</strong> قبلاً ذخیره شده است. آیا مایلید مشخصات و فیلترهای جدید جایگزین آن شوند؟
              </p>
              <div className="p-3 bg-amber-50 border border-amber-200 rounded-xl text-xs text-amber-900 font-medium">
                در صورت جایگزینی، گزارش قبلی به‌روزرسانی شده و شناسه آن حفظ می‌گردد.
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              <label htmlFor="saved-report-name" className="block text-xs font-semibold text-gray-700">
                نام گزارش:
              </label>
              <input
                ref={inputRef}
                id="saved-report-name"
                type="text"
                value={name}
                onChange={(e) => {
                  setName(e.target.value);
                  setError(null);
                }}
                disabled={loading}
                placeholder="مثلاً: ۱۰ کالای برتر شش‌ماهه"
                className="w-full px-3.5 py-2.5 text-sm text-gray-900 border border-gray-300 rounded-xl focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 transition"
                autoComplete="off"
              />
              <p className="text-xs text-gray-500">
                گزارش‌های ذخیره‌شده فقط برای حساب کاربری شما قابل مشاهده خواهند بود.
              </p>
            </div>
          )}

          {error && (
            <div role="alert" className="p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-800 font-medium">
              {error}
            </div>
          )}

          <footer className="pt-2 flex items-center justify-end gap-3">
            {confirmOverwrite ? (
              <>
                <button
                  type="button"
                  onClick={() => setConfirmOverwrite(false)}
                  disabled={loading}
                  className="px-4 py-2 text-xs font-semibold text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-gray-400"
                >
                  انصراف
                </button>
                <button
                  type="button"
                  onClick={() => executeSave(true)}
                  disabled={loading}
                  className="px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
                >
                  {loading ? 'در حال جایگزینی...' : 'بله، جایگزین شود'}
                </button>
              </>
            ) : (
              <>
                <button
                  type="button"
                  onClick={onClose}
                  disabled={loading}
                  className="px-4 py-2 text-xs font-semibold text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-gray-400"
                >
                  لغو
                </button>
                <button
                  type="submit"
                  disabled={loading || !name.trim()}
                  className={`px-5 py-2 text-xs font-semibold text-white rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600 ${
                    loading || !name.trim()
                      ? 'bg-blue-400 cursor-not-allowed'
                      : 'bg-blue-600 hover:bg-blue-700'
                  }`}
                >
                  {loading ? 'در حال ذخیره...' : 'ذخیره گزارش'}
                </button>
              </>
            )}
          </footer>
        </form>
      </div>
    </div>
  );
}
