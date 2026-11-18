import { useEffect, useState } from 'react';
import { getPurchasing, type ReportPrint, type VendorRow } from '../services/api';
import { defaultPeriod, useReportQuery, type PeriodValue } from '../hooks/useReportQuery';
import { comparisonColumns } from '../components/ComparisonCells';
import { FilterBar } from '../components/FilterBar';
import { Pager } from '../components/Pager';
import { PrintReport } from '../components/PrintReport';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from '../components/ReportTable';
import { UnavailableNote } from '../components/UnavailableNote';
import { faRialFromDecimal } from '../utils/format';
import { VendorProfile } from './VendorProfile';

/** A vendor profile requested from elsewhere (the Overview's top vendors), for the month shown there. */
export interface VendorTarget {
  vendor: { key: string; name: string };
  /** «YYYY-MM» Latin Jalali month. */
  month: string;
}

interface VendorsProps {
  serverToday: string;
  target?: VendorTarget | null;
}

const SORT_KEYS = ['net', 'name', 'code', 'lastPurchase'] as const;

/**
 * The vendor directory (spec 004 T045, US1): the period, the Persian-tolerant search by name or code, sort and paging,
 * with the previous-period columns when compare is on. A row opens the vendor's profile in place with the current period.
 */
export function Vendors({ serverToday, target = null }: VendorsProps) {
  const query = useReportQuery<VendorRow>({
    path: 'vendors',
    declared: ['compare'],
    searchable: true,
    sortKeys: SORT_KEYS,
    initialSort: 'net',
    initialDir: 'desc',
    serverToday,
  });
  const { filters, data, error, loading } = query;
  const [profile, setProfile] = useState<{ vendor: { key: string; name: string }; period: PeriodValue } | null>(null);
  const [print, setPrint] = useState<ReportPrint<VendorRow> | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);

  // A profile requested from the Overview opens directly, for that month.
  useEffect(() => {
    if (target) {
      setProfile({ vendor: target.vendor, period: { ...defaultPeriod(serverToday), kind: 'month', month: target.month } });
    }
  }, [target, serverToday]);

  if (profile) {
    return (
      <VendorProfile
        vendor={profile.vendor}
        serverToday={serverToday}
        initialPeriod={profile.period}
        onBack={() => setProfile(null)}
      />
    );
  }

  const codeUnavailable = data?.unavailable.some((item) => item.figure === 'vendorCode') ?? false;

  const columns: ReportColumn<VendorRow>[] = [
    { id: 'name', label: 'نام تأمین‌کننده', sortKey: 'name', render: (row) => row.name },
    {
      id: 'code',
      label: 'کد',
      sortKey: 'code',
      render: (row) => row.code ?? (codeUnavailable ? 'داده در دسترس نیست' : '—'),
    },
    {
      id: 'net',
      label: 'خالص خرید',
      sortKey: 'net',
      numeric: true,
      render: (row) => faRialFromDecimal(row.net),
      footer: () => faRialFromDecimal(data?.totals.net),
    },
    {
      id: 'last',
      label: 'آخرین خرید (فاکتور)',
      sortKey: 'lastPurchase',
      render: (row) => row.lastPurchaseDateJalali ?? '—',
    },
    ...(data?.previousPeriod
      ? comparisonColumns<VendorRow>(
          'cmp',
          { previous: 'خالص دوره قبل', delta: 'تفاوت', percent: 'درصد تغییر' },
          (row) => ({ previous: row.previousNet, delta: row.delta, percent: row.percent }),
        )
      : []),
  ];

  const printNow = async () => {
    setPrintError(null);
    try {
      const response = await getPurchasing<ReportPrint<VendorRow>>('vendors/print', query.printParams());
      setPrint(response);
      window.setTimeout(() => window.print(), 50);
    } catch (caught: unknown) {
      setPrintError(caught instanceof Error ? caught.message : 'چاپ انجام نشد.');
    }
  };

  return (
    <div className="space-y-5">
      <FilterBar
        filters={filters}
        update={query.update}
        setSearch={query.setSearch}
        declared={['compare']}
        searchable
        searchLabel="جستجوی نام یا کد تأمین‌کننده"
        serverToday={serverToday}
        activeLabels={data?.filters}
      />

      {data && <ReportStatusLine envelope={data} />}
      {error && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </p>
      )}
      {data && codeUnavailable && <UnavailableNote items={data.unavailable} figure="vendorCode" />}

      <div className="flex justify-end">
        <button
          type="button"
          onClick={printNow}
          disabled={loading || !data}
          className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-50 cursor-pointer"
        >
          چاپ فهرست
        </button>
      </div>
      {printError && <p role="alert" className="text-sm text-red-700">{printError}</p>}

      {data && (
        <>
          <Table
            caption="فهرست تأمین‌کنندگان"
            columns={columns}
            rows={data.rows}
            rowKey={(row) => row.key}
            sort={filters.sort}
            dir={filters.dir}
            onSort={query.toggleSort}
            rowActionLabel=""
            rowAction={(row) => (
              <button
                type="button"
                onClick={() => setProfile({ vendor: { key: row.key, name: row.name }, period: filters.period })}
                className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                aria-label={`پروفایل ${row.name}`}
              >
                پروفایل
              </button>
            )}
            emptyText="تأمین‌کننده‌ای با این جستجو یافت نشد."
          />
          <Pager
            page={data.page}
            pageSize={filters.pageSize}
            totalRows={data.totalRows}
            onPage={(page) => query.update({ page })}
            onPageSize={(pageSize) => query.update({ pageSize })}
            disabled={loading}
          />
        </>
      )}

      <PrintReport
        title="فهرست تأمین‌کنندگان"
        print={print}
        columns={columns}
        rowKey={(row) => row.key}
      />
    </div>
  );
}
