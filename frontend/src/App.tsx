import { useEffect, useState } from 'react';
import { ChangePassword } from './pages/ChangePassword';
import { DataModeBadge } from './components/DataModeBadge';
import { DemoDataBanner } from './components/DemoDataBanner';
import { LastSyncIndicator } from './components/LastSyncIndicator';
import { NoAccess } from './components/NoAccess';
import { AuditLog } from './pages/AuditLog';
import { DataQuality } from './pages/DataQuality';
import { DataSync } from './pages/DataSync';
import { Dashboard } from './pages/Dashboard';
import { OverviewPurchasing } from './pages/OverviewPurchasing';
import { PurchasingReports } from './pages/PurchasingReports';
import { SavedReports } from './pages/SavedReports';
import { UsersRoles } from './pages/UsersRoles';
import { VendorComparison } from './pages/VendorComparison';
import { Vendors, type VendorTarget } from './pages/Vendors';
import { VendorFinancials } from './pages/VendorFinancials';
import { Assistant } from './pages/Assistant';
import { AiSettings } from './pages/AiSettings';
import { Login } from './pages/Login';
import {
  FORBIDDEN_EVENT,
  PASSWORD_CHANGE_REQUIRED_EVENT,
  getMe,
  getSystemStatus,
  isUnauthenticated,
  apiPost,
  type Me,
  type SystemModules,
  type SystemStatus,
} from './services/api';

type ViewId = 'overview' | 'vendors' | 'financials' | 'reports' | 'compare' | 'assistant' | 'saved' | 'dataSync' | 'users' | 'audit' | 'dataQuality' | 'aiSettings' | 'demo';

/** Hash route of each view (contracts/api.md §8). */
const VIEW_HASH: Record<ViewId, string> = {
  overview: '#/overview',
  vendors: '#/vendors',
  financials: '#/financials',
  reports: '#/reports',
  compare: '#/compare',
  assistant: '#/assistant',
  saved: '#/saved',
  dataSync: '#/settings/sync',
  users: '#/settings/users',
  audit: '#/settings/audit',
  dataQuality: '#/settings/data-quality',
  aiSettings: '#/settings/ai',
  demo: '#/demo',
};

/**
 * Views of the shell. Which views exist is decided by the server in GET /api/system/status (`modules`); the UI only
 * shows what the server allows, and the server enforces every endpoint.
 */
const VIEW_TABS: ReadonlyArray<{
  id: ViewId;
  label: string;
  isVisible: (modules: SystemModules) => boolean;
}> = [
  { id: 'overview', label: 'نمای کلی خرید', isVisible: (modules) => modules.overview },
  { id: 'vendors', label: 'تأمین‌کنندگان', isVisible: (modules) => modules.purchasingReports },
  { id: 'financials', label: 'اطلاعات مالی تأمین‌کنندگان', isVisible: (modules) => modules.vendorFinancials },
  { id: 'reports', label: 'گزارش‌های خرید', isVisible: (modules) => modules.purchasingReports },
  { id: 'compare', label: 'مقایسه تأمین‌کنندگان', isVisible: (modules) => modules.purchasingReports },
  { id: 'assistant', label: 'دستیار هوشمند خرید', isVisible: (modules) => modules.ai },
  { id: 'saved', label: 'گزارش‌های ذخیره‌شده', isVisible: (modules) => modules.savedReports },
  { id: 'dataSync', label: 'تنظیمات ← همگام‌سازی داده', isVisible: (modules) => modules.dataSync },
  { id: 'users', label: 'تنظیمات ← کاربران و نقش‌ها', isVisible: (modules) => modules.users },
  { id: 'audit', label: 'تنظیمات ← گزارش رویدادها', isVisible: (modules) => modules.auditLog },
  { id: 'dataQuality', label: 'تنظیمات ← کیفیت داده', isVisible: (modules) => modules.dataQuality },
  { id: 'aiSettings', label: 'تنظیمات ← هوش مصنوعی', isVisible: (modules) => modules.aiSettings },
  { id: 'demo', label: 'صفحات نمایشی', isVisible: (modules) => modules.demoPages },
];

type ViewTab = (typeof VIEW_TABS)[number];

interface ViewContext {
  vendorTarget: VendorTarget | null;
  openVendor: (vendor: { key: string; name: string }, month: string) => void;
  /** Issue type ids named by `#/settings/data-quality?types=a,b`. */
  dataQualityTypes: string[];
}

/** The view a hash names (`#/settings/users?x=1` → users), or null for an unknown hash. */
function viewFromHash(hash: string): ViewId | null {
  const path = hash.split('?')[0];
  const entry = (Object.keys(VIEW_HASH) as ViewId[]).find((id) => VIEW_HASH[id] === path);
  return entry ?? null;
}

