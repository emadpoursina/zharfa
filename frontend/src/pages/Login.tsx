import { useState, type FormEvent } from 'react';
import { apiPost, ApiError } from '../services/api';

interface UserInfo {
  username: string;
  role: string;
}

interface LoginProps {
  onLoginSuccess: (user: UserInfo) => void;
}

export function Login({ onLoginSuccess }: LoginProps) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    const trimmedUsername = username.trim();
    if (!trimmedUsername || !password) {
      setErrorMessage('لطفاً نام کاربری و گذرواژه را وارد کنید.');
      return;
    }

    setLoading(true);
    setErrorMessage(null);

    try {
      const user = await apiPost<UserInfo>('/api/auth/login', {
        username: trimmedUsername,
        password,
      });
      onLoginSuccess(user);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          setErrorMessage('نام کاربری یا گذرواژه اشتباه است.');
        } else {
          setErrorMessage(err.message || 'خطایی در ورود به سیستم رخ داد.');
        }
      } else {
        setErrorMessage('خطا در برقراری ارتباط با سرور.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-[calc(100vh-42px)] flex items-center justify-center p-4 bg-gray-50">
      <div className="w-full max-w-md bg-white rounded-xl shadow-lg border border-gray-200 p-8 space-y-6">
        <div className="text-center space-y-2">
          <h1 className="text-2xl font-bold text-gray-900 tracking-tight">
            سامانه تحلیلی زارفا
          </h1>
          <p className="text-sm text-gray-600">
            برای دسترسی به داشبورد و تحلیل‌های کسب‌وکار وارد شوید
          </p>
        </div>

        {errorMessage && (
          <div
            role="alert"
            className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-700 text-right leading-relaxed"
          >
            {errorMessage}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-5" noValidate>
          <div className="space-y-1.5 text-right">
            <label
              htmlFor="username"
              className="block text-sm font-semibold text-gray-700"
            >
              نام کاربری
            </label>
            <input
              id="username"
              name="username"
              type="text"
              dir="ltr"
              autoComplete="username"
              required
              disabled={loading}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full px-3.5 py-2.5 rounded-lg border border-gray-300 text-gray-900 shadow-xs focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 disabled:bg-gray-100 disabled:cursor-not-allowed transition"
              placeholder="admin یا manager"
            />
          </div>

          <div className="space-y-1.5 text-right">
            <label
              htmlFor="password"
              className="block text-sm font-semibold text-gray-700"
            >
              گذرواژه
            </label>
            <input
              id="password"
              name="password"
              type="password"
              dir="ltr"
              autoComplete="current-password"
              required
              disabled={loading}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-3.5 py-2.5 rounded-lg border border-gray-300 text-gray-900 shadow-xs focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 disabled:bg-gray-100 disabled:cursor-not-allowed transition"
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full flex justify-center items-center py-2.5 px-4 border border-transparent rounded-lg shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 focus:outline-hidden focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-60 disabled:cursor-not-allowed transition cursor-pointer"
          >
            {loading ? 'در حال ورود...' : 'ورود'}
          </button>
        </form>
      </div>
    </div>
  );
}
