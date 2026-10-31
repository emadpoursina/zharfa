namespace Zharfa.Api.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("product_category")]
public class ProductCategory
{
    [Key]
    [Column("category_id")]
    public long CategoryId { get; set; }

    [Column("name_fa")]
    public string NameFa { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}

[Table("product")]
public class Product
{
    [Key]
    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("category_id")]
    public long CategoryId { get; set; }

    [Column("name_fa")]
    public string NameFa { get; set; } = string.Empty;

    [Column("unit_fa")]
    public string UnitFa { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("has_batch")]
    public bool HasBatch { get; set; } = false;
}

[Table("customer")]
public class Customer
{
    [Key]
    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("name_fa")]
    public string NameFa { get; set; } = string.Empty;

    [Column("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}

[Table("warehouse")]
public class Warehouse
{
    [Key]
    [Column("warehouse_id")]
    public long WarehouseId { get; set; }

    [Column("name_fa")]
    public string NameFa { get; set; } = string.Empty;
}

[Table("sales_invoice")]
public class SalesInvoice
{
    [Key]
    [Column("invoice_id")]
    public long InvoiceId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("issued_at")]
    public DateTime IssuedAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = string.Empty;

    [Column("note_fa")]
    public string? NoteFa { get; set; }
}

[Table("sales_invoice_item")]
public class SalesInvoiceItem
{
    [Key]
    [Column("item_id")]
    public long ItemId { get; set; }

    [Column("invoice_id")]
    public long InvoiceId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("qty")]
    public decimal Qty { get; set; }

    [Column("unit_price")]
    public decimal UnitPrice { get; set; }

    [Column("line_discount")]
    public decimal LineDiscount { get; set; }
}

[Table("stock_movement")]
public class StockMovement
{
    [Key]
    [Column("movement_id")]
    public long MovementId { get; set; }

    [Column("product_id")]
    public long ProductId { get; set; }

    [Column("warehouse_id")]
    public long WarehouseId { get; set; }

    [Column("moved_at")]
    public DateTime MovedAt { get; set; }

    [Column("qty_delta")]
    public decimal QtyDelta { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("batch_flag")]
    public bool BatchFlag { get; set; } = false;
}

[Table("receipt")]
public class Receipt
{
    [Key]
    [Column("receipt_id")]
    public long ReceiptId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }
}

[Table("app_user")]
public class AppUser
{
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("role")]
    public string Role { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}

[Table("audit_log")]
public class AuditLog
{
    [Key]
    [Column("audit_id")]
    public long AuditId { get; set; }

    [Column("at")]
    public DateTime At { get; set; } = DateTime.UtcNow;

    [Column("actor")]
    public string? Actor { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("detail")]
    public string? Detail { get; set; }
}

/// <summary>
/// EF Core DbContext for Zharfa canonical tables.
/// Schema is owned and managed exclusively by forward-only SQL migrations (MigrationRunner).
/// EF Core never calls Migrate() or EnsureCreated().
/// </summary>
public class ZharfaDbContext : DbContext
{
    public ZharfaDbContext(DbContextOptions<ZharfaDbContext> options) : base(options)
    {
    }

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceItem> SalesInvoiceItems => Set<SalesInvoiceItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
}