/** The issue type ids of a hash query (`#/settings/data-quality?types=a,b`), in order; empty when absent. */
function typesFromHash(hash: string): string[] {
  const query = hash.split('?')[1];
  if (!query) {
    return [];
  }
  const raw = new URLSearchParams(query).get('types');
  return raw ? raw.split(',').map((type) => type.trim()).filter((type) => type.length > 0) : [];
}

function navigateTo(id: ViewId) {
  window.location.hash = VIEW_HASH[id];
}

function renderView(id: ViewId, status: SystemStatus, context: ViewContext) {
  const showLink = status.modules.dataQuality;
  switch (id) {
    case 'overview':
      return <OverviewPurchasing serverJalaliToday={status.serverJalaliToday} onOpenVendor={context.openVendor} showLink={showLink} />;
    case 'vendors':
      return <Vendors serverToday={status.serverJalaliToday} target={context.vendorTarget} showLink={showLink} canExport={status.permissions.includes('export')} canSeeFinancials={status.modules.vendorFinancials} />;
    case 'financials':
      return <VendorFinancials serverToday={status.serverJalaliToday} showLink={showLink} canExport={status.permissions.includes('export')} />;
    case 'reports':
      return <PurchasingReports serverToday={status.serverJalaliToday} showLink={showLink} canExport={status.permissions.includes('export')} />;
    case 'compare':
      return <VendorComparison serverToday={status.serverJalaliToday} showLink={showLink} canExport={status.permissions.includes('export')} />;
    case 'assistant':
      return <Assistant canExport={status.permissions.includes('export')} />;
    case 'saved':
      return <SavedReports permissions={status.permissions} serverToday={status.serverJalaliToday} />;
    case 'dataSync':
      return <DataSync />;
    case 'users':
      return <UsersRoles />;
    case 'audit':
      return <AuditLog />;
    case 'dataQuality':
      return <DataQuality focusTypes={context.dataQualityTypes} />;
    case 'aiSettings':
      return <AiSettings />;
    case 'demo':
      // Demo 1 AI chat is shown only on demo pages (research R7).
      return <Dashboard demoChatEnabled={status.modules.demoPages} />;
  }
}

