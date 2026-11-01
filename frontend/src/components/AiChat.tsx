import { useEffect, useRef, useState } from 'react';
import { apiPost, ApiError } from '../services/api';
import { toPersianDigits } from '../utils/jalali';

export interface CategoryItem {
  categoryId: number;
  name: string;
}

export interface PeriodInfo {
  from: string;
  to: string;
  jalaliLabel: string;
  prev?: {
    from: string;
    to: string;
    jalaliLabel: string;
  };
}

export interface FiltersInfo {
  categoryId: number | null;
}

export interface AiChatResponse {
  demoData: boolean;
  answer: string;
  period: PeriodInfo;
  filters: FiltersInfo;
  view: string | null;
  facts: string[];
  interpretation: string;
  unavailable: boolean;
  message?: string;
}

export interface AiChatProps {
  from: string;
  to: string;
  jalaliLabel: string;
  categoryId: number | null;
  categories?: CategoryItem[];
}

interface ChatMessage {
  id: string;
  sender: 'user' | 'assistant';
  question?: string;
  citationJalali?: string;
  citationCategory?: string;
  view?: string | null;
  answer?: string;
  facts?: string[];
  interpretation?: string;
  unavailable?: boolean;
  unavailableMessage?: string;
  periodEcho?: {
    jalaliLabel: string;
    prevJalaliLabel?: string;
  };
  isError?: boolean;
  errorMessage?: string;
}

const SUGGESTIONS = [
  'میزان فروش این ماه چقدر بوده؟',
  'کدام مشتریان بیشترین بدهی را دارند؟',
  'چه محصولاتی موجودی کمی دارند؟',
];

function resolveCategoryLabel(
  catId: number | null | undefined,
  categories?: CategoryItem[]
): string {
  if (catId === null || catId === undefined) {
    return 'همه دسته‌ها';
  }
  const found = categories?.find((c) => c.categoryId === catId);
  return found ? found.name : `دسته‌بندی ${toPersianDigits(catId)}`;
}

