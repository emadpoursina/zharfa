namespace Zharfa.Api.SavedReports;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Saved report entity (data-model §1).
/// Schema is owned and managed exclusively by forward-only SQL migrations (0010_ai_reports_export.sql).
/// Stored rows contain only the spec (kind, period, filters, generated metrics), never calculated result rows.
/// </summary>
[Table("saved_report")]
public class SavedReport
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("name_key")]
    public string NameKey { get; set; } = string.Empty;

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("required_permission")]
    public string RequiredPermission { get; set; } = string.Empty;

    [Column("period_json", TypeName = "jsonb")]
    public string PeriodJson { get; set; } = "{}";

    [Column("filters_json", TypeName = "jsonb")]
    public string FiltersJson { get; set; } = "{}";

    [Column("generated_json", TypeName = "jsonb")]
    public string? GeneratedJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
