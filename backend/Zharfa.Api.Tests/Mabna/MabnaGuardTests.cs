namespace Zharfa.Api.Tests.Mabna;

using Microsoft.Extensions.Logging.Abstractions;
using Zharfa.Api.Mabna;

// Placeholder object names only: real Mabna table names are not committed (spec FR-02).
public class MabnaObjectPolicyTests
{
    private static MabnaObjectPolicy Policy(params string[] allowed) => new(allowed);

    [Theory]
    [InlineData("scuUser")]
    [InlineData("SCUUSER")]
    [InlineData("dbo.scuUser")]
    [InlineData("UserRights")]
    [InlineData("userrights")]
    [InlineData("users")]
    [InlineData("Users")]
    [InlineData("scuAnything")]
    [InlineData("msgInbox")]
    [InlineData("MSGQueue")]
    public void Excluded_objects_are_refused_even_when_allowlisted(string name)
    {
        var policy = Policy(name, "dbo." + name.Split('.')[^1]);
        Assert.Equal(0, policy.AllowedCount);
        Assert.NotEmpty(policy.RejectedConfigEntries);
        Assert.Throws<MabnaAccessDeniedException>(() => policy.Authorize(name));
    }

    [Fact]
    public void Exclusion_is_case_insensitive_and_prefix_based()
    {
        Assert.True(MabnaObjectPolicy.IsExcluded("ScUfoo"));
        Assert.True(MabnaObjectPolicy.IsExcluded("msg"));
        Assert.False(MabnaObjectPolicy.IsExcluded("ExampleLedger"));
        Assert.False(MabnaObjectPolicy.IsExcluded("usersettings_view")); // only exact "users"
    }

    [Fact]
    public void Allowlisted_object_is_authorized_with_default_schema()
    {
        var policy = Policy("ExampleLedger", "rpt.ExampleView");
        Assert.Equal("[dbo].[ExampleLedger]", policy.Authorize("exampleledger").Quoted);
        Assert.Equal("[rpt].[ExampleView]", policy.Authorize("rpt.ExampleView").Quoted);
    }

    [Theory]
    [InlineData("OtherTable")]
    [InlineData("other.ExampleLedger")]
    public void Non_allowlisted_object_is_refused(string name)
    {
        Assert.Throws<MabnaAccessDeniedException>(() => Policy("ExampleLedger").Authorize(name));
    }

    [Fact]
    public void Empty_allowlist_denies_everything()
    {
        Assert.Throws<MabnaAccessDeniedException>(() => Policy().Authorize("ExampleLedger"));
    }

    [Theory]
    [InlineData("[ExampleLedger]")]
    [InlineData("Example Ledger")]
    [InlineData("ExampleLedger;DROP TABLE x")]
    [InlineData("db.dbo.ExampleLedger")]
    [InlineData("srv.db.dbo.ExampleLedger")]
    [InlineData("Exаmple")] // Cyrillic 'а'
    [InlineData("")]
    public void Malformed_names_are_refused(string name)
    {
        var policy = Policy("ExampleLedger");
        Assert.Throws<MabnaAccessDeniedException>(() => policy.Authorize(name));
    }
}

public class MabnaSqlBuilderTests
{
    private static readonly MabnaObjectName Ledger = new("dbo", "ExampleLedger");

    [Fact]
    public void Builds_single_parameterized_select_with_row_ceiling()
    {
        var cmd = MabnaSqlBuilder.Build(Ledger, new MabnaQuery(
            "ExampleLedger",
            new[] { "Id", "Amount" },
            new[] { new MabnaFilter("Amount", MabnaFilterOp.Ge, 10), new MabnaFilter("Note", MabnaFilterOp.IsNull) },
            new[] { new MabnaOrder("Id", Descending: true) },
            Top: 50), maxRows: 1000);

        Assert.Equal(
            "SELECT TOP (51) [Id], [Amount] FROM [dbo].[ExampleLedger] WHERE [Amount] >= @p0 AND [Note] IS NULL ORDER BY [Id] DESC",
            cmd.Sql);
        Assert.Single(cmd.Parameters);
        Assert.Equal(50, cmd.RowLimit);
    }

    [Fact]
    public void Top_is_clamped_to_configured_max_rows()
    {
        var cmd = MabnaSqlBuilder.Build(Ledger, new MabnaQuery("ExampleLedger", new[] { "Id" }, Top: 1_000_000), maxRows: 100);
        Assert.Equal(100, cmd.RowLimit);
        Assert.StartsWith("SELECT TOP (101) ", cmd.Sql);
    }

