namespace Zharfa.Api.Mabna;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

/// <summary>
/// Builds SQL Server connections for the Mabna source from configuration.
/// The connection string is read here and nowhere else; it is never logged
/// and never echoed in exception messages produced by this code.
/// <c>Create</c> is virtual only so that unit tests can count connection attempts without a server.
/// </summary>
public class MabnaConnectionFactory(IOptions<MabnaSourceOptions> options)
{
    private readonly MabnaSourceOptions _options = options.Value;

    public bool IsEnabled => _options.IsEnabled;

    public virtual SqlConnection Create()
    {
        if (!_options.IsEnabled)
        {
            throw new MabnaSourceUnavailableException("Mabna source is not configured.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(_options.ConnectionString);
        }
        catch (ArgumentException)
        {
            // Do not include the original message: it can quote the bad fragment.
            throw new MabnaSourceUnavailableException("Mabna connection string is malformed.");
        }

        // ApplicationIntent=ReadOnly is set for intent/AG routing only. It does
        // NOT make the session read-only (Microsoft docs; scratch research
        // finding 11). Read-only is enforced by the server login (primary
        // control) and by this gateway (secondary control).
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        builder.ApplicationName = "Zharfa-MabnaReader";
        builder.PersistSecurityInfo = false;
        builder.MultipleActiveResultSets = false;
        builder.ConnectTimeout = Math.Clamp(_options.ConnectTimeoutSeconds, 1, 120);

        // Encrypt / TrustServerCertificate are deployment-specific and left to
        // the configured connection string (SqlClient 7 defaults to
        // Encrypt=Mandatory; an on-prem SQL 2019 with a self-signed cert needs
        // TrustServerCertificate=True or a trusted cert).
        return new SqlConnection(builder.ConnectionString);
    }
}
