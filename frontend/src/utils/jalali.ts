/**
 * In-house Jalali (Persian / Solar Hijri) date arithmetic implementation.
 * Zero external dependencies. Mirrors backend/Zharfa.Api/Analytics/JalaliDate.cs algorithm.
 */

const breaks = [
  -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181,
  1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178,
];

export const JALALI_MONTH_NAMES = [
  'فروردین', 'اردیبهشت', 'خرداد',
  'تیر', 'مرداد', 'شهریور',
  'مهر', 'آبان', 'آذر',
  'دی', 'بهمن', 'اسفند',
];

const div = (a: number, b: number): number => Math.trunc(a / b);

export interface JalaliDateParts {
  jy: number;
  jm: number;
  jd: number;
}

export function toPersianDigits(input: string | number): string {
  const str = String(input);
  const persianDigits = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];
  return str.replace(/[0-9]/g, (w) => persianDigits[+w]);
}

export function toLatinDigits(input: string): string {
  const persianToLatin: Record<string, string> = {
    '۰': '0', '۱': '1', '۲': '2', '۳': '3', '۴': '4',
    '۵': '5', '۶': '6', '۷': '7', '۸': '8', '۹': '9',
    '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4',
    '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9',
  };
  return input.replace(/[۰-۹٠-٩]/g, (w) => persianToLatin[w] || w);
}

function jalCal(jy: number): { leap: number; gy: number; march: number } {
  const bl = breaks.length;
  const gy = jy + 621;
  let leapJ = -14;
  let jp = breaks[0];
  let jm = 0;
  let jump = 0;
  let leap = 0;
  let leapG = 0;
  let march = 0;
  let n = 0;

  if (jy < jp || jy >= breaks[bl - 1]) {
    throw new Error(`سال جلالی نامعتبر است: ${jy}`);
  }

  for (let i = 1; i < bl; i++) {
    jm = breaks[i];
    jump = jm - jp;
    if (jy < jm) break;
    leapJ = leapJ + div(jump, 33) * 8 + div(jump % 33, 4);
    jp = jm;
  }

  n = jy - jp;
  leapJ = leapJ + div(n, 33) * 8 + div((n % 33) + 3, 4);

  if ((jump % 33) === 4 && jump - n === 4) {
    leapJ += 1;
  }

  leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
  march = 20 + leapJ - leapG;

  if (jump - n < 6) {
    n = n - jump + div(jump + 4, 33) * 33;
  }

  leap = ((n + 1) % 33 - 1) % 4;
  if (leap === -1) {
    leap = 4;
  }

  return { leap, gy, march };
}

export function isLeapJalaliYear(jy: number): boolean {
  const { leap } = jalCal(jy);
  return leap === 0;
}

export function daysInJalaliMonth(jy: number, jm: number): number {
  if (jm < 1 || jm > 12) {
    throw new Error('ماه جلالی باید بین ۱ تا ۱۲ باشد.');
  }
  if (jm <= 6) return 31;
  if (jm <= 11) return 30;
  return isLeapJalaliYear(jy) ? 30 : 29;
}

function gregorianToJdn(gy: number, gm: number, gd: number): number {
  let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) +
          div(153 * ((gm + 9) % 12) + 2, 5) + gd - 34840408;
  d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
  return d;
}

function jdnToGregorian(jdn: number): { gy: number; gm: number; gd: number } {
  let j = 4 * jdn + 139361631;
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
  const i = div(j % 1461, 4) * 5 + 308;
  const gd = div(i % 153, 5) + 1;
  const gm = (div(i, 153) % 12) + 1;
  const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
  return { gy, gm, gd };
}

