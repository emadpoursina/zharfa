interface SortHeaderProps {
  label: string;
  /** The sort key this header controls (from the report's allowlist). */
  sortKey: string;
  activeSort: string;
  dir: 'asc' | 'desc';
  onSort: (key: string) => void;
  align?: 'start' | 'end';
}

/**
 * A sortable column header (spec 004 T039): a button with `aria-sort` on the cell. The arrow is text, so the direction
 * is not conveyed by colour alone.
 */
export function SortHeader({ label, sortKey, activeSort, dir, onSort, align = 'end' }: SortHeaderProps) {
  const active = activeSort === sortKey;
  const ariaSort = active ? (dir === 'asc' ? 'ascending' : 'descending') : 'none';
  return (
    <th
      scope="col"
      aria-sort={ariaSort}
      className={`px-3 py-2 font-semibold text-gray-700 whitespace-nowrap text-right ${align === 'end' ? 'tabular-nums' : ''}`}
    >
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className="inline-flex items-center gap-1 rounded-sm px-1 py-0.5 hover:text-blue-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
      >
        <span>{label}</span>
        <span aria-hidden="true" className="text-xs text-gray-500">
          {active ? (dir === 'asc' ? '▲' : '▼') : '↕'}
        </span>
      </button>
    </th>
  );
}
