import { useEffect, useState } from 'react';
import {
  getAiSettings,
  testAiProvider,
  setAiCloud,
  ApiError,
  type AiSettingsResponse,
} from '../services/api';

export function AiSettings() {
  const [settings, setSettings] = useState<AiSettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Connection testing state
  const [testingTarget, setTestingTarget] = useState<'local' | 'cloud' | null>(null);
  const [testResult, setTestResult] = useState<{ target: 'local' | 'cloud'; ok: boolean; message: string } | null>(null);

  // Cloud toggle state
  const [approvalNote, setApprovalNote] = useState('');
  const [noteError, setNoteError] = useState<string | null>(null);
  const [savingCloud, setSavingCloud] = useState(false);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  const loadSettings = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getAiSettings();
      setSettings(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'خطا در بارگذاری تنظیمات هوش مصنوعی.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadSettings();
  }, []);

  const handleTest = async (target: 'local' | 'cloud') => {
    setTestingTarget(target);
    setTestResult(null);
    setActionSuccess(null);
    try {
      const res = await testAiProvider(target);
      setTestResult({ target, ok: res.ok, message: res.message });
    } catch (err) {
      setTestResult({
        target,
        ok: false,
        message: err instanceof ApiError ? err.message : 'خطا در برقراری ارتباط با ارائه‌دهنده.',
      });
    } finally {
      setTestingTarget(null);
    }
  };

  const handleEnableCloud = async () => {
    const trimmed = approvalNote.trim();
    if (!trimmed) {
      setNoteError('یادداشت تأیید مدیرعامل الزامی است.');
      return;
    }

    setSavingCloud(true);
    setNoteError(null);
    setActionSuccess(null);
    try {
      const updated = await setAiCloud(true, trimmed);
      setSettings(updated);
      setApprovalNote('');
      setActionSuccess('ارائه‌دهنده ابری با موفقیت فعال شد.');
    } catch (err) {
      setNoteError(err instanceof ApiError ? err.message : 'خطا در فعال‌سازی ارائه‌دهنده ابری.');
    } finally {
      setSavingCloud(false);
    }
  };

  const handleDisableCloud = async () => {
    setSavingCloud(true);
    setNoteError(null);
    setActionSuccess(null);
    try {
      const updated = await setAiCloud(false);
      setSettings(updated);
      setActionSuccess('ارائه‌دهنده ابری غیرفعال شد و پاسخ‌گویی به مدل محلی بازگشت.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'خطا در غیرفعال‌سازی ارائه‌دهنده ابری.');
    } finally {
      setSavingCloud(false);
    }
  };

  if (loading) {
    return (
      <div className="bg-white rounded-xl border border-gray-200 p-12 text-center space-y-3" dir="rtl">
        <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-blue-600 border-r-transparent align-[-0.125em]" />
        <p className="text-xs font-medium text-gray-600">در حال دریافت تنظیمات هوش مصنوعی...</p>
      </div>
    );
  }

  if (error || !settings) {
    return (
      <div role="alert" className="p-4 bg-rose-50 border border-rose-200 text-rose-800 text-xs rounded-xl font-medium" dir="rtl">
        {error || 'تنظیمات در دسترس نیست.'}
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto space-y-6" dir="rtl">
      {/* Header */}
      <header className="space-y-1">
        <h1 className="text-xl font-bold text-gray-900">تنظیمات ارائه‌دهنده هوش مصنوعی</h1>
        <p className="text-xs text-gray-600">
          مدیریت ارائه‌دهنده پاسخ‌گوی تحلیل هوشمند خرید، بررسی وضعیت اتصال و ضوابط فعال‌سازی مدل ابری.
        </p>
      </header>

      {/* Overview Status Card */}
      <section className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-4 pb-4 border-b border-gray-100">
          <div>
            <span className="text-xs text-gray-500">ارائه‌دهنده پاسخ‌گو در حال حاضر:</span>
            <div className="text-base font-bold text-gray-900 mt-0.5">
              {settings.answeringProvider === 'cloud' ? (
                <span className="text-purple-700">ارائه‌دهنده ابری (Cloud)</span>
              ) : (
                <span className="text-emerald-700">ارائه‌دهنده محلی شرکت (Local Default)</span>
              )}
            </div>
          </div>
          <span className={`px-3 py-1 rounded-full text-xs font-semibold ${
            settings.answeringProvider === 'cloud'
              ? 'bg-purple-100 text-purple-800'
              : 'bg-emerald-100 text-emerald-800'
          }`}>
            {settings.answeringProvider === 'cloud' ? 'پاسخ‌گویی ابری' : 'پاسخ‌گویی محلی'}
          </span>
        </div>

        {/* Aggregates-only notice */}
        <div className="p-3 bg-blue-50 border border-blue-200 text-blue-900 text-xs rounded-lg font-medium leading-relaxed">
          {settings.aggregatesOnlyNotice}
        </div>

        {actionSuccess && (
          <div role="status" className="p-3 bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs rounded-lg font-medium">
            {actionSuccess}
          </div>
        )}
      </section>

      {/* Local Provider (Default) */}
      <section className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-bold text-gray-900">ارائه‌دهنده محلی (Local AI)</h2>
            <span className="px-2 py-0.5 bg-blue-100 text-blue-800 text-[11px] font-semibold rounded">پیش‌فرض سرور</span>
          </div>
          <span className={`text-xs font-medium px-2.5 py-1 rounded-full ${
            settings.local.configured ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-gray-100 text-gray-600'
          }`}>
            {settings.local.configured ? 'پیکربندی‌شده' : 'پیکربندی‌نشده'}
          </span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-xs bg-gray-50 p-4 rounded-lg">
          <div>
            <span className="text-gray-500 block">نام سرویس:</span>
            <strong className="text-gray-800">{settings.local.name || '—'}</strong>
          </div>
          <div>
            <span className="text-gray-500 block">آدرس پایانه (Endpoint):</span>
            <span className="font-mono text-gray-800 break-all">{settings.local.endpoint || '—'}</span>
          </div>
          <div>
            <span className="text-gray-500 block">مدل برگزیده:</span>
            <span className="font-mono text-gray-800">{settings.local.model || '—'}</span>
          </div>
        </div>

        <div className="flex items-center justify-between pt-2">
          <span className="text-[11px] text-gray-500">تنظیمات محلی از متغیرهای محیطی LocalAi__* خوانده می‌شود.</span>
          <button
            type="button"
            onClick={() => handleTest('local')}
            disabled={testingTarget !== null}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-gray-800 bg-gray-100 hover:bg-gray-200 border border-gray-300 transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
          >
            {testingTarget === 'local' ? 'در حال آزمون...' : 'آزمون اتصال محلی'}
          </button>
        </div>

        {testResult && testResult.target === 'local' && (
          <div
            role="status"
            className={`p-3 text-xs rounded-lg font-medium border ${
              testResult.ok
                ? 'bg-emerald-50 border-emerald-200 text-emerald-800'
                : 'bg-rose-50 border-rose-200 text-rose-800'
            }`}
          >
            {testResult.message}
          </div>
        )}
      </section>

      {/* Cloud Provider (Optional) */}
      <section className="bg-white rounded-xl border border-gray-200 p-6 shadow-xs space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-bold text-gray-900">ارائه‌دهنده ابری (Cloud AI)</h2>
            <span className="px-2 py-0.5 bg-purple-100 text-purple-800 text-[11px] font-semibold rounded">اختیاری</span>
          </div>
          <span className={`text-xs font-medium px-2.5 py-1 rounded-full ${
            settings.cloud.enabled ? 'bg-purple-100 text-purple-800' : 'bg-gray-100 text-gray-600'
          }`}>
            {settings.cloud.enabled ? 'فعال‌شده' : 'غیرفعال'}
          </span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-xs bg-gray-50 p-4 rounded-lg">
          <div>
            <span className="text-gray-500 block">نام سرویس:</span>
            <strong className="text-gray-800">{settings.cloud.name || '—'}</strong>
          </div>
          <div>
            <span className="text-gray-500 block">آدرس پایانه (Endpoint):</span>
            <span className="font-mono text-gray-800 break-all">{settings.cloud.endpoint || '—'}</span>
          </div>
          <div>
            <span className="text-gray-500 block">مدل ابری:</span>
            <span className="font-mono text-gray-800">{settings.cloud.model || '—'}</span>
          </div>
        </div>

        <div className="flex items-center justify-between pt-2">
          <span className="text-[11px] text-gray-500">کلید دسترسی فقط در اسرار سرور نگه‌داری می‌شود و در سامانه نمایش داده نمی‌شود.</span>
          <button
            type="button"
            onClick={() => handleTest('cloud')}
            disabled={testingTarget !== null}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-gray-800 bg-gray-100 hover:bg-gray-200 border border-gray-300 transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-blue-600"
          >
            {testingTarget === 'cloud' ? 'در حال آزمون...' : 'آزمون اتصال ابری'}
          </button>
        </div>

        {testResult && testResult.target === 'cloud' && (
          <div
            role="status"
            className={`p-3 text-xs rounded-lg font-medium border ${
              testResult.ok
                ? 'bg-emerald-50 border-emerald-200 text-emerald-800'
                : 'bg-rose-50 border-rose-200 text-rose-800'
            }`}
          >
            {testResult.message}
          </div>
        )}

        {/* Cloud Enable/Disable Controls (User Story 5) */}
        <div className="pt-4 border-t border-gray-200 space-y-3">
          <h3 className="text-xs font-bold text-gray-800">وضعیت فعال‌سازی و تأیید مالک:</h3>

          {settings.cloud.enabled ? (
            <div className="space-y-3 bg-purple-50/50 p-4 rounded-lg border border-purple-100">
              <p className="text-xs text-purple-900 leading-relaxed font-medium">
                ارائه‌دهنده ابری در حال حاضر فعال است و تمام پرسش‌های خرید به صورت نتایج تجمیعی به این ارائه‌دهنده ارسال می‌شوند.
                {settings.cloud.ownerApprovalRecorded && ' (تأییدیه مدیرعامل در سوابق سامانه ثبت شده است.)'}
              </p>
              <button
                type="button"
                onClick={handleDisableCloud}
                disabled={savingCloud}
                className="px-4 py-2 rounded-lg text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-rose-600"
              >
                {savingCloud ? 'در حال اعمال...' : 'غیرفعال‌سازی ارائه‌دهنده ابری و بازگشت به مدل محلی'}
              </button>
            </div>
          ) : (
            <div className="space-y-3 bg-gray-50 p-4 rounded-lg border border-gray-200">
              <label htmlFor="owner-approval-note" className="block text-xs font-semibold text-gray-800">
                یادداشت تأیید مدیرعامل / مالک برای فعال‌سازی ارائه‌دهنده ابری (الزامی):
              </label>
              <input
                id="owner-approval-note"
                type="text"
                value={approvalNote}
                onChange={(e) => setApprovalNote(e.target.value)}
                placeholder="مثال: تأیید مدیرعامل — فقط برای داده‌های تجمیعی خرید"
                className="w-full px-3 py-2 text-xs border border-gray-300 rounded-lg text-gray-900 focus:outline-hidden focus:ring-2 focus:ring-purple-600"
                disabled={savingCloud}
              />
              {noteError && (
                <p className="text-xs text-rose-600 font-medium">{noteError}</p>
              )}
              <button
                type="button"
                onClick={handleEnableCloud}
                disabled={savingCloud}
                className="px-4 py-2 rounded-lg text-xs font-semibold text-white bg-purple-600 hover:bg-purple-700 transition cursor-pointer focus:outline-hidden focus:ring-2 focus:ring-purple-600"
              >
                {savingCloud ? 'در حال فعال‌سازی...' : 'فعال‌سازی ارائه‌دهنده ابری با ثبت تأییدیه'}
              </button>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
