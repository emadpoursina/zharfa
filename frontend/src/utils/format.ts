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
