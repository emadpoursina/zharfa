import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { DataModeBadge } from '../components/DataModeBadge';
import { toPersianDigits } from '../utils/jalali';
import {
  ApiError,
  createUser,
  deactivateUser,
  getRoles,
  getUsers,
  reactivateUser,
  resetUserPassword,
  setUserRole,
  unlockUser,
  type RoleInfo,
  type UserRow,
  type UsersResponse,
} from '../services/api';

/** S-PW-MIN (contracts/api.md §9): the client check; the server enforces the same rule. */
const PASSWORD_TOO_SHORT = 'گذرواژه باید حداقل ۸ نویسه باشد.';
const NETWORK_ERROR = 'خطا در برقراری ارتباط با سرور.';
const DEFAULT_ROLE = 'purchasing';
const INPUT_CLASS =
  'px-3 py-2 rounded-lg border border-gray-300 text-gray-900 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-blue-600 focus:border-blue-600 disabled:bg-gray-100 disabled:cursor-not-allowed transition';
const BUTTON_CLASS =
  'px-3 py-1.5 text-xs sm:text-sm font-medium rounded-lg border transition cursor-pointer focus:outline-hidden focus-visible:ring-2 focus-visible:ring-blue-600 focus-visible:ring-offset-1 disabled:opacity-60 disabled:cursor-not-allowed';
const PRIMARY_BUTTON = `${BUTTON_CLASS} text-white bg-blue-600 border-blue-600 hover:bg-blue-700`;
const SECONDARY_BUTTON = `${BUTTON_CLASS} text-gray-800 bg-white border-gray-300 hover:bg-gray-100`;

function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : NETWORK_ERROR;
}

interface RunAction {
  (action: () => Promise<unknown>): Promise<boolean>;
}

interface UserRowProps {
  user: UserRow;
  roles: RoleInfo[];
  busy: boolean;
  runAction: RunAction;
}

/** One user: role change, password reset, activation and unlock. Every action refreshes the list. */
function UserRowView({ user, roles, busy, runAction }: UserRowProps) {
  const [roleDraft, setRoleDraft] = useState(user.role);
  const [passwordDraft, setPasswordDraft] = useState('');
  const [passwordHint, setPasswordHint] = useState<string | null>(null);

  // The list reloads after every action, so the draft follows the stored role again.
  useEffect(() => {
    setRoleDraft(user.role);
  }, [user.role]);

  const handleResetPassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (passwordDraft.length < 8) {
      setPasswordHint(PASSWORD_TOO_SHORT);
      return;
    }
    setPasswordHint(null);
    const done = await runAction(() => resetUserPassword(user.id, passwordDraft));
    if (done) {
      setPasswordDraft('');
    }
  };

  return (
    <tr className="align-top">
      <th scope="row" className="px-3 py-3 text-right font-semibold text-gray-900 whitespace-nowrap" dir="ltr">
        {user.username}
      </th>
      <td className="px-3 py-3 text-right whitespace-nowrap">
        <div className="flex items-center gap-2">
          <label htmlFor={`role-${user.id}`} className="sr-only">
            نقش {user.username}
          </label>
          <select
            id={`role-${user.id}`}
            value={roleDraft}
            disabled={busy}
            onChange={(event) => setRoleDraft(event.target.value)}
            className={INPUT_CLASS}
          >
            {roles.map((role) => (
              <option key={role.id} value={role.id}>
                {role.label}
              </option>
            ))}
          </select>
          <button
            type="button"
            disabled={busy || roleDraft === user.role}
            onClick={() => void runAction(() => setUserRole(user.id, roleDraft))}
            className={SECONDARY_BUTTON}
          >
            ذخیرهٔ نقش
          </button>
        </div>
      </td>
      <td className="px-3 py-3 text-right whitespace-nowrap">
        <span
          className={`inline-block rounded-md border px-2 py-0.5 text-xs font-medium ${
            user.active
              ? 'bg-green-50 text-green-800 border-green-200'
              : 'bg-gray-100 text-gray-700 border-gray-300'
          }`}
        >
          {user.active ? 'فعال' : 'غیرفعال'}
        </span>
      </td>
      <td className="px-3 py-3 text-right whitespace-nowrap text-gray-700">
        {user.locked && user.lockedUntilJalali ? toPersianDigits(user.lockedUntilJalali) : '—'}
      </td>
      <td className="px-3 py-3 text-right whitespace-nowrap text-gray-700">
        {user.mustChangePassword ? 'بله' : 'خیر'}
      </td>
      <td className="px-3 py-3 text-right whitespace-nowrap text-gray-700">{toPersianDigits(user.createdAtJalali)}</td>
      <td className="px-3 py-3 text-right">
        <div className="flex flex-wrap items-start gap-2">
          <form onSubmit={handleResetPassword} className="flex flex-wrap items-start gap-2" noValidate>
            <label htmlFor={`password-${user.id}`} className="sr-only">
              گذرواژهٔ جدید {user.username}
            </label>
            <input
              id={`password-${user.id}`}
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={passwordDraft}
              disabled={busy}
              onChange={(event) => setPasswordDraft(event.target.value)}
              className={`${INPUT_CLASS} w-40`}
              aria-describedby={passwordHint ? `password-hint-${user.id}` : undefined}
            />
            <button type="submit" disabled={busy} className={SECONDARY_BUTTON}>
              بازنشانی گذرواژه
            </button>
            {passwordHint && (
              <p id={`password-hint-${user.id}`} className="w-full text-xs text-red-700">
                {passwordHint}
              </p>
            )}
          </form>

          <button
            type="button"
            disabled={busy}
            onClick={() =>
              void runAction(() => (user.active ? deactivateUser(user.id) : reactivateUser(user.id)))
            }
            className={SECONDARY_BUTTON}
          >
            {user.active ? 'غیرفعال‌سازی' : 'فعال‌سازی'}
          </button>

          {user.locked && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void runAction(() => unlockUser(user.id))}
              className={PRIMARY_BUTTON}
            >
              رفع قفل
            </button>
          )}
        </div>
      </td>
    </tr>
  );
}

