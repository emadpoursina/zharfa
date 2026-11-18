import { useEffect, useState } from 'react';
import { getPurchasing, type DocumentLines, type PurchasingQueryValue } from '../services/api';
import { faQty, faRialFromDecimal } from '../utils/format';

interface DocumentLinesPanelProps {
  documentId: number;
  /** product, category and match of the surrounding report (the lines shown are the in-scope ones). */
  params?: Record<string, PurchasingQueryValue>;
  /** Show every line of the document (`all=true`). */
  all?: boolean;
}

/**
 * The lines of one document (spec 004 T046, T058): fetched when shown, in-scope lines only unless `all` is set. A line
 * outside the report's product or category filter is not listed, so the panel's totals equal the row that opened it.
 */
export function DocumentLinesPanel({ documentId, params = {}, all = false }: DocumentLinesPanelProps) {
  const [data, setData] = useState<DocumentLines | null>(null);
  const [error, setError] = useState<string | null>(null);
  const paramKey = JSON.stringify({ ...params, all });

  useEffect(() => {
    let active = true;
    setData(null);
    setError(null);
    const query: Record<string, PurchasingQueryValue> = { ...(JSON.parse(paramKey) as Record<string, PurchasingQueryValue>) };
    if (!query.all) {
      delete query.all;
    }
    getPurchasing<DocumentLines>(`documents/${documentId}/lines`, query)
      .then((response) => {
        if (active) setData(response);
      })
      .catch((caught: unknown) => {
        if (active) setError(caught instanceof Error ? caught.message : 'خطایی رخ داد.');
      });
    return () => {
      active = false;
    };
  }, [documentId, paramKey]);

  if (error) {
    return <p role="alert" className="text-sm text-red-700">{error}</p>;
  }
  if (!data) {
    return <p className="text-sm text-gray-600">در حال بارگذاری ردیف‌ها...</p>;
  }
  if (data.lines.length === 0) {
    return <p className="text-sm text-gray-600">ردیفی از این سند در فیلترهای فعال نیست.</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <caption className="sr-only">ردیف‌های سند {data.document.number}</caption>
        <thead>
          <tr className="text-gray-700">
            <th scope="col" className="px-2 py-1 text-right">کالا</th>
            <th scope="col" className="px-2 py-1 text-right">گروه دارویی</th>
            <th scope="col" className="px-2 py-1 text-left">تعداد</th>
            <th scope="col" className="px-2 py-1 text-left">پیش از تخفیف</th>
            <th scope="col" className="px-2 py-1 text-left">تخفیف</th>
            <th scope="col" className="px-2 py-1 text-left">مبلغ ردیف</th>
            <th scope="col" className="px-2 py-1 text-left">علامت‌دار</th>
          </tr>
        </thead>
        <tbody>
          {data.lines.map((line) => (
            <tr key={line.lineId} className={line.unknownProduct ? 'italic text-amber-900' : undefined}>
              <td className="px-2 py-1 text-right">{line.productName}</td>
              <td className="px-2 py-1 text-right">{line.categoryName ?? '—'}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faQty(line.quantity)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.preDiscount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.discount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.lineAmount)}</td>
              <td className="px-2 py-1 text-left tabular-nums">{faRialFromDecimal(line.signedAmount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-2 text-xs text-gray-600">
        جمع ردیف‌های نمایش‌داده‌شده: {faRialFromDecimal(data.totals.signedAmount)} (علامت‌دار)
      </p>
    </div>
  );
}