export function AiChat({ from, to, jalaliLabel, categoryId, categories }: AiChatProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const transcriptEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    transcriptEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, loading]);

  const handleSuggestionClick = (suggestion: string) => {
    setInput(suggestion);
    if (textareaRef.current) {
      textareaRef.current.focus();
    }
  };

  const handleSubmit = async (e?: React.FormEvent) => {
    if (e) {
      e.preventDefault();
    }
    const trimmed = input.trim();
    if (!trimmed || trimmed.length > 2000 || loading) {
      return;
    }

    const timestamp = Date.now();
    const userMsgId = `user-${timestamp}`;
    const assistantMsgId = `assistant-${timestamp}`;

    const userMsg: ChatMessage = {
      id: userMsgId,
      sender: 'user',
      question: trimmed,
    };

    setMessages((prev) => [...prev, userMsg]);
    setInput('');
    setLoading(true);

    const fallbackCategoryLabel = resolveCategoryLabel(categoryId, categories);

    try {
      const res = await apiPost<AiChatResponse>('/api/ai/chat', {
        question: trimmed,
        from,
        to,
        categoryId: categoryId !== null ? categoryId : null,
      });

      const resCategoryLabel = resolveCategoryLabel(res.filters?.categoryId ?? categoryId, categories);
      const resJalaliLabel = res.period?.jalaliLabel || jalaliLabel;

      if (res.unavailable) {
        const assistantMsg: ChatMessage = {
          id: assistantMsgId,
          sender: 'assistant',
          citationJalali: resJalaliLabel,
          citationCategory: resCategoryLabel,
          view: res.view ?? null,
          unavailable: true,
          unavailableMessage: res.message || (res.answer !== 'داده‌ای در دسترس نیست؛ پاسخ بدون مرجع داده ارائه نشد.' ? res.answer : undefined),
          periodEcho: {
            jalaliLabel: resJalaliLabel,
            prevJalaliLabel: res.period?.prev?.jalaliLabel,
          },
        };
        setMessages((prev) => [...prev, assistantMsg]);
      } else {
        const assistantMsg: ChatMessage = {
          id: assistantMsgId,
          sender: 'assistant',
          citationJalali: resJalaliLabel,
          citationCategory: resCategoryLabel,
          view: res.view ?? null,
          answer: res.answer,
          facts: Array.isArray(res.facts) ? res.facts : [],
          interpretation: res.interpretation || '',
          unavailable: false,
          periodEcho: {
            jalaliLabel: resJalaliLabel,
            prevJalaliLabel: res.period?.prev?.jalaliLabel,
          },
        };
        setMessages((prev) => [...prev, assistantMsg]);
      }
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 503) {
        const data = err.data as Record<string, unknown> | undefined;
        const periodData = data?.period as PeriodInfo | undefined;
        const bodyMsg =
          typeof data?.message === 'string' && data.message.trim().length > 0
            ? data.message.trim()
            : (err.message &&
               err.message !== 'خطایی در پردازش درخواست رخ داد.' &&
               err.message !== 'سرویس هوش مصنوعی در دسترس نیست.'
                ? err.message
                : null);

        const assistantMsg: ChatMessage = {
          id: assistantMsgId,
          sender: 'assistant',
          citationJalali: periodData?.jalaliLabel || jalaliLabel,
          citationCategory: fallbackCategoryLabel,
          view: null,
          unavailable: true,
          unavailableMessage: bodyMsg || 'سرویس هوش مصنوعی در دسترس نیست.',
          periodEcho: {
            jalaliLabel: periodData?.jalaliLabel || jalaliLabel,
            prevJalaliLabel: periodData?.prev?.jalaliLabel,
          },
        };
        setMessages((prev) => [...prev, assistantMsg]);
      } else {
        const msg =
          err instanceof ApiError
            ? (err.message || 'خطا در ارتباط با سرویس هوش مصنوعی.')
            : 'خطا در برقراری ارتباط با سرور.';

        const assistantMsg: ChatMessage = {
          id: assistantMsgId,
          sender: 'assistant',
          citationJalali: jalaliLabel,
          citationCategory: fallbackCategoryLabel,
          view: null,
          isError: true,
          errorMessage: msg,
        };
        setMessages((prev) => [...prev, assistantMsg]);
      }
    } finally {
      setLoading(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  return (
    <section className="bg-white rounded-xl border border-gray-200 p-5 shadow-xs space-y-6" dir="rtl" aria-label="تحلیلگر هوشمند کسب‌وکار">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 border-b border-gray-200 pb-4">
        <div>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-bold text-gray-900">تحلیلگر هوشمند کسب‌وکار</h2>
            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 border border-amber-300">
              داده نمایشی
            </span>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            دستیار هوش مصنوعی برای تحلیل داده‌های فروش، مطالبات و موجودی انبار
          </p>
        </div>
      </div>

      {/* Chat Transcript */}
      <div className="space-y-4">
        {messages.length === 0 ? (
          <div className="text-center py-12 px-4 bg-gray-50/60 rounded-xl border border-dashed border-gray-200">
            <div className="w-12 h-12 mx-auto mb-3 text-blue-600 bg-blue-50 rounded-full flex items-center justify-center">
              <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
            </div>
            <p className="text-sm font-semibold text-gray-700">یک پرسش بپرسید…</p>
            <p className="text-xs text-gray-500 mt-1.5 max-w-md mx-auto leading-relaxed">
              می‌توانید درباره میزان فروش این دوره، مشتریان دارای بیشترین بدهی یا وضعیت موجودی کالاها سوال بپرسید.
            </p>
          </div>
        ) : (
          <div className="space-y-4 max-h-[520px] overflow-y-auto p-1" role="log" aria-live="polite">
            {messages.map((m) => {
              if (m.sender === 'user') {
                return (
                  <div key={m.id} className="flex justify-end">
                    <div className="bg-blue-600 text-white rounded-2xl rounded-tl-xs px-4 py-3 max-w-xl shadow-xs text-sm">
                      <div className="text-[11px] text-blue-100 font-semibold mb-1">شما</div>
                      <p className="whitespace-pre-wrap leading-relaxed">{m.question}</p>
                    </div>
                  </div>
                );
              }

              if (m.isError) {
                return (
                  <div key={m.id} className="flex justify-start">
                    <div className="bg-white border border-red-200 rounded-2xl rounded-tr-xs p-4 max-w-2xl shadow-xs text-sm w-full space-y-2">
                      <div className="flex items-center gap-2 text-xs font-bold text-red-700">
                        <span className="w-2 h-2 rounded-full bg-red-500" />
                        <span>خطا</span>
                      </div>
                      <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-red-700 text-xs font-medium">
                        {m.errorMessage || 'خطا در ارتباط با سرویس هوش مصنوعی.'}
                      </div>
                    </div>
                  </div>
                );
              }

              if (m.unavailable) {
                return (
                  <div key={m.id} className="flex justify-start">
                    <div className="bg-white border border-amber-200 rounded-2xl rounded-tr-xs p-4 max-w-2xl shadow-xs text-sm w-full space-y-3">
                      {/* Citation line */}
                      <div className="flex items-center justify-between text-xs font-medium text-gray-500 border-b border-gray-100 pb-2">
                        <span>
                          بر اساس بازه‌ی {m.citationJalali} و {m.citationCategory} — {m.view ? `مرجع: ${m.view}` : 'مرجع داده‌ای: —'}
                        </span>
                        <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-100 text-amber-800 border border-amber-300 shrink-0">
                          داده نمایشی
                        </span>
                      </div>

                      {/* Unavailable Notice */}
                      <div className="rounded-lg bg-amber-50 border border-amber-200 p-3.5 text-amber-900 text-xs space-y-1">
                        <div className="font-semibold">
                          سرویس هوش مصنوعی در دسترس نیست یا داده‌ای برای این پرسش وجود ندارد.
                        </div>
                        {m.unavailableMessage && (
                          <div className="text-amber-800 leading-relaxed">{m.unavailableMessage}</div>
                        )}
                      </div>

                      {/* Period echo */}
                      {m.periodEcho && (
                        <div className="text-xs text-gray-500 mt-2.5 pt-2 border-t border-gray-100 flex flex-wrap items-center gap-1.5">
                          <span>بازه تحلیل: {m.periodEcho.jalaliLabel}</span>
                          {m.periodEcho.prevJalaliLabel && (
                            <span className="text-gray-400">
                              (دوره مقایسه: {m.periodEcho.prevJalaliLabel})
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                );
              }

              return (
                <div key={m.id} className="flex justify-start">
                  <div className="bg-white border border-gray-200 rounded-2xl rounded-tr-xs p-4 max-w-2xl shadow-xs text-sm w-full space-y-3">
                    {/* Header line (citation) */}
                    <div className="flex items-center justify-between text-xs font-medium text-gray-500 border-b border-gray-100 pb-2">
                      <span>
                        بر اساس بازه‌ی {m.citationJalali} و {m.citationCategory} — {m.view ? `مرجع: ${m.view}` : 'مرجع داده‌ای: —'}
                      </span>
                      <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-100 text-amber-800 border border-amber-300 shrink-0">
                        داده نمایشی
                      </span>
                    </div>

                    {/* Answer: assistant's main text, shown above facts */}
                    {m.answer && (
                      <div className="text-sm text-gray-800 leading-relaxed whitespace-pre-wrap">
                        {m.answer}
                      </div>
                    )}

                    {/* Facts: bulleted «داده‌ها:» block */}
                    {m.facts && m.facts.length > 0 && (
                      <div className="mt-3 bg-gray-50 rounded-lg p-3.5 border border-gray-200">
                        <div className="text-xs font-bold text-gray-700 mb-1.5">داده‌ها:</div>
                        <ul className="list-disc list-inside space-y-1 text-xs text-gray-800">
                          {m.facts.map((fact, idx) => (
                            <li key={idx} className="leading-relaxed">
                              {fact}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {/* Interpretation: visually separated block titled «برداشت (نه داده):» */}
                    {m.interpretation && m.interpretation.trim().length > 0 && (
                      <div className="mt-3 bg-blue-50/70 rounded-lg p-3.5 border border-blue-200">
                        <div className="text-xs font-bold text-blue-900 mb-1">برداشت (نه داده):</div>
                        <div className="text-xs text-blue-950 whitespace-pre-wrap leading-relaxed">
                          {m.interpretation}
                        </div>
                      </div>
                    )}

                    {/* Period + filters echo */}
                    {m.periodEcho && (
                      <div className="text-xs text-gray-500 mt-3 pt-2.5 border-t border-gray-100 flex flex-wrap items-center gap-1.5">
                        <span>بازه تحلیل: {m.periodEcho.jalaliLabel}</span>
                        {m.periodEcho.prevJalaliLabel && (
                          <span className="text-gray-400">
                            (دوره مقایسه: {m.periodEcho.prevJalaliLabel})
                          </span>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              );
            })}

            {loading && (
              <div className="flex justify-start">
                <div className="bg-white border border-gray-200 rounded-2xl rounded-tr-xs p-4 max-w-md shadow-xs text-xs flex items-center gap-2.5 text-gray-600">
                  <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-solid border-blue-600 border-r-transparent" />
                  <span>در حال دریافت پاسخ...</span>
                </div>
              </div>
            )}
            <div ref={transcriptEndRef} />
          </div>
        )}
      </div>

      {/* Suggestion Chips */}
      <div className="space-y-2 pt-2 border-t border-gray-100">
        <div className="text-xs font-semibold text-gray-500">پرسش‌های پیشنهادی:</div>
        <div className="flex flex-wrap items-center gap-2">
          {SUGGESTIONS.map((suggestion) => (
            <button
              key={suggestion}
              type="button"
              onClick={() => handleSuggestionClick(suggestion)}
              className="px-3 py-1.5 rounded-lg text-xs font-medium bg-gray-100 hover:bg-blue-50 hover:text-blue-700 text-gray-700 transition cursor-pointer border border-transparent hover:border-blue-200"
            >
              {suggestion}
            </button>
          ))}
        </div>
      </div>

      {/* Input Form */}
      <form onSubmit={handleSubmit} className="space-y-2">
        <div>
          <textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            maxLength={2000}
            rows={3}
            placeholder="مثلاً: میزان فروش این ماه چقدر بوده؟"
            className="w-full p-3 text-xs md:text-sm bg-gray-50 border border-gray-300 rounded-xl focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 focus:bg-white transition resize-none text-gray-900 placeholder-gray-400"
            aria-label="متن پرسش"
          />
        </div>
        <div className="flex flex-col-reverse sm:flex-row sm:items-center sm:justify-between gap-2">
          <div className="text-[11px] text-gray-400">
            <span>{toPersianDigits(input.length)} / {toPersianDigits(2000)} کاراکتر</span>
            <span className="mr-2 text-gray-400">(حداکثر ۲۰۰۰ کاراکتر)</span>
          </div>
          <button
            type="submit"
            disabled={loading || input.trim().length === 0 || input.trim().length > 2000}
            className="inline-flex items-center justify-center px-5 py-2 rounded-lg text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition shadow-xs cursor-pointer min-w-[130px]"
          >
            {loading ? (
              <>
                <span className="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-solid border-white border-r-transparent ml-2" />
                <span>در حال دریافت پاسخ...</span>
              </>
            ) : (
              'ارسال'
            )}
          </button>
        </div>
      </form>
    </section>
  );
}