    [Fact]
    public void Filter_values_are_never_inlined()
    {
        var cmd = MabnaSqlBuilder.Build(Ledger, new MabnaQuery(
            "ExampleLedger", new[] { "Id" }, new[] { new MabnaFilter("Id", MabnaFilterOp.Eq, "1'; DROP TABLE x --") }), 10);
        Assert.DoesNotContain("DROP", cmd.Sql);
        Assert.Equal("1'; DROP TABLE x --", cmd.Parameters[0].Value);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("Id, Amount")]
    [InlineData("Id]; DELETE FROM x; --")]
    [InlineData("COUNT(*)")]
    public void Unsafe_column_names_are_refused(string column)
    {
        Assert.Throws<MabnaAccessDeniedException>(() =>
            MabnaSqlBuilder.Build(Ledger, new MabnaQuery("ExampleLedger", new[] { column }), 10));
    }

    [Fact]
    public void Columns_are_required()
    {
        Assert.Throws<MabnaAccessDeniedException>(() =>
            MabnaSqlBuilder.Build(Ledger, new MabnaQuery("ExampleLedger", Array.Empty<string>()), 10));
    }

    [Fact]
    public void Column_named_like_a_keyword_is_allowed_when_bracketed()
    {
        var cmd = MabnaSqlBuilder.Build(Ledger, new MabnaQuery("ExampleLedger", new[] { "Update", "Set" }), 10);
        Assert.Contains("[Update], [Set]", cmd.Sql);
    }
}

public class MabnaSqlGuardTests
{
    [Theory]
    [InlineData("SELECT TOP (11) [Id] FROM [dbo].[T]")]
    [InlineData("SELECT TOP (11) [Id] FROM [dbo].[T] WHERE [Id] = @p0 ORDER BY [Id] ASC")]
    public void Accepts_single_select(string sql) => Assert.True(MabnaSqlGuard.IsSingleSelect(sql, out _));

    [Theory]
    [InlineData("")]
    [InlineData("UPDATE [dbo].[T] SET [A] = 1")]
    [InlineData("DELETE FROM [dbo].[T]")]
    [InlineData("INSERT INTO [dbo].[T] VALUES (1)")]
    [InlineData("SELECT [Id] INTO [dbo].[Copy] FROM [dbo].[T]")]
    [InlineData("SELECT [Id] FROM [dbo].[T]; DROP TABLE [dbo].[T]")]
    [InlineData("SELECT [Id] FROM [dbo].[T] -- comment")]
    [InlineData("SELECT [Id] FROM [dbo].[T] /* x */")]
    [InlineData("SELECT [Id] FROM [dbo].[T] WHERE [A] = 'x'")]
    [InlineData("SELECT [Id] FROM [dbo].[T] UNION SELECT [Id] FROM [dbo].[scuUser]")]
    [InlineData("SELECT [Id] FROM (SELECT [Id] FROM [dbo].[T]) [x]")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT [Id] FROM OPENROWSET [x]")]
    [InlineData("WITH [c] AS (SELECT 1) SELECT 1")]
    [InlineData("SELECT [Id] FROM [dbo].[T]\nGO")]
    [InlineData("SELECT [Id] FROM [dbo].[T]] WAITFOR")]
    [InlineData("SELECT [Id] FROM \"dbo\".\"T\"")]
    public void Rejects_anything_else(string sql) => Assert.False(MabnaSqlGuard.IsSingleSelect(sql, out _));

    // T044: MERGE and DDL, the statements the read-only login must never let through either.
    [Theory]
    [InlineData("MERGE INTO [dbo].[T] AS t USING (SELECT 1 AS [Id]) AS s ON t.[Id] = s.[Id] WHEN MATCHED THEN DELETE;")]
    [InlineData("MERGE [dbo].[T] USING [dbo].[U] ON 1 = 1 WHEN MATCHED THEN UPDATE SET [A] = 1;")]
    [InlineData("TRUNCATE TABLE [dbo].[T]")]
    [InlineData("DROP TABLE [dbo].[T]")]
    [InlineData("ALTER TABLE [dbo].[T] ADD [X] int")]
    [InlineData("CREATE TABLE [dbo].[X] ([A] int)")]
    [InlineData("GRANT SELECT ON [dbo].[T] TO [zharfa_ro]")]
    [InlineData("EXECUTE [dbo].[SimWriter]")]
    [InlineData("BULK INSERT [dbo].[T] FROM 'x'")]
    public void Rejects_merge_truncate_and_ddl_statements(string sql) =>
        Assert.False(MabnaSqlGuard.IsSingleSelect(sql, out _));
}

/// <summary>
/// T044 (spec 003, US3): refusals happen in the connector before any connection is created. A
/// counting connection factory (no server, no network) records every attempt. The example
/// purchasing tables are read only when they are on the allowlist.
/// </summary>
public class MabnaConnectorRefusalTests
{
    private static readonly string ExampleAllowlistPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-allowlist.example.txt");

