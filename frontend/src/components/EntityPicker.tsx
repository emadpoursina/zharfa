import { useEffect, useId, useState } from 'react';
import { getPurchasing, type LookupResponse, type ReportUnavailable } from '../services/api';
import { UnavailableNote } from './UnavailableNote';

export type LookupKind = 'vendors' | 'products' | 'categories';

export interface EntityChoice {
  /** Source key, or «~unknown» for the unknown bucket. */
  key: string;
  name: string;
}

interface EntityPickerProps {
  kind: LookupKind;
  label: string;
  onSelect: (choice: EntityChoice) => void;
  /** Offer the unknown bucket («نامشخص») as a choice. */
  includeUnknown?: boolean;
  disabled?: boolean;
  placeholder?: string;
}

const UNKNOWN_KEY = '~unknown';
const UNKNOWN_NAME = 'نامشخص';
const DEBOUNCE_MS = 250;

const inputClass =
  'w-64 max-w-full px-3 py-1.5 text-sm text-gray-800 bg-white border border-gray-300 rounded-lg shadow-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-60';

/**
 * Combobox for a vendor, product or category (spec 004 T037): a search box backed by `lookup/*`, server-side search
 * through the Persian-tolerant search key. Shows «داده در دسترس نیست» when the lookup cannot be served.
 */
export function EntityPicker({ kind, label, onSelect, includeUnknown = false, disabled = false, placeholder }: EntityPickerProps) {
  const inputId = useId();
  const [text, setText] = useState('');
  const [items, setItems] = useState<Array<{ key: string | null; name: string }>>([]);
  const [unavailable, setUnavailable] = useState<ReportUnavailable[] | undefined>(undefined);
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let active = true;
    const timer = window.setTimeout(() => {
      setLoading(true);
      setFailed(false);
      getPurchasing<LookupResponse>(`lookup/${kind}`, { q: text.trim(), limit: 20 })
        .then((response) => {
          if (active) {
            setItems(response.items.map((item) => ({ key: item.key, name: item.name })));
            setUnavailable(response.unavailable);
            setLoading(false);
          }
        })
        .catch(() => {
          if (active) {
            setItems([]);
            setFailed(true);
            setLoading(false);
          }
        });
    }, DEBOUNCE_MS);
    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [kind, text]);

  const showUnknown = includeUnknown && (text.trim() === '' || UNKNOWN_NAME.includes(text.trim()));
  const unavailableNow = failed || (unavailable !== undefined && unavailable.length > 0);

  return (
    <div className="flex flex-col gap-1 text-sm text-gray-600">
      <label htmlFor={inputId}>{label}</label>
      <input
        id={inputId}
        type="search"
        className={inputClass}
        value={text}
        disabled={disabled}
        placeholder={placeholder ?? 'جستجو...'}
        autoComplete="off"
        onChange={(event) => setText(event.target.value)}
        aria-describedby={`${inputId}-results`}
      />
      <div id={`${inputId}-results`} className="max-h-56 overflow-auto rounded-lg border border-gray-200 bg-white">
        {unavailableNow ? (
          <div className="p-2">
            <UnavailableNote items={failed ? [{ figure: kind, reason: 'source-item-unverified' }] : unavailable} />
          </div>
        ) : loading ? (
          <p className="p-2 text-xs text-gray-500">در حال جستجو...</p>
        ) : (
          <ul className="divide-y divide-gray-100">
            {showUnknown && (
              <li>
                <button
                  type="button"
                  className="w-full px-3 py-1.5 text-start text-amber-800 italic hover:bg-amber-50 cursor-pointer focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
                  onClick={() => {
                    onSelect({ key: UNKNOWN_KEY, name: UNKNOWN_NAME });
                    setText('');
                  }}
                >
                  {UNKNOWN_NAME}
                </button>
              </li>
            )}
            {items.length === 0 && !showUnknown && <li className="p-2 text-xs text-gray-500">موردی یافت نشد.</li>}
            {items.map((item) => (
              <li key={`${item.key ?? 'none'}-${item.name}`}>
                <button
                  type="button"
                  disabled={item.key === null}
                  className="w-full px-3 py-1.5 text-start text-gray-800 hover:bg-blue-50 cursor-pointer disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
                  onClick={() => {
                    if (item.key !== null) {
                      onSelect({ key: item.key, name: item.name });
                      setText('');
                    }
                  }}
                >
                  {item.name}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
