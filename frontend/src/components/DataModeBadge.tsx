import type { DataLabel } from '../services/api';

interface DataModeBadgeProps {
  /** `dataLabel` from GET /api/system/status (or any data-bearing payload). */
  dataLabel: DataLabel;
  className?: string;
}

/**
 * Which data this install shows, in every header and page (FR-022, FR-024): the company label
 * in production, the demonstration label in demo installs. Driven only by the server's label;
 * there is no client-side switch.
 */
export function DataModeBadge({ dataLabel, className = '' }: DataModeBadgeProps) {
  const isReal = dataLabel === 'real';

  return (
    <span
      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border whitespace-nowrap ${
        isReal
          ? 'bg-green-50 text-green-800 border-green-200'
          : 'bg-amber-100 text-amber-800 border-amber-300'
      } ${className}`.trim()}
    >
      {isReal ? 'داده واقعی شرکت' : 'داده نمایشی (دمو)'}
    </span>
  );
}