function App() {
  const [user, setUser] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);
  const [loggingOut, setLoggingOut] = useState(false);
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [statusFailed, setStatusFailed] = useState(false);
  const [hash, setHash] = useState(() => window.location.hash);
  const [forbidden, setForbidden] = useState(false);
  const [vendorTarget, setVendorTarget] = useState<VendorTarget | null>(null);

  const isSignedIn = user !== null;
  // A session whose password must change first sees only the change-password screen (research R6).
  const mustChangePassword = user?.mustChangePassword === true;

  // Overview → vendor profile: opens the Vendors view on that vendor for the Overview's month.
  const openVendor = (vendor: { key: string; name: string }, month: string) => {
    setVendorTarget({ vendor, month });
    navigateTo('vendors');
  };

  // The hash selects the view; a new hash also clears any earlier no-access notice.
  useEffect(() => {
    const onHashChange = () => {
      setHash(window.location.hash);
      setForbidden(false);
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  // A page's call refused for a missing permission shows the no-access view in place of the page.
  useEffect(() => {
    const onForbidden = () => setForbidden(true);
    window.addEventListener(FORBIDDEN_EVENT, onForbidden);
    return () => window.removeEventListener(FORBIDDEN_EVENT, onForbidden);
  }, []);

  // Any call answered 403 `password-change-required` switches the session to the change-password screen.
  useEffect(() => {
    const onPasswordChangeRequired = () => {
      setUser((current) => (current ? { ...current, mustChangePassword: true } : current));
    };
    window.addEventListener(PASSWORD_CHANGE_REQUIRED_EVENT, onPasswordChangeRequired);
    return () => window.removeEventListener(PASSWORD_CHANGE_REQUIRED_EVENT, onPasswordChangeRequired);
  }, []);

  // The signed-in user, with the role label and permissions decided by the server.
  useEffect(() => {
    let active = true;

    getMe()
      .then((currentUser) => {
        if (active) {
          setUser(currentUser);
        }
      })
      .catch(() => {
        // 401 or network error sends the user to the login gate
        if (active) {
          setUser(null);
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  // Data mode, last sync and available modules for the signed-in user. Not loaded while a password change is due.
  useEffect(() => {
    if (!isSignedIn || mustChangePassword) {
      return;
    }

    let active = true;
    getSystemStatus()
      .then((systemStatus) => {
        if (active) {
          setStatus(systemStatus);
          setStatusFailed(false);
        }
      })
      .catch((error: unknown) => {
        if (!active) {
          return;
        }
        if (isUnauthenticated(error)) {
          setUser(null);
          return;
        }
        setStatusFailed(true);
      });

    return () => {
      active = false;
    };
  }, [isSignedIn, mustChangePassword]);

  const handlePasswordChanged = () => {
    // Reload the session: the flag is cleared on the server, and the status loads through the effect above.
    getMe()
      .then((currentUser) => setUser(currentUser))
      .catch(() => setUser(null));
  };

  const handleLogout = async () => {
    setLoggingOut(true);
    try {
      await apiPost('/api/auth/logout');
    } catch {
      // Regardless of network status, clear the local state to return to gate
    } finally {
      setUser(null);
      setStatus(null);
      setStatusFailed(false);
      setForbidden(false);
      setVendorTarget(null);
      setLoggingOut(false);
    }
  };

  const selectedId = viewFromHash(hash);
  const visibleTabs = status ? VIEW_TABS.filter((tab) => tab.isVisible(status.modules)) : [];
  const requestedTab: ViewTab | null = selectedId ? VIEW_TABS.find((tab) => tab.id === selectedId) ?? null : null;
  // A hash naming a view whose module the server does not grant renders the no-access view.
  const requestedDenied = Boolean(status && requestedTab && !requestedTab.isVisible(status.modules));
  const currentTab: ViewTab | null = requestedDenied
    ? null
    : requestedTab && visibleTabs.includes(requestedTab)
      ? requestedTab
      : visibleTabs[0] ?? null;
  const showNoAccess = !mustChangePassword && (forbidden || requestedDenied);

  return (
    <div className="min-h-screen flex flex-col bg-gray-50 text-gray-900" dir="rtl">
      {/* Demonstration data notice: shown only in demo installs (never in production) */}
      {status?.dataMode === 'demo' && <DemoDataBanner />}

      {loading ? (
        <div className="flex-1 flex items-center justify-center p-8">
          <div className="text-center space-y-3">
            <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
            <p className="text-sm font-medium text-gray-600">در حال بررسی وضعیت دسترسی...</p>
          </div>
        </div>
      ) : !user ? (
        <main className="flex-1">
          <Login
            onLoginSuccess={(signedIn) => {
              setForbidden(false);
              setUser(signedIn);
            }}
          />
        </main>
      ) : (
        <div className="flex-1 flex flex-col">
          <header className="bg-white border-b border-gray-200 px-6 py-4 shadow-xs">
            <div className="max-w-7xl mx-auto flex items-center justify-between gap-4">
              <div>
                <h1 className="text-xl font-bold text-gray-900">زارفا — سامانه تحلیلی</h1>
                <p className="text-xs text-gray-500">داشبورد هوشمند شرکت پخش دارویی</p>
              </div>

              <div className="flex items-center gap-4">
                {status && <DataModeBadge dataLabel={status.dataLabel} />}

                {(status || statusFailed) && (
                  <LastSyncIndicator
                    lastSuccessfulSync={status?.lastSuccessfulSync ?? null}
                    isUnknown={status === null}
                  />
                )}

                <div className="text-left sm:text-right">
                  <div className="text-sm font-semibold text-gray-800">{user.username}</div>
                  <div className="text-xs text-gray-500">
                    نقش:{' '}
                    <span className="font-medium text-blue-700">{user.roleLabel}</span>
                  </div>
                </div>

                <button
                  type="button"
                  onClick={handleLogout}
                  disabled={loggingOut}
                  className="px-3 py-1.5 text-xs sm:text-sm font-medium text-red-700 bg-red-50 hover:bg-red-100 border border-red-200 rounded-lg transition disabled:opacity-60 cursor-pointer"
                >
                  {loggingOut ? 'در حال خروج...' : 'خروج'}
                </button>
              </div>
            </div>
          </header>

          <main className="flex-1 max-w-7xl w-full mx-auto p-6 space-y-6">
            {mustChangePassword ? (
              <ChangePassword mustChange onDone={handlePasswordChanged} />
            ) : (
              <>
                {statusFailed && (
                  <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
                    وضعیت سامانه در دسترس نیست. صفحه را دوباره بارگذاری کنید.
                  </p>
                )}

                {!status && !statusFailed && (
                  <p className="text-sm text-gray-600">در حال بارگذاری وضعیت سامانه...</p>
                )}

                {status && (
                  <>
                    <nav aria-label="بخش‌های سامانه" className="flex flex-wrap gap-2">
                      {visibleTabs.map((tab) => {
                        const active = currentTab?.id === tab.id;
                        return (
                          <button
                            key={tab.id}
                            type="button"
                            aria-current={active ? 'page' : undefined}
                            onClick={() => navigateTo(tab.id)}
                            className={`px-3 py-1.5 text-sm font-medium rounded-lg border transition cursor-pointer ${
                              active
                                ? 'bg-blue-600 text-white border-blue-600'
                                : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-100'
                            }`}
                          >
                            {tab.label}
                          </button>
                        );
                      })}
                    </nav>

                    {showNoAccess && <NoAccess />}
                    {!showNoAccess && currentTab && renderView(currentTab.id, status, { vendorTarget, openVendor, dataQualityTypes: typesFromHash(hash) })}
                  </>
                )}
              </>
            )}
          </main>
        </div>
      )}
    </div>
  );
}

export default App;
