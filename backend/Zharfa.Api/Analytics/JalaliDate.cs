namespace Zharfa.Api.Analytics;

using System;
using System.Globalization;

/// <summary>
/// In-house Jalali (Persian / Solar Hijri) date arithmetic implementation.
/// Uses the Borkowski arithmetic algorithm across Julian Day Numbers (JDN).
/// Zero culture dependencies; runs safely under DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1.
/// </summary>
public readonly record struct JalaliDate(int Year, int Month, int Day)
{
    private static readonly int[] Breaks = [
        -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181,
        1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178
    ];

    private static readonly string[] MonthNames = [
        "فروردین", "اردیبهشت", "خرداد",
        "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر",
        "دی", "بهمن", "اسفند"
    ];

    public string MonthName => Month is >= 1 and <= 12 ? MonthNames[Month - 1] : string.Empty;

    public static string GetMonthName(int month) =>
        month is >= 1 and <= 12 ? MonthNames[month - 1] : string.Empty;

    public static bool IsLeapYear(int jy)
    {
        var (leap, _, _) = JalCal(jy);
        return leap == 0;
    }

    public static int DaysInMonth(int jy, int jm)
    {
        if (jm is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(jm), "ماه جلالی باید بین ۱ تا ۱۲ باشد.");
        }

        if (jm <= 6) return 31;
        if (jm <= 11) return 30;
        return IsLeapYear(jy) ? 30 : 29;
    }

    public static (int leap, int gy, int march) JalCal(int jy)
    {
        int bl = Breaks.Length;
        int gy = jy + 621;
        int leapJ = -14;
        int jp = Breaks[0];
        int jm = 0;
        int jump = 0;
        int leap = 0;
        int leapG = 0;
        int march = 0;
        int n = 0;

        if (jy < jp || jy >= Breaks[bl - 1])
        {
            throw new ArgumentOutOfRangeException(nameof(jy), $"سال جلالی نامعتبر است: {jy}");
        }

        for (int i = 1; i < bl; i++)
        {
            jm = Breaks[i];
            jump = jm - jp;
            if (jy < jm) break;
            leapJ = leapJ + (jump / 33) * 8 + (jump % 33) / 4;
            jp = jm;
        }

        n = jy - jp;
        leapJ = leapJ + (n / 33) * 8 + ((n % 33) + 3) / 4;

        if ((jump % 33) == 4 && jump - n == 4)
        {
            leapJ += 1;
        }

        leapG = (gy / 4) - (((gy / 100) + 1) * 3 / 4) - 150;
        march = 20 + leapJ - leapG;

        if (jump - n < 6)
        {
            n = n - jump + ((jump + 4) / 33) * 33;
        }

        leap = ((n + 1) % 33 - 1) % 4;
        if (leap == -1)
        {
            leap = 4;
        }

        return (leap, gy, march);
    }

    public static int JalaliToJdn(int jy, int jm, int jd)
    {
        var (_, gy, march) = JalCal(jy);
        return GregorianToJdn(gy, 3, march) + (jm - 1) * 31 - (jm / 7) * (jm - 7) + jd - 1;
    }

    public static (int jy, int jm, int jd) JdnToJalali(int jdn)
    {
        var (gy, _, _) = JdnToGregorian(jdn);
        int jy = gy - 621;
        var (_, _, march) = JalCal(jy);
        int jdn1f = GregorianToJdn(gy, 3, march);
        int k = jdn - jdn1f;

        if (k >= 0)
        {
            if (k <= 185)
            {
                int jm = 1 + k / 31;
                int jd = (k % 31) + 1;
                return (jy, jm, jd);
            }
            else
            {
                k -= 186;
                int jm = 7 + k / 30;
                int jd = (k % 30) + 1;
                return (jy, jm, jd);
            }
        }
        else
        {
            jy -= 1;
            var (_, _, marchPrev) = JalCal(jy);
            jdn1f = GregorianToJdn(jy + 621, 3, marchPrev);
            k = jdn - jdn1f;
            if (k <= 185)
            {
                int jm = 1 + k / 31;
                int jd = (k % 31) + 1;
                return (jy, jm, jd);
            }
            else
            {
                k -= 186;
                int jm = 7 + k / 30;
                int jd = (k % 30) + 1;
                return (jy, jm, jd);
            }
        }
    }

    public static int GregorianToJdn(int gy, int gm, int gd)
    {
        int d = ((gy + (gm - 8) / 6 + 100100) * 1461) / 4 +
                (153 * ((gm + 9) % 12) + 2) / 5 +
                gd - 34840408;
        d = d - (((gy + 100100 + (gm - 8) / 6) / 100) * 3) / 4 + 752;
        return d;
    }

    public static (int gy, int gm, int gd) JdnToGregorian(int jdn)
    {
        int j = 4 * jdn + 139361631;
        j = j + (((4 * jdn + 183187720) / 146097) * 3 / 4) * 4 - 3908;
        int i = ((j % 1461) / 4) * 5 + 308;
        int gd = ((i % 153) / 5) + 1;
        int gm = ((i / 153) % 12) + 1;
        int gy = (j / 1461) - 100100 + (8 - gm) / 6;
        return (gy, gm, gd);
    }

    public static JalaliDate FromDate(DateOnly date)
    {
        int jdn = GregorianToJdn(date.Year, date.Month, date.Day);
        var (jy, jm, jd) = JdnToJalali(jdn);
        return new JalaliDate(jy, jm, jd);
    }

    public static JalaliDate FromDateTime(DateTime dateTime) =>
        FromDate(DateOnly.FromDateTime(dateTime));

    public DateOnly ToDateOnly()
    {
        int jdn = JalaliToJdn(Year, Month, Day);
        var (gy, gm, gd) = JdnToGregorian(jdn);
        return new DateOnly(gy, gm, gd);
    }

    public DateTime ToDateTime(TimeOnly timeOnly = default)
    {
        var d = ToDateOnly();
        return new DateTime(d.Year, d.Month, d.Day, timeOnly.Hour, timeOnly.Minute, timeOnly.Second, DateTimeKind.Utc);
    }

    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '0' && chars[i] <= '9')
            {
                chars[i] = (char)('۰' + (chars[i] - '0'));
            }
        }
        return new string(chars);
    }

    public string ToPersianDigitsString() =>
        ToPersianDigits(ToString());

    public override string ToString() =>
        $"{Year.ToString("D4", CultureInfo.InvariantCulture)}/{Month.ToString("D2", CultureInfo.InvariantCulture)}/{Day.ToString("D2", CultureInfo.InvariantCulture)}";
}
