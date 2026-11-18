import { useState, type FormEvent } from 'react';
import { ApiError, changePassword } from '../services/api';

/** S-PW-MIN (contracts/api.md §9): the same rule the server enforces; the server stays the authority. */
const MIN_PASSWORD_LENGTH = 8;
const PASSWORD_TOO_SHORT = 'گذرواژه باید حداقل ۸ نویسه باشد.';
const PASSWORD_MISMATCH = 'تکرار گذرواژه با گذرواژهٔ جدید یکسان نیست.';
const NETWORK_ERROR = 'خطا در برقراری ارتباط با سرور.';

const INPUT_CLASS =
  'w-full px-3.5 py-2.5 rounded-lg border border-gray-300 text-gray-900 shadow-xs focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 disabled:bg-gray-100 disabled:cursor-not-allowed transition';

interface ChangePasswordProps {
  /** True when the sign-in requires the change first; shows the reason above the form. */
  mustChange?: boolean;
  /** Called after the server answers 204. */
  onDone: () => void;
}

/**
 * Change-password form (US1, contracts/api.md §1). It is the only screen a session may use while a change is
 * required. The server checks the current password, the length and that the new one differs.
 */
export function ChangePassword({ mustChange = false, onDone }: ChangePasswordProps) {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [repeatPassword, setRepeatPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (newPassword.length < MIN_PASSWORD_LENGTH) {
      setErrorMessage(PASSWORD_TOO_SHORT);
      return;
    }
    if (newPassword !== repeatPassword) {
      setErrorMessage(PASSWORD_MISMATCH);
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    let changed = false;
    try {
      await changePassword(currentPassword, newPassword);
      changed = true;
    } catch (error: unknown) {
      setErrorMessage(error instanceof ApiError ? error.message : NETWORK_ERROR);
    } finally {
      setSubmitting(false);
    }

    if (changed) {
      onDone();
    }
  };

  return (
    <div className="min-h-[calc(100vh-42px)] flex items-center justify-center p-4 bg-gray-50" dir="rtl">
      <div className="w-full max-w-md bg-white rounded-xl shadow-lg border border-gray-200 p-8 space-y-6">
        <div className="text-center space-y-2">
          <h1 className="text-2xl font-bold text-gray-900 tracking-tight">تغییر گذرواژه</h1>
          {mustChange && (
            <p className="text-sm text-gray-700">
              برای ادامهٔ کار باید گذرواژهٔ خود را تغییر دهید.
            </p>
          )}
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
            <label htmlFor="current-password" className="block text-sm font-semibold text-gray-700">
              گذرواژهٔ فعلی
            </label>
            <input
              id="current-password"
              name="currentPassword"
              type="password"
              dir="ltr"
              autoComplete="current-password"
              required
              disabled={submitting}
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
              className={INPUT_CLASS}
            />
          </div>

          <div className="space-y-1.5 text-right">
            <label htmlFor="new-password" className="block text-sm font-semibold text-gray-700">
              گذرواژهٔ جدید
            </label>
            <input
              id="new-password"
              name="newPassword"
              type="password"
              dir="ltr"
              autoComplete="new-password"
              required
              disabled={submitting}
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              className={INPUT_CLASS}
            />
            <p className="text-xs text-gray-600">حداقل ۸ نویسه.</p>
          </div>

          <div className="space-y-1.5 text-right">
            <label htmlFor="repeat-password" className="block text-sm font-semibold text-gray-700">
              تکرار گذرواژهٔ جدید
            </label>
            <input
              id="repeat-password"
              name="repeatPassword"
              type="password"
              dir="ltr"
              autoComplete="new-password"
              required
              disabled={submitting}
              value={repeatPassword}
              onChange={(event) => setRepeatPassword(event.target.value)}
              className={INPUT_CLASS}
            />
          </div>

          <button
            type="submit"
            disabled={submitting}
            className="w-full flex justify-center items-center py-2.5 px-4 border border-transparent rounded-lg shadow-sm text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 focus:outline-hidden focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-60 disabled:cursor-not-allowed transition cursor-pointer"
          >
            {submitting ? 'در حال ثبت...' : 'ثبت گذرواژه'}
          </button>
        </form>
      </div>
    </div>
  );
}
