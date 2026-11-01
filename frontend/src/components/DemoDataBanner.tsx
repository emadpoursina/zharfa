/**
 * Persistent Persian banner indicating demonstration data.
 * Rendered across all views in Demo 1.
 */
export function DemoDataBanner() {
  return (
    <aside
      aria-label="اطلاعیه داده نمایشی"
      className="w-full bg-amber-100 border-b border-amber-300 text-amber-950 text-xs sm:text-sm py-2 px-4 text-center font-medium shadow-xs"
    >
      داده‌های این نسخه صرفاً داده نمایشی (دمو) است؛ داده واقعی شرکت استفاده نمی‌شود.
    </aside>
  );
}
