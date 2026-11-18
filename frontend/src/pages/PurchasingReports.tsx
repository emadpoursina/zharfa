import { useState, type ReactNode } from 'react';
import {
  getPurchasing,
  type DocumentRow,
  type GroupedRow,
  type ReportDrill,
  type ReportPrint,
  type ReportTotals,
  type ReturnsRow,
  type TrendRow,
  type PurchasingQueryValue,
} from '../services/api';
import { periodParams, useReportQuery, type ReportFilterName, type ReportFilters } from '../hooks/useReportQuery';
import { comparisonColumns } from '../components/ComparisonCells';
import { DocumentLinesPanel } from '../components/DocumentLinesPanel';
import { DrillDownDialog } from '../components/DrillDownDialog';
import { FilterBar } from '../components/FilterBar';
import { Pager } from '../components/Pager';
import { PrintReport } from '../components/PrintReport';
import { ReportStatusLine, ReportTable as Table, type ReportColumn } from '../components/ReportTable';
import { UnavailableNote } from '../components/UnavailableNote';
import { faNum, faQty, faRialFromDecimal } from '../utils/format';
import { daysInJalaliMonth, toLatinDigits } from '../utils/jalali';

interface ReportScreenProps<TRow> {
  title: string;
  description: string;
  /** Path below /api/purchasing, for example `reports/by-vendor`. */
  path: string;
  declared: ReportFilterName[];
  searchLabel: string;
  sortKeys: string[];
  initialSort: string;
  initialDir: 'asc' | 'desc';
  serverToday: string;
  /** Columns of the report; the totals of the response (or the print twin) feed the footer cells. */
  buildColumns: (source: { totals: ReportTotals } | null, compare: boolean) => ReportColumn<TRow>[];
  rowKey: (row: TRow) => string;
  isUnknown?: (row: TRow) => boolean;
  /** The drill-down filters of a row; null = the row has no drill-down. */
  drillOf?: (row: TRow) => ReportDrill | null;
  drillTitle?: (row: TRow) => string;
  /** Expandable rows (transactions): the lines of the row's document. */
  expandable?: (row: TRow) => ReactNode;
  landscape?: boolean;
  /** Totals line for the quantity note (quantityMissingLines). */
  quantityNote?: (totals: ReportTotals | undefined) => string | null;
}

interface DrillState {
  title: string;
  drill: ReportDrill;
  base: Record<string, PurchasingQueryValue>;
}

/**
 * The filter parameters a drill-down keeps from the screen: the period and the vendor, product and category filters the
 * report declares. A trend row's month is drilled within a custom range of that month when the screen is month-based,
 * so the month filter is never read as the period (contracts/api.md §5).
 */
function drillBase(filters: ReportFilters, declared: ReportFilterName[], drill: ReportDrill): Record<string, PurchasingQueryValue> {
  const base: Record<string, PurchasingQueryValue> = {};
  if (drill.month && filters.period.kind === 'month') {
    const [yearText, monthText] = toLatinDigits(drill.month).split('-');
    const days = daysInJalaliMonth(parseInt(yearText, 10), parseInt(monthText, 10));
    Object.assign(base, {
      period: 'custom',
      from: `${yearText}-${monthText}-01`,
      to: `${yearText}-${monthText}-${String(days).padStart(2, '0')}`,
    });
  } else {
    Object.assign(base, periodParams(filters.period));
  }
  if (declared.includes('vendor') && filters.vendor) base.vendor = filters.vendor;
  if (declared.includes('product') && filters.product) base.product = filters.product;
  if (declared.includes('category') && filters.category) base.category = filters.category;
  return base;
}

/**
 * One purchasing report (spec 004 T054, T058, T070): the report's declared filters only, search, sort, paging, the
 * comparison when the report offers it, drill-down on every grouped row, expandable rows where the report asks for
 * them, the statement of period and filters, and the print twin.
 */