/**
 * Users and roles (US1, Admin only, contracts/api.md §2): the list with status, lock and password-change flags, the
 * create form, per-user actions, and each role with its permission labels. The server enforces the Admin
 * permission and the last-Admin rule; this page shows its messages in a role="alert" area.
 */
export function UsersRoles() {
  const [roles, setRoles] = useState<RoleInfo[]>([]);
  const [data, setData] = useState<UsersResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [newUsername, setNewUsername] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState(DEFAULT_ROLE);

  const load = useCallback(async () => {
    try {
      const [loadedRoles, loadedUsers] = await Promise.all([getRoles(), getUsers()]);
      setRoles(loadedRoles);
      setData(loadedUsers);
      setLoadError(null);
    } catch (error: unknown) {
      setLoadError(messageOf(error));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const runAction: RunAction = async (action) => {
    setBusy(true);
    setActionError(null);
    try {
      await action();
      await load();
      return true;
    } catch (error: unknown) {
      setActionError(messageOf(error));
      return false;
    } finally {
      setBusy(false);
    }
  };

  const handleCreate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (newPassword.length < 8) {
      setActionError(PASSWORD_TOO_SHORT);
      return;
    }
    const done = await runAction(() => createUser(newUsername.trim(), newPassword, newRole));
    if (done) {
      setNewUsername('');
      setNewPassword('');
      setNewRole(DEFAULT_ROLE);
    }
  };

  return (
    <div className="space-y-6" dir="rtl">
      <section aria-labelledby="users-heading" className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h2 id="users-heading" className="text-lg font-semibold text-gray-900">
              کاربران و نقش‌ها
            </h2>
            <p className="mt-1 text-sm text-gray-600">
              کاربران را بسازید، نقش آنان را تعیین کنید و دسترسی‌ها را مدیریت کنید. تغییر نقش یا غیرفعال‌سازی از درخواست بعدی
              همان کاربر اعمال می‌شود.
            </p>
          </div>
          {data && <DataModeBadge dataLabel={data.dataLabel} />}
        </div>

        {loadError && (
          <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
            {loadError}
          </p>
        )}

        {actionError && (
          <p role="alert" className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg p-3">
            {actionError}
          </p>
        )}

        <form onSubmit={handleCreate} className="grid gap-4 sm:grid-cols-4 items-end" noValidate>
          <div className="space-y-1.5">
            <label htmlFor="new-user-username" className="block text-sm font-semibold text-gray-700">
              نام کاربری
            </label>
            <input
              id="new-user-username"
              type="text"
              dir="ltr"
              autoComplete="off"
              value={newUsername}
              disabled={busy}
              onChange={(event) => setNewUsername(event.target.value)}
              className={`${INPUT_CLASS} w-full`}
            />
          </div>
          <div className="space-y-1.5">
            <label htmlFor="new-user-password" className="block text-sm font-semibold text-gray-700">
              گذرواژه
            </label>
            <input
              id="new-user-password"
              type="password"
              dir="ltr"
              autoComplete="new-password"
              value={newPassword}
              disabled={busy}
              onChange={(event) => setNewPassword(event.target.value)}
              className={`${INPUT_CLASS} w-full`}
            />
          </div>
          <div className="space-y-1.5">
            <label htmlFor="new-user-role" className="block text-sm font-semibold text-gray-700">
              نقش
            </label>
            <select
              id="new-user-role"
              value={newRole}
              disabled={busy}
              onChange={(event) => setNewRole(event.target.value)}
              className={`${INPUT_CLASS} w-full`}
            >
              {roles.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.label}
                </option>
              ))}
            </select>
          </div>
          <button type="submit" disabled={busy} className={PRIMARY_BUTTON}>
            ایجاد کاربر
          </button>
        </form>

        <div className="overflow-x-auto">
          <table className="min-w-full text-sm divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">نام کاربری</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">نقش</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">وضعیت</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">قفل تا</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">تغییر گذرواژه</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">ایجاد</th>
                <th scope="col" className="px-3 py-2 text-right font-semibold text-gray-700">عملیات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {data?.users.map((user) => (
                <UserRowView key={user.id} user={user} roles={roles} busy={busy} runAction={runAction} />
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="roles-heading" className="bg-white border border-gray-200 rounded-xl p-6 shadow-xs space-y-4">
        <h2 id="roles-heading" className="text-lg font-semibold text-gray-900">
          نقش‌ها و دسترسی‌های هر نقش
        </h2>
        <div className="grid gap-4 md:grid-cols-2">
          {roles.map((role) => (
            <div key={role.id} className="border border-gray-200 rounded-lg p-4 space-y-2">
              <h3 className="text-sm font-semibold text-gray-900">{role.label}</h3>
              <ul className="list-disc pr-5 space-y-1 text-sm text-gray-700">
                {role.permissions.map((permission) => (
                  <li key={permission.id}>{permission.label}</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
