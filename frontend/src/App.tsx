import { useEffect, useState } from 'react';
import { DemoDataBanner } from './components/DemoDataBanner';
import { Dashboard } from './pages/Dashboard';
import { Login } from './pages/Login';
import { apiGet, apiPost } from './services/api';

interface UserInfo {
  username: string;
  role: string;
}

function App() {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [loggingOut, setLoggingOut] = useState(false);

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

  const handleLogout = async () => {
    setLoggingOut(true);
    try {
      await apiPost('/api/auth/logout');
    } catch {
      // Regardless of network status, clear the local state to return to gate
    } finally {
      setUser(null);
      setLoggingOut(false);
    }
  };

  return (
    <div className="min-h-screen flex flex-col bg-gray-50 text-gray-900" dir="rtl">
      {/* Persistent demonstration data notification */}
      <DemoDataBanner />

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
            <div className="max-w-7xl mx-auto flex items-center justify-between">
              <div>
                <h1 className="text-xl font-bold text-gray-900">زارفا — سامانه تحلیلی</h1>
                <p className="text-xs text-gray-500">داشبورد هوشمند شرکت پخش دارویی</p>
              </div>

              <div className="flex items-center gap-4">
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

          <main className="flex-1 max-w-7xl w-full mx-auto p-6">
            <Dashboard />
          </main>
        </div>
      )}
    </div>
  );
}

export default App;