function ReportScreen<TRow>(props: ReportScreenProps<TRow>) {
  const { title, description, path, declared, searchLabel, sortKeys, initialSort, initialDir, serverToday, rowKey, isUnknown, drillOf, drillTitle, expandable, landscape = false, quantityNote } = props;
  const query = useReportQuery<TRow>({
    path,
    declared,
    searchable: true,
    sortKeys,
    initialSort,
    initialDir,
    serverToday,
  });
  const { filters, data, error, loading } = query;
  const [expandedKey, setExpandedKey] = useState<string | null>(null);
  const [drill, setDrill] = useState<DrillState | null>(null);
  const [print, setPrint] = useState<ReportPrint<TRow> | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);

  const compare = Boolean(data?.previousPeriod);
  const columns = props.buildColumns(data, compare);
  const note = quantityNote?.(data?.totals) ?? null;

  const printNow = async () => {
    setPrintError(null);
    try {
      const response = await getPurchasing<ReportPrint<TRow>>(`${path}/print`, query.printParams());
      setPrint(response);
      window.setTimeout(() => window.print(), 50);
    } catch (caught: unknown) {
      setPrintError(caught instanceof Error ? caught.message : 'چاپ انجام نشد.');
    }
  };

  return (
    <section aria-labelledby={`${path}-title`} className="space-y-5">
      <header className="space-y-1">
        <h2 id={`${path}-title`} className="text-lg font-bold text-gray-900">
          {title}
        </h2>
        <p className="text-sm text-gray-600">{description}</p>
      </header>

      <FilterBar
        filters={filters}
        update={query.update}
        setSearch={query.setSearch}
        declared={declared}
        searchable
        searchLabel={searchLabel}
        serverToday={serverToday}
        activeLabels={data?.filters}
      />

      {data && <ReportStatusLine envelope={data} />}
      {error && (
        <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </p>
      )}
      {note && data && data.totals.quantityMissingLines !== undefined && data.totals.quantityMissingLines > 0 && (
        <p className="text-xs text-amber-900">{note}</p>
      )}
      {data && data.unavailable.some((item) => item.figure === 'quantity') && (
        <UnavailableNote items={data.unavailable} figure="quantity" />
      )}

      <div className="flex justify-end">
        <button
          type="button"
          onClick={printNow}
          disabled={loading || !data}
          className="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 hover:bg-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 disabled:opacity-50 cursor-pointer"
        >
          چاپ گزارش
        </button>
      </div>
      {printError && <p role="alert" className="text-sm text-red-700">{printError}</p>}

      {data && (
        <>
          <Table
            caption={title}
            columns={columns}
            rows={data.rows}
            rowKey={rowKey}
            sort={filters.sort}
            dir={filters.dir}
            onSort={query.toggleSort}
            isUnknown={isUnknown}
            rowActionLabel={drillOf || expandable ? 'جزئیات' : ''}
            rowAction={
              drillOf || expandable
                ? (row) => {
                    const drillFilters = drillOf?.(row) ?? null;
                    return (
                      <div className="flex flex-wrap justify-end gap-2">
                        {drillFilters && (
                          <button
                            type="button"
                            onClick={() =>
                              setDrill({
                                title: drillTitle?.(row) ?? title,
                                drill: drillFilters,
                                base: drillBase(filters, declared, drillFilters),
                              })
                            }
                            className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                          >
                            اسناد
                          </button>
                        )}
                        {expandable && (
                          <button
                            type="button"
                            aria-expanded={expandedKey === rowKey(row)}
                            onClick={() => setExpandedKey(expandedKey === rowKey(row) ? null : rowKey(row))}
                            className="rounded-md border border-gray-300 px-2 py-0.5 text-xs font-medium text-blue-800 hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 cursor-pointer"
                          >
                            ردیف‌ها
                          </button>
                        )}
                      </div>
                    );
                  }
                : undefined
            }
            expandedKey={expandable ? expandedKey : null}
            renderExpanded={expandable ? (row) => expandable(row) : undefined}
            emptyText="برای این گزارش و فیلترها ردیفی یافت نشد."
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

      {drill && (
        <DrillDownDialog
          open
          title={drill.title}
          baseParams={drill.base}
          drill={drill.drill}
          onClose={() => setDrill(null)}
        />
      )}

      <PrintReport
        title={title}
        print={print}
        columns={props.buildColumns(print, Boolean(print?.previousPeriod))}
        rowKey={rowKey}
        isUnknown={isUnknown}
        landscape={landscape}
      />
    </section>
  );
}

// ----- measures shared by the grouped reports ----------------------------------------------------------------

const COMPARE_LABELS = { previous: 'دوره قبل', delta: 'تفاوت', percent: 'درصد تغییر' };

function groupedComparison(compare: boolean): ReportColumn<GroupedRow>[] {
  return compare
    ? comparisonColumns<GroupedRow>('cmp', COMPARE_LABELS, (row) => ({
        previous: row.previousNet,
        delta: row.delta,
        percent: row.percent,
      }))
    : [];
}

function groupedMeasures(source: { totals: ReportTotals } | null, withDocuments: boolean): ReportColumn<GroupedRow>[] {
  const totals = source?.totals;
  const documents: ReportColumn<GroupedRow>[] = withDocuments
    ? [
        {
          id: 'documents',
          label: 'تعداد اسناد',
          sortKey: 'documents',
          numeric: true,
          render: (row) => faNum(row.documentCount),
          footer: () => faNum(totals?.documentCount),
        },
      ]
    : [];
  return [
    {
      id: 'net',
      label: 'خالص',
      sortKey: 'net',
      numeric: true,
      render: (row) => faRialFromDecimal(row.net),
      footer: () => faRialFromDecimal(totals?.net),
    },
    {
      id: 'gross',
      label: 'خرید ناخالص',
      sortKey: 'gross',
      numeric: true,
      render: (row) => faRialFromDecimal(row.gross),
      footer: () => faRialFromDecimal(totals?.gross),
    },
    {
      id: 'returns',
      label: 'برگشت از خرید',
      sortKey: 'returns',
      numeric: true,
      render: (row) => faRialFromDecimal(row.returns),
      footer: () => faRialFromDecimal(totals?.returns),
    },
    {
      id: 'vat',
      label: 'ارزش افزوده',
      sortKey: 'vat',
      numeric: true,
      render: (row) => faRialFromDecimal(row.vat),
      footer: () => faRialFromDecimal(totals?.vat),
    },
    {
      id: 'qty',
      label: 'تعداد (واحد ثبت‌شده)',
      sortKey: 'quantity',
      numeric: true,
      render: (row) => (row.netQuantity === null ? 'داده در دسترس نیست' : faQty(row.netQuantity)),
      footer: () => faQty(totals?.netQuantity),
    },
    ...documents,
  ];
}

const GROUPED_NOTE = (totals: ReportTotals | undefined): string | null =>
  totals?.quantityMissingLines && totals.quantityMissingLines > 0
    ? `${faNum(totals.quantityMissingLines)} ردیف بدون تعداد ثبت‌شده است؛ تعداد خالص فقط ردیف‌های دارای تعداد را جمع می‌زند.`
    : null;

const rowKeyGrouped = (row: GroupedRow) => `${row.key ?? '~unknown'}`;

// ----- the seven reports ---------------------------------------------------------------------------------------

function ByVendorReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<GroupedRow>
      title="خرید به تفکیک تأمین‌کننده"
      description="خرید خالص، ناخالص، برگشت و ارزش افزوده هر تأمین‌کننده در دوره انتخابی."
      path="reports/by-vendor"
      declared={['product', 'category', 'compare']}
      searchLabel="جستجوی تأمین‌کننده"
      sortKeys={['net', 'gross', 'returns', 'vat', 'quantity', 'documents', 'name']}
      initialSort="net"
      initialDir="desc"
      serverToday={serverToday}
      rowKey={rowKeyGrouped}
      isUnknown={(row) => row.unknown}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `اسناد ${row.name}`}
      quantityNote={GROUPED_NOTE}
      buildColumns={(data, compare) => [
        { id: 'name', label: 'تأمین‌کننده', sortKey: 'name', render: (row) => (row.code ? `${row.name} (${row.code})` : row.name) },
        ...groupedMeasures(data, true),
        ...groupedComparison(compare),
      ]}
    />
  );
}

function ByProductReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<GroupedRow>
      title="خرید به تفکیک کالا"
      description="خرید خالص و اقلام مالی هر کالا در دوره انتخابی."
      path="reports/by-product"
      declared={['vendor', 'category', 'compare']}
      searchLabel="جستجوی کالا"
      sortKeys={['net', 'gross', 'returns', 'vat', 'quantity', 'name']}
      initialSort="net"
      initialDir="desc"
      serverToday={serverToday}
      rowKey={rowKeyGrouped}
      isUnknown={(row) => row.unknown}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `اسناد ${row.name}`}
      quantityNote={GROUPED_NOTE}
      buildColumns={(data, compare) => [
        { id: 'name', label: 'کالا', sortKey: 'name', render: (row) => row.name },
        ...groupedMeasures(data, false),
        ...groupedComparison(compare),
      ]}
    />
  );
}

function ByCategoryReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<GroupedRow>
      title="خرید به تفکیک گروه دارویی"
      description="خرید خالص هر گروه دارویی؛ کالاهای بدون گروه یا با گروه ناشناخته در «نامشخص» جمع می‌شوند."
      path="reports/by-category"
      declared={['vendor', 'compare']}
      searchLabel="جستجوی گروه دارویی"
      sortKeys={['net', 'gross', 'returns', 'vat', 'quantity', 'name']}
      initialSort="net"
      initialDir="desc"
      serverToday={serverToday}
      rowKey={rowKeyGrouped}
      isUnknown={(row) => row.unknown}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `اسناد ${row.name}`}
      quantityNote={GROUPED_NOTE}
      buildColumns={(data, compare) => [
        { id: 'name', label: 'گروه دارویی', sortKey: 'name', render: (row) => row.name },
        ...groupedMeasures(data, false),
        ...groupedComparison(compare),
      ]}
    />
  );
}

function TopProductsReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<GroupedRow>
      title="پرخریدترین کالاها"
      description="رتبه بر اساس خالص؛ با مرتب‌سازی بر اساس تعداد، رتبه‌ها بر اساس تعداد دوباره محاسبه می‌شود."
      path="reports/top-products"
      declared={['vendor', 'category', 'compare']}
      searchLabel="جستجوی کالا"
      sortKeys={['net', 'quantity']}
      initialSort="net"
      initialDir="desc"
      serverToday={serverToday}
      rowKey={rowKeyGrouped}
      isUnknown={(row) => row.unknown}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `اسناد ${row.name}`}
      quantityNote={GROUPED_NOTE}
      buildColumns={(data, compare) => [
        { id: 'rank', label: 'رتبه', render: (row) => (row.rank ? faNum(row.rank) : '—') },
        { id: 'name', label: 'کالا', render: (row) => row.name },
        ...groupedMeasures(data, true),
        ...groupedComparison(compare),
      ]}
    />
  );
}

function TrendReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<TrendRow>
      title="روند ماهانه خرید"
      description="خرید خالص هر ماه؛ ماه‌های بدون داده همگام‌سازی‌شده خالی هستند، صفر نیستند. جستجو فقط ردیف‌های تأمین‌کننده یا کالای منطبق را جمع می‌زند."
      path="reports/trend"
      declared={['vendor', 'product', 'category', 'compare']}
      searchLabel="جستجوی تأمین‌کننده یا کالا"
      sortKeys={['month', 'net']}
      initialSort="month"
      initialDir="asc"
      serverToday={serverToday}
      rowKey={(row) => row.month}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `اسناد ${row.label}`}
      buildColumns={(data, compare) => [
        { id: 'month', label: 'ماه', sortKey: 'month', render: (row) => row.label },
        {
          id: 'net',
          label: 'خالص',
          sortKey: 'net',
          numeric: true,
          render: (row) => (row.available ? faRialFromDecimal(row.net) : 'داده در دسترس نیست'),
          footer: () => faRialFromDecimal(data?.totals.net),
        },
        {
          id: 'qty',
          label: 'تعداد (واحد ثبت‌شده)',
          numeric: true,
          render: (row) => (row.available && row.netQuantity !== null ? faQty(row.netQuantity) : '—'),
          footer: () => faQty(data?.totals.netQuantity),
        },
        {
          id: 'status',
          label: 'وضعیت ماه',
          render: (row) => (!row.available ? 'بدون داده' : row.partial ? 'ماه ناقص بازه' : 'کامل'),
        },
        ...(compare
          ? comparisonColumns<TrendRow>('cmp', COMPARE_LABELS, (row) => ({
              previous: row.previousNet,
              delta: row.delta,
              percent: row.percent,
            }))
          : []),
      ]}
    />
  );
}

function ReturnsReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<ReturnsRow>
      title="برگشت از خرید"
      description="اسناد برگشت از خرید به تفکیک تأمین‌کننده و کالا، بر اساس تاریخ سند برگشت."
      path="reports/returns"
      declared={['vendor', 'product', 'category', 'compare']}
      searchLabel="جستجوی تأمین‌کننده یا کالا"
      sortKeys={['returns', 'quantity', 'vendor', 'product']}
      initialSort="returns"
      initialDir="desc"
      serverToday={serverToday}
      rowKey={(row) => `${row.vendorKey ?? '~unknown'}|${row.productKey ?? '~unknown'}`}
      isUnknown={(row) => row.unknownVendor || row.unknownProduct}
      drillOf={(row) => row.drill}
      drillTitle={(row) => `برگشت‌های ${row.vendorName} — ${row.productName}`}
      buildColumns={(data, compare) => [
        { id: 'vendor', label: 'تأمین‌کننده', sortKey: 'vendor', render: (row) => row.vendorName },
        { id: 'product', label: 'کالا', sortKey: 'product', render: (row) => row.productName },
        {
          id: 'returns',
          label: 'برگشت از خرید',
          sortKey: 'returns',
          numeric: true,
          render: (row) => faRialFromDecimal(row.returns),
          footer: () => faRialFromDecimal(data?.totals.returns),
        },
        {
          id: 'qty',
          label: 'تعداد (واحد ثبت‌شده)',
          sortKey: 'quantity',
          numeric: true,
          render: (row) => faQty(row.quantity),
          footer: () => faQty(data?.totals.netQuantity),
        },
        { id: 'documents', label: 'تعداد اسناد', numeric: true, render: (row) => faNum(row.documentCount) },
        ...(compare
          ? comparisonColumns<ReturnsRow>('cmp', COMPARE_LABELS, (row) => ({
              previous: row.previousReturns,
              delta: row.delta,
              percent: row.percent,
            }))
          : []),
      ]}
    />
  );
}

