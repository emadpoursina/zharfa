import { useState, type FormEvent } from 'react';
import {
  postPurchasingChat,
  downloadAiVisualExport,
  ApiError,
  type PurchasingChatResponse,
} from '../services/api';
import { RankingBarChart } from '../components/RankingBarChart';
import { SaveReportDialog } from '../components/SaveReportDialog';
import { faRialFromDecimal } from '../utils/format';
import { toPersianDigits } from '../utils/jalali';

const WAIT_MESSAGE = 'لطفاً صبر کنید تا پاسخ پرسش فعلی آماده شود.';
const ASSISTANT_UNAVAILABLE_FALLBACK = 'پاسخی ارائه نشد؛ دستیار هوشمند در حال حاضر در دسترس نیست.';

interface AssistantProps {
  canExport?: boolean;
}

export function Assistant({ canExport }: AssistantProps) {
  const [question, setQuestion] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [result, setResult] = useState<PurchasingChatResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [waitNotice, setWaitNotice] = useState<string | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [savedSuccess, setSavedSuccess] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const handleExport = async () => {
    if (!result?.visual?.saveSpec) return;
    setExporting(true);
    setExportError(null);
    try {
      await downloadAiVisualExport(result.visual.saveSpec);
    } catch (caught) {
      setExportError(caught instanceof Error ? caught.message : 'دریافت خروجی اکسل انجام نشد.');
    } finally {
      setExporting(false);
    }
  };

  const handleSubmit = async (e?: FormEvent) => {
    if (e) {
      e.preventDefault();
    }

    if (isLoading) {
      setWaitNotice(WAIT_MESSAGE);
      return;
    }

    const trimmed = question.trim();
    if (!trimmed) {
      return;
    }

    setIsLoading(true);
    setError(null);
    setWaitNotice(null);

    try {
      const res = await postPurchasingChat(trimmed);
      setResult(res);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          setError(WAIT_MESSAGE);
        } else if (err.status === 503) {
          setError(err.message || ASSISTANT_UNAVAILABLE_FALLBACK);
        } else {
          setError(err.message || 'خطا در برقراری ارتباط با دستیار هوشمند.');
        }
      } else {
        setError(ASSISTANT_UNAVAILABLE_FALLBACK);
      }
      setResult(null);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6" dir="rtl">
      {/* Header */}
      <header className="space-y-1">
        <h1 className="text-xl font-bold text-gray-900">دستیار هوشمند خرید</h1>
        <p className="text-xs text-gray-600">
          پرسش به زبان فارسی از شاخص‌ها و داده‌های تجمیعی خرید؛ پاسخ‌ها بر اساس تعریف‌های استاندارد گزارش‌ها محاسبه و مستند می‌شوند.
        </p>
      </header>

      {/* Question Form */}
      <section className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs space-y-4">
        <form onSubmit={handleSubmit} className="space-y-3">
          <label htmlFor="ai-question" className="block text-xs font-semibold text-gray-800">
            پرسش خود را بنویسید:
          </label>
          <textarea
            id="ai-question"
            rows={3}
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                handleSubmit();
              }
            }}
            placeholder="مثال: خالص خرید این ماه چقدر است؟ یا بیشترین خرید مربوط به کدام تأمین‌کننده بوده است؟"
            className="w-full px-3 py-2 text-sm text-gray-900 border border-gray-300 rounded-lg focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600"
            disabled={isLoading}
          />
          <div className="flex items-center justify-between">
            <span className="text-xs text-gray-500">برای ارسال سریع می‌توانید از کلیدهای ترکیبی Ctrl+Enter استفاده کنید.</span>
            <button
              type="submit"
              disabled={isLoading || !question.trim()}
              aria-busy={isLoading}
              className={`px-5 py-2 rounded-lg text-xs font-semibold text-white transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:ring-offset-2 ${
                isLoading || !question.trim()
                  ? 'bg-blue-400 cursor-not-allowed'
                  : 'bg-blue-600 hover:bg-blue-700'
              }`}
            >
              {isLoading ? 'در حال دریافت پاسخ...' : 'ارسال پرسش'}
            </button>
          </div>
        </form>

        {waitNotice && (
          <div role="status" className="p-3 bg-amber-50 border border-amber-200 text-amber-800 text-xs rounded-lg font-medium">
            {waitNotice}
          </div>
        )}
      </section>

      {/* Loading state */}
      {isLoading && (
        <div className="bg-white rounded-xl border border-gray-200 p-12 text-center space-y-3">
          <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
          <p className="text-xs font-medium text-gray-600">در حال دریافت داده‌ها و تحلیل پاسخ...</p>
        </div>
      )}

      {/* Error state / 503 / 409 */}
      {error && !isLoading && (
        <div role="alert" className="p-4 bg-rose-50 border border-rose-200 text-rose-800 text-xs rounded-xl font-medium">
          {error}
        </div>
      )}

      {/* Answer Result */}
      {result && !isLoading && (
        <section className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs space-y-6">
          {/* Demo Notice if present */}
          {result.demoNotice && (
            <div className="p-3 bg-amber-50 border border-amber-200 text-amber-900 text-xs rounded-lg font-medium">
              {result.demoNotice}
            </div>
          )}

          {/* Metric Unavailable Notice */}
          {result.unavailable && (
            <div className="p-4 bg-gray-50 border border-gray-200 text-gray-800 text-sm rounded-lg font-medium">
              داده در دسترس نیست. برای فیلترها یا دوره درخواستی پاسخی استخراج نشد.
            </div>
          )}

          {/* Visual Table & Chart (F14) */}
          {!result.unavailable && result.visual && (
            <div className="space-y-4 pt-2">
              {/* Captions */}
              <div className="bg-gray-50 border border-gray-200 rounded-lg p-3 text-xs text-gray-700 flex flex-wrap gap-4">
                <span>دوره: <strong className="text-gray-900">{toPersianDigits(result.visual.captions.period)}</strong></span>
                <span>فیلترها: <strong className="text-gray-900">{result.visual.captions.filters}</strong></span>
                <span>شاخص: <strong className="text-gray-900">{result.visual.captions.metrics}</strong></span>
              </div>

              {/* Data Quality Notice if flagged */}
              {result.visual.dataQuality && result.visual.dataQuality.affectedRecords > 0 && (
                <div role="status" className="p-3 bg-amber-50 border border-amber-200 text-amber-900 text-xs rounded-lg font-medium">
                  توجه کیفیت داده: {toPersianDigits(result.visual.dataQuality.affectedRecords)} رکورد پرچم‌دار در این بازه وجود دارد.
                </div>
              )}

              {/* Table */}
              <div className="overflow-x-auto border border-gray-200 rounded-xl">
                <table className="w-full text-xs text-right divide-y divide-gray-200">
                  <thead className="bg-gray-50 text-gray-700 font-semibold">
                    <tr>
                      <th scope="col" className="px-4 py-3 w-12 text-center">ردیف</th>
                      {result.visual.table.columns.map((col) => (
                        <th key={col.id} scope="col" className="px-4 py-3">
                          {col.header}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 bg-white">
                    {result.visual.table.rows.length === 0 ? (
                      <tr>
                        <td colSpan={result.visual.table.columns.length + 1} className="px-4 py-6 text-center text-gray-500 font-medium">
                          هیچ رکوردی برای این بازه یافت نشد.
                        </td>
                      </tr>
                    ) : (
                      result.visual.table.rows.map((row, idx) => (
                        <tr key={row.key || idx} className="hover:bg-gray-50/80 transition-colors">
                          <td className="px-4 py-2.5 text-center text-gray-500 font-medium">
                            {toPersianDigits(idx + 1)}
                          </td>
                          {result.visual!.table.columns.map((col) => {
                            const val = row[col.id];
                            const isAmount = col.id === 'net' || col.id === 'gross' || col.id === 'returns' || col.id === 'vat';
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

              {/* Chart */}
              {result.visual.chart && result.visual.table.rows.length >= 2 && (
                <RankingBarChart chart={result.visual.chart} rows={result.visual.table.rows} />
              )}

              {/* Action buttons (Keep / Save / Export) */}
              <div className="flex items-center justify-end gap-3 pt-2">
                {savedSuccess && (
                  <span className="text-xs text-emerald-700 font-medium">
                    ✓ گزارش با موفقیت در گزارش‌های ذخیره‌شده نگه‌داری شد.
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => setSaveOpen(true)}
                  className="px-4 py-2 text-xs font-semibold text-blue-700 bg-blue-50 hover:bg-blue-100 border border-blue-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
                >
                  ذخیره در گزارش‌ها
                </button>
                {canExport && (
                  <button
                    type="button"
                    onClick={handleExport}
                    disabled={exporting}
                    className="px-4 py-2 text-xs font-semibold text-emerald-800 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 rounded-xl transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-emerald-600 disabled:opacity-50"
                  >
                    {exporting ? 'در حال دریافت...' : 'خروجی اکسل'}
                  </button>
                )}
              </div>
              {exportError && (
                <div role="alert" className="p-3 bg-rose-50 border border-rose-200 rounded-xl text-xs text-rose-800 font-medium text-left">
                  {exportError}
                </div>
              )}

              {saveOpen && result.visual.saveSpec && (
                <SaveReportDialog
                  open={saveOpen}
                  kind="generated"
                  defaultName={question.trim()}
                  period={result.visual.saveSpec.period}
                  filters={result.visual.saveSpec.filters}
                  generated={result.visual.saveSpec.generated}
                  onClose={() => setSaveOpen(false)}
                  onSaved={() => {
                    setSavedSuccess(true);
                    setTimeout(() => setSavedSuccess(false), 5000);
                  }}
                />
              )}
            </div>
          )}

          {/* Facts Section */}
          {!result.unavailable && result.facts && result.facts.length > 0 && (
            <div className="space-y-2">
              <h2 className="text-xs font-bold text-gray-800">حقایق مستند استخراج‌شده از داده‌ها:</h2>
              <ul className="list-disc list-inside space-y-1 text-xs text-gray-700 bg-gray-50 p-3 rounded-lg font-mono">
                {result.facts.map((fact, idx) => (
                  <li key={idx} className="break-all">{fact}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Interpretation / Answer Section */}
          {!result.unavailable && result.answer && (
            <div className="space-y-2">
              <h2 className="text-xs font-bold text-gray-800">پاسخ و تحلیل:</h2>
              <div className="p-4 bg-blue-50/40 border border-blue-100 rounded-lg text-sm text-gray-900 leading-relaxed whitespace-pre-wrap">
                {result.answer}
              </div>
            </div>
          )}

          {/* Metadata Citation */}
          {(result.view || result.period?.jalaliLabel || result.period?.from) && (
            <footer className="pt-4 border-t border-gray-100 flex flex-wrap gap-4 text-xs text-gray-500">
              {result.period?.jalaliLabel ? (
                <span>دوره زمانی: <strong className="text-gray-700">{result.period.jalaliLabel}</strong></span>
              ) : result.period?.from && result.period?.to ? (
                <span>دوره زمانی: <strong className="text-gray-700">{result.period.from} تا {result.period.to}</strong></span>
              ) : null}
              {result.filters && (result.filters.vendor || result.filters.product || result.filters.category) && (
                <span>
                  فیلترها:{' '}
                  <span className="text-gray-700 font-medium">
                    {[
                      result.filters.vendor && `تأمین‌کننده: ${result.filters.vendor}`,
                      result.filters.product && `کالا: ${result.filters.product}`,
                      result.filters.category && `گروه: ${result.filters.category}`,
                    ]
                      .filter(Boolean)
                      .join(' | ')}
                  </span>
                </span>
              )}
              {result.view && (
                <span>نمای مرجع داده: <code className="text-gray-700 bg-gray-100 px-1 py-0.5 rounded">{result.view}</code></span>
              )}
            </footer>
          )}
        </section>
      )}
    </div>
  );
}
