namespace Zharfa.Api.Purchasing.Reports;

using System.Collections.Generic;
using System.Linq;

/// <summary>Result of shaping a print query: at most <see cref="Limit"/> rows and the truncation flag.</summary>
public sealed record PrintShape<T>(IReadOnlyList<T> Rows, long TotalRows, int Limit, bool Truncated);

/// <summary>
/// Print limit (contracts/api.md "Print twin", research R9): a print request ignores paging, the query
/// fetches <see cref="FetchLimit"/> rows (one more than the limit) and the response keeps at most
/// <see cref="Limit"/>. Pure: the SQL layer supplies the fetched rows and the full total.
/// </summary>
public static class PrintLimit
{
    public const int Limit = 1000;

    /// <summary>Rows the print query asks for (<c>LIMIT 1001</c>), so truncation is known without a second query.</summary>
    public const int FetchLimit = Limit + 1;

    public static PrintShape<T> Shape<T>(IReadOnlyList<T> fetched, long totalRows)
    {
        var rows = fetched.Take(Limit).ToList();
        return new PrintShape<T>(rows, totalRows, Limit, totalRows > Limit);
    }
}