function TransactionsReport({ serverToday }: { serverToday: string }) {
  return (
    <ReportScreen<DocumentRow>
      title="فهرست اسناد خرید"
      description="همه فاکتورها و برگشت‌های خرید دوره؛ مبلغ پیش از تخفیف، تخفیف، ارزش افزوده و خالص هر سند."
      path="reports/transactions"
      declared={['vendor', 'product', 'category', 'docType']}
      searchLabel="جستجوی شماره سند یا تأمین‌کننده"
      sortKeys={['date', 'number', 'vendor', 'net']}
      initialSort="date"
      initialDir="desc"
      serverToday={serverToday}
      landscape
      rowKey={(row) => String(row.documentId)}
      isUnknown={(row) => row.vendorKey === null}
      expandable={(row) => <DocumentLinesPanel documentId={row.documentId} />}
      buildColumns={(data) => [
        { id: 'number', label: 'شماره سند', sortKey: 'number', render: (row) => row.number },
        { id: 'date', label: 'تاریخ', sortKey: 'date', render: (row) => row.dateJalali },
        { id: 'type', label: 'نوع سند', render: (row) => row.docTypeLabel },
        { id: 'vendor', label: 'تأمین‌کننده', sortKey: 'vendor', render: (row) => row.vendorName },
        {
          id: 'pre',
          label: 'مبلغ پیش از تخفیف',
          numeric: true,
          render: (row) => faRialFromDecimal(row.preDiscount),
          footer: () => faRialFromDecimal(data?.totals.preDiscount),
        },
        {
          id: 'discount',
          label: 'تخفیف',
          numeric: true,
          render: (row) => faRialFromDecimal(row.discount),
          footer: () => faRialFromDecimal(data?.totals.discount),
        },
        {
          id: 'vat',
          label: 'ارزش افزوده',
          numeric: true,
          render: (row) => faRialFromDecimal(row.vat),
          footer: () => faRialFromDecimal(data?.totals.vat),
        },
        {
          id: 'net',
          label: 'خالص',
          sortKey: 'net',
          numeric: true,
          render: (row) => faRialFromDecimal(row.net),
          footer: () => faRialFromDecimal(data?.totals.net),
        },
        { id: 'qty', label: 'تعداد (واحد ثبت‌شده)', numeric: true, render: (row) => faQty(row.quantity) },
      ]}
    />
  );
}

// ----- the page: report picker and the selected report -------------------------------------------------------

const REPORT_TABS = [
  { id: 'by-vendor', label: 'تأمین‌کننده' },
  { id: 'by-product', label: 'کالا' },
  { id: 'by-category', label: 'گروه دارویی' },
  { id: 'trend', label: 'روند ماهانه' },
  { id: 'top-products', label: 'پرخریدترین کالاها' },
  { id: 'returns', label: 'برگشت از خرید' },
  { id: 'transactions', label: 'فهرست اسناد' },
] as const;

type ReportId = (typeof REPORT_TABS)[number]['id'];

interface PurchasingReportsProps {
  serverToday: string;
}

/**
 * The «گزارش‌های خرید» screen (spec 004 US2): the seven standard reports behind one picker. Each report keeps its own
 * filter state and resets when the picker changes.
 */
export function PurchasingReports({ serverToday }: PurchasingReportsProps) {
  const [report, setReport] = useState<ReportId>('by-vendor');

  const body = (() => {
    switch (report) {
      case 'by-vendor':
        return <ByVendorReport key="by-vendor" serverToday={serverToday} />;
      case 'by-product':
        return <ByProductReport key="by-product" serverToday={serverToday} />;
      case 'by-category':
        return <ByCategoryReport key="by-category" serverToday={serverToday} />;
      case 'trend':
        return <TrendReport key="trend" serverToday={serverToday} />;
      case 'top-products':
        return <TopProductsReport key="top-products" serverToday={serverToday} />;
      case 'returns':
        return <ReturnsReport key="returns" serverToday={serverToday} />;
      case 'transactions':
        return <TransactionsReport key="transactions" serverToday={serverToday} />;
    }
  })();

  return (
    <div className="space-y-6">
      <nav aria-label="گزارش‌های خرید" className="flex flex-wrap gap-2">
        {REPORT_TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            aria-pressed={report === tab.id}
            onClick={() => setReport(tab.id)}
            className={`px-3 py-1.5 text-sm font-medium rounded-lg border transition cursor-pointer focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600 ${
              report === tab.id ? 'bg-blue-700 text-white border-blue-700' : 'bg-white text-gray-800 border-gray-300 hover:bg-gray-100'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </nav>
      {body}
    </div>
  );
}