function jalaliToJdn(jy: number, jm: number, jd: number): number {
  const { gy, march } = jalCal(jy);
  return gregorianToJdn(gy, 3, march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
}

function jdnToJalali(jdn: number): JalaliDateParts {
  const { gy } = jdnToGregorian(jdn);
  let jy = gy - 621;
  const { march } = jalCal(jy);
  let jdn1f = gregorianToJdn(gy, 3, march);
  let k = jdn - jdn1f;
  if (k >= 0) {
    if (k <= 185) {
      return { jy, jm: 1 + div(k, 31), jd: (k % 31) + 1 };
    } else {
      k -= 186;
      return { jy, jm: 7 + div(k, 30), jd: (k % 30) + 1 };
    }
  } else {
    jy -= 1;
    const { march: marchPrev } = jalCal(jy);
    jdn1f = gregorianToJdn(jy + 621, 3, marchPrev);
    k = jdn - jdn1f;
    if (k <= 185) {
      return { jy, jm: 1 + div(k, 31), jd: (k % 31) + 1 };
    } else {
      k -= 186;
      return { jy, jm: 7 + div(k, 30), jd: (k % 30) + 1 };
    }
  }
}

/**
 * Converts a Gregorian ISO date ("YYYY-MM-DD") to Jalali { jy, jm, jd }.
 */
export function toJalali(dateIso: string): JalaliDateParts {
  const parts = dateIso.split('T')[0].split('-').map(Number);
  if (parts.length < 3 || isNaN(parts[0]) || isNaN(parts[1]) || isNaN(parts[2])) {
    throw new Error(`تاریخ میلادی نامعتبر است: ${dateIso}`);
  }
  const jdn = gregorianToJdn(parts[0], parts[1], parts[2]);
  return jdnToJalali(jdn);
}

/**
 * Converts a Jalali date to Gregorian ISO "YYYY-MM-DD".
 */
export function toGregorian(jy: number, jm: number, jd: number): string {
  const jdn = jalaliToJdn(jy, jm, jd);
  const { gy, gm, gd } = jdnToGregorian(jdn);
  return `${gy}-${String(gm).padStart(2, '0')}-${String(gd).padStart(2, '0')}`;
}

/**
 * Formats a Gregorian ISO date as «۹ آبان ۱۴۰۵».
 */
export function formatJalaliDate(iso: string): string {
  try {
    const { jy, jm, jd } = toJalali(iso);
    const monthName = JALALI_MONTH_NAMES[jm - 1] ?? '';
    return `${toPersianDigits(jd)} ${monthName} ${toPersianDigits(jy)}`;
  } catch {
    return iso;
  }
}

/**
 * Formats a Gregorian date range into a user-friendly Persian label.
 */
export function formatJalaliRange(fromIso: string, toIso: string): string {
  try {
    const jFrom = toJalali(fromIso);
    // toIso is half-open (exclusive), calculate inclusive end
    const toDate = new Date(toIso.split('T')[0]);
    toDate.setUTCDate(toDate.getUTCDate() - 1);
    const inclusiveIso = toDate.toISOString().slice(0, 10);
    const jEnd = toJalali(inclusiveIso);

    if (jFrom.jd === 1) {
      const days = daysInJalaliMonth(jFrom.jy, jFrom.jm);
      if (jEnd.jy === jFrom.jy && jEnd.jm === jFrom.jm && jEnd.jd === days) {
        return `${JALALI_MONTH_NAMES[jFrom.jm - 1]} ${toPersianDigits(jFrom.jy)}`;
      }
    }

    const startStr = `${toPersianDigits(jFrom.jy)}/${toPersianDigits(String(jFrom.jm).padStart(2, '0'))}/${toPersianDigits(String(jFrom.jd).padStart(2, '0'))}`;
    const endStr = `${toPersianDigits(jEnd.jy)}/${toPersianDigits(String(jEnd.jm).padStart(2, '0'))}/${toPersianDigits(String(jEnd.jd).padStart(2, '0'))}`;
    return `${startStr} تا ${endStr}`;
  } catch {
    return `${fromIso} تا ${toIso}`;
  }
}

/**
 * Parses user input in formats like «۱۴۰۵/۰۸/۰۹» or «1405/08/09» (tolerates Persian and Latin digits).
 * Returns JalaliDateParts or null if invalid.
 */
export function parseJalaliInput(input: string): JalaliDateParts | null {
  if (!input) return null;
  const normalized = toLatinDigits(input.trim());
  const match = normalized.match(/^(\d{4})[/-](\d{1,2})[/-](\d{1,2})$/);
  if (!match) return null;

  const jy = parseInt(match[1], 10);
  const jm = parseInt(match[2], 10);
  const jd = parseInt(match[3], 10);

  if (jy < 1200 || jy > 1600) return null;
  if (jm < 1 || jm > 12) return null;
  if (jd < 1 || jd > daysInJalaliMonth(jy, jm)) return null;

  return { jy, jm, jd };
}

/**
 * Returns half-open Gregorian [from, to) for the current Jalali month-to-date:
 * [first day of current Jalali month, tomorrow).
 */
export function currentJalaliMonthToDate(): { from: string; to: string } {
  const now = new Date();
  const todayIso = now.toISOString().slice(0, 10);
  const jToday = toJalali(todayIso);

  const from = toGregorian(jToday.jy, jToday.jm, 1);
  const tomorrow = new Date(now.getTime() + 86400000).toISOString().slice(0, 10);

  return { from, to: tomorrow };
}

/**
 * Computes exact whole Jalali month range [month start, month end + 1 day)
 * offset by the specified number of months (0 = current month, 1 = previous month, 2 = 2 months ago).
 */
export function getJalaliMonthRange(offsetMonths: number): { from: string; to: string } {
  const now = new Date();
  const todayIso = now.toISOString().slice(0, 10);
  const jToday = toJalali(todayIso);

  let targetYear = jToday.jy;
  let targetMonth = jToday.jm - offsetMonths;

  while (targetMonth < 1) {
    targetMonth += 12;
    targetYear -= 1;
  }

  const from = toGregorian(targetYear, targetMonth, 1);

  // Next month start day is end + 1 day
  let nextYear = targetYear;
  let nextMonth = targetMonth + 1;
  if (nextMonth > 12) {
    nextMonth = 1;
    nextYear += 1;
  }
  const to = toGregorian(nextYear, nextMonth, 1);

  return { from, to };
}
