import type { ReportColumn } from './ReportTable';
import { faPercent, faRialFromDecimal } from '../utils/format';

/** The comparison figures of one row (spec 004 T066). Null = not available for this row. */
export interface ComparisonValues {
  previous: string | null | undefined;
  delta: string | null | undefined;
  percent: string | null | undefined;
}

const UNAVAILABLE_TEXT = 'داده در دسترس نیست';

function signedPercent(percent: string): string {
  const text = faPercent(percent);
  return percent.trim().startsWith('-') ? text : `+${text}`;
}

/**
 * Three columns for a row's previous period: previous net, the absolute difference and the percentage change. The
 * difference carries a sign and the percentage is «—» on a zero base, so colour is never the only signal (WCAG 1.4.1).
 * `previous` absent (`undefined`) means the comparison is not requested and no column should be rendered.
 */
export function comparisonColumns<TRow>(
  idPrefix: string,
  labels: { previous: string; delta: string; percent: string },
  pick: (row: TRow) => ComparisonValues,
): ReportColumn<TRow>[] {
  return [
    {
      id: `${idPrefix}-previous`,
      label: labels.previous,
      numeric: true,
      render: (row) => {
        const { previous } = pick(row);
        return previous === null || previous === undefined ? UNAVAILABLE_TEXT : faRialFromDecimal(previous);
      },
    },
    {
      id: `${idPrefix}-delta`,
      label: labels.delta,
      numeric: true,
      render: (row) => {
        const { delta } = pick(row);
        return delta === null || delta === undefined ? '—' : faRialFromDecimal(delta, { signed: true });
      },
    },
    {
      id: `${idPrefix}-percent`,
      label: labels.percent,
      numeric: true,
      render: (row) => {
        const { percent } = pick(row);
        return percent === null || percent === undefined ? '—' : signedPercent(percent);
      },
    },
  ];
}
