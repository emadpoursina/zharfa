/**
 * S-NO-ACCESS (contracts/api.md §9): shown in place of a section the signed-in role may not use. It carries no data
 * and no link to an admin section. The server decides access; this only explains the refusal.
 */
export function NoAccess() {
  return (
    <section
      dir="rtl"
      role="alert"
      className="bg-white border border-amber-300 rounded-xl p-6 shadow-xs text-right"
    >
      <p className="text-base font-medium text-gray-900">شما به این بخش دسترسی ندارید.</p>
    </section>
  );
}
