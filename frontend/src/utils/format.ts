import { toPersianDigits } from './jalali';

/**
 * Persian number and currency formatting utilities using Intl.NumberFormat('fa-IR').
 * In-house implementation with zero external dependencies.
 */

const numberFormatter = new Intl.NumberFormat('fa-IR', {
  maximumFractionDigits: 2,
  useGrouping: true,
});

const qtyFormatter = new Intl.NumberFormat('fa-IR', {
  maximumFractionDigits: 3,
  minimumFractionDigits: 0,
  useGrouping: true,
});

const percentFormatter = new Intl.NumberFormat('fa-IR', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  useGrouping: true,
});

/**
 * Formats a monetary amount into Persian with digit grouping and «ریال» suffix.
 * Example: 123456 -> «۱۲۳٬۴۵۶ ریال»
 */
export function faAmount(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (isNaN(num)) {
    return '—';
  }
  return `${numberFormatter.format(num)} ریال`;
}

/**
 * Formats a number with Persian digits and grouping.
 */
export function faNum(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (isNaN(num)) {
    return '—';
  }
  return numberFormatter.format(num);
}

/**
 * Formats quantity with up to 3 decimal places and trimmed trailing zeros in Persian digits.
 * Example: 10 -> «۱۰», 1.5 -> «۱٫۵»
 */
export function faQty(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (isNaN(num)) {
    return '—';
  }
  return qtyFormatter.format(num);
}

/**
 * Formats a percentage value into Persian digits with «٪» suffix, or «—» if null.
 * Example: 23.46 -> «۲۳٫۴۶٪», null -> «—»
 */
export function faPercent(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (isNaN(num)) {
    return '—';
  }
  return `${percentFormatter.format(num)}٪`;
}

const bigIntFormatter = new Intl.NumberFormat('fa-IR', { useGrouping: true });

/**
 * Formats an exact decimal string from the API (for example "1250000000.0000") as Rial with Persian
 * digits and grouping, followed by «ریال». Works on the string, so no float rounding is involved.
 * Fractions are shown only when they are not zero. `signed` adds «+» to positive values (comparisons).
 * Example: "-50.0000" → «−۵۰ ریال», "1250000000.0000" → «۱٬۲۵۰٬۰۰۰٬۰۰۰ ریال»
 */
export function faRialFromDecimal(
  value: string | null | undefined,
  options: { signed?: boolean } = {},
): string {
  if (value === null || value === undefined) {
    return '—';
  }
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(value.trim());
  if (!match) {
    return '—';
  }
  const [, sign, integerPart, fractionRaw = ''] = match;
  const fraction = fractionRaw.replace(/0+$/, '');
  const isZero = /^0+$/.test(integerPart) && fraction.length === 0;
  const integerText = bigIntFormatter.format(BigInt(integerPart));
  const fractionText = fraction.length > 0 ? `٫${toPersianDigits(fraction)}` : '';

  let signText = '';
  if (sign === '-' && !isZero) {
    signText = '−';
  } else if (options.signed && !isZero) {
    signText = '+';
  }

  return `${signText}${integerText}${fractionText} ریال`;
}
