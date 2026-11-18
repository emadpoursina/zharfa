import { useEffect, useState } from 'react';
import { DataModeBadge } from './components/DataModeBadge';
import { DemoDataBanner } from './components/DemoDataBanner';
import { LastSyncIndicator } from './components/LastSyncIndicator';
import { DataSync } from './pages/DataSync';
import { Dashboard } from './pages/Dashboard';
import { OverviewPurchasing } from './pages/OverviewPurchasing';
import { PurchasingReports } from './pages/PurchasingReports';
import { VendorComparison } from './pages/VendorComparison';
import { Vendors, type VendorTarget } from './pages/Vendors';
import { Login } from './pages/Login';
import {
  apiGet,
  apiPost,
  getSystemStatus,
  isUnauthenticated,
  type SystemModules,
  type SystemStatus,
} from './services/api';

interface UserInfo {
  username: string;
  role: string;
}

type ViewId = 'overview' | 'vendors' | 'reports' | 'compare' | 'dataSync' | 'demo';

/**
 * Views of the shell (no router). Which views exist is decided by the server in
 * GET /api/system/status (`modules`); the UI only shows what the server allows.
 */
const VIEW_TABS: ReadonlyArray<{
  id: ViewId;
  label: string;
  isVisible: (modules: SystemModules) => boolean;
}> = [
  { id: 'overview', label: 'نمای کلی خرید', isVisible: (modules) => modules.overview },
  { id: 'vendors', label: 'تأمین‌کنندگان', isVisible: (modules) => modules.purchasingReports },
  { id: 'reports', label: 'گزارش‌های خرید', isVisible: (modules) => modules.purchasingReports },
  { id: 'compare', label: 'مقایسه تأمین‌کنندگان', isVisible: (modules) => modules.purchasingReports },
  { id: 'dataSync', label: 'تنظیمات ← همگام‌سازی داده', isVisible: (modules) => modules.dataSync },
  { id: 'demo', label: 'صفحات نمایشی', isVisible: (modules) => modules.demoPages },
];

type ViewTab = (typeof VIEW_TABS)[number];

interface ViewContext {
  vendorTarget: VendorTarget | null;
  openVendor: (vendor: { key: string; name: string }, month: string) => void;
}

function renderView(id: ViewId, status: SystemStatus, context: ViewContext) {
  switch (id) {
    case 'overview':
      return <OverviewPurchasing serverJalaliToday={status.serverJalaliToday} onOpenVendor={context.openVendor} />;
    case 'vendors':
      return <Vendors serverToday={status.serverJalaliToday} target={context.vendorTarget} />;
    case 'reports':
      return <PurchasingReports serverToday={status.serverJalaliToday} />;
    case 'compare':
      return <VendorComparison serverToday={status.serverJalaliToday} />;
    case 'dataSync':
      return <DataSync />;
    case 'demo':
      // The AI agent is a separate module: hidden in production until Phase 4 (modules.ai).
      return <Dashboard aiEnabled={status.modules.ai} />;
  }
}

function App() {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [loggingOut, setLoggingOut] = useState(false);
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [statusFailed, setStatusFailed] = useState(false);
  const [requestedView, setRequestedView] = useState<ViewId>('overview');
  const [vendorTarget, setVendorTarget] = useState<VendorTarget | null>(null);

  const isSignedIn = user !== null;

  // Overview → vendor profile: opens the Vendors view on that vendor for the Overview's month.
  const openVendor = (vendor: { key: string; name: string }, month: string) => {
    setVendorTarget({ vendor, month });
    setRequestedView('vendors');
  };

  useEffect(() => {
    let active = true;

    apiGet<UserInfo>('/api/auth/me')
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

  // Data mode, last sync and available modules for the signed-in user.
  useEffect(() => {
    if (!isSignedIn) {
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
  }, [isSignedIn]);

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
      setRequestedView('overview');
      setVendorTarget(null);
      setLoggingOut(false);
    }
  };

  const visibleTabs = status ? VIEW_TABS.filter((tab) => tab.isVisible(status.modules)) : [];
  const currentTab: ViewTab | null =
    visibleTabs.find((tab) => tab.id === requestedView) ?? visibleTabs[0] ?? null;

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
          <Login onLoginSuccess={(u) => setUser(u)} />
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
                    <span className="font-medium text-blue-700">
                      {user.role === 'admin' ? 'مدیر ارشد' : 'مدیر'}
                    </span>
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
                        onClick={() => setRequestedView(tab.id)}
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

                {currentTab && renderView(currentTab.id, status, { vendorTarget, openVendor })}
              </>
            )}
          </main>
        </div>
      )}
    </div>
  );
}

export default App;