    private static MabnaSourceOptions SourceOptions(IEnumerable<string> allowed) => new()
    {
        ConnectionString = "Server=unused.invalid;Database=unused;Integrated Security=true",
        VerifyReadOnlyLogin = false,
        AllowedObjects = allowed.ToList(),
    };

    private static (MabnaReader Reader, CountingConnectionFactory Factory) ReaderWith(MabnaSourceOptions options)
    {
        var factory = new CountingConnectionFactory(options);
        var policy = new MabnaObjectPolicy(MabnaObjectPolicy.LoadEntries(options));
        var gate = new MabnaReadOnlyGate(
            factory,
            policy,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<MabnaReadOnlyGate>.Instance);
        var reader = new MabnaReader(
            factory,
            policy,
            gate,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<MabnaReader>.Instance);
        return (reader, factory);
    }

    [Theory]
    [InlineData("scuUser")]
    [InlineData("UserRights")]
    [InlineData("scuAccount")]
    [InlineData("msgInbox")]
    public async Task Excluded_object_is_refused_with_zero_connections_even_when_listed(string name)
    {
        var (reader, factory) = ReaderWith(SourceOptions(new[] { name }));

        await Assert.ThrowsAsync<MabnaAccessDeniedException>(() => reader.ReadAsync(new MabnaQuery(name, new[] { "Id" })));
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData("dbo.SimVendor")]
    [InlineData("dbo.SimProduct")]
    [InlineData("dbo.SimInvDoc")]
    [InlineData("dbo.SimRetDoc")]
    [InlineData("dbo.SimInvDocItem")]
    public async Task Purchasing_table_not_on_the_allowlist_is_refused_with_zero_connections(string name)
    {
        var (reader, factory) = ReaderWith(SourceOptions(new[] { "dbo.SimSomethingElse" }));

        await Assert.ThrowsAsync<MabnaAccessDeniedException>(() => reader.ReadAsync(new MabnaQuery(name, new[] { "Id" })));
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData("dbo.SimVendor; DROP TABLE x")]
    [InlineData("dbo.SimVendor]")]
    [InlineData("srv.db.dbo.SimVendor")]
    [InlineData("")]
    public async Task Malformed_object_name_is_refused_with_zero_connections(string name)
    {
        var (reader, factory) = ReaderWith(SourceOptions(new[] { "dbo.SimVendor" }));

        await Assert.ThrowsAsync<MabnaAccessDeniedException>(() => reader.ReadAsync(new MabnaQuery(name, new[] { "Id" })));
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData("dbo.SimVendor")]
    [InlineData("dbo.SimProduct")]
    [InlineData("dbo.SimInvDoc")]
    [InlineData("dbo.SimRetDoc")]
    [InlineData("dbo.SimInvDocItem")]
    public async Task Example_purchasing_table_on_the_allowlist_is_the_only_one_that_reaches_a_connection(string name)
    {
        // Control: an allowlisted object passes every app-side check and reaches the connection
        // attempt, which this test factory refuses. Proves the refusals above are the allowlist's doing.
        var (reader, factory) = ReaderWith(SourceOptions(new[] { name }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(new MabnaQuery(name, new[] { "Id" })));
        Assert.Equal(1, factory.Opens);
    }

    [Fact]
    public void Example_allowlist_file_holds_only_the_purchasing_examples_and_no_excluded_name()
    {
        var entries = MabnaObjectPolicy.LoadEntries(new MabnaSourceOptions { AllowlistFile = ExampleAllowlistPath }).ToList();

        Assert.Equal(5, entries.Count);
        Assert.All(entries, e => Assert.False(MabnaObjectPolicy.IsExcluded(e.Split('.')[^1])));
    }

    /// <summary>Counts connection attempts; the attempt itself fails without touching any network.</summary>
    private sealed class CountingConnectionFactory(MabnaSourceOptions options)
        : MabnaConnectionFactory(Microsoft.Extensions.Options.Options.Create(options))
    {
        public int Opens { get; private set; }

        public override Microsoft.Data.SqlClient.SqlConnection Create()
        {
            Opens++;
            throw new InvalidOperationException("unit test: the source is never contacted");
        }
    }
}
