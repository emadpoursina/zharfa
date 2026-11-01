-- DEMONSTRATION DATA ONLY — synthetic, not real company data. / -- داده‌های نمایشی (دمو)؛ داده واقعی نیست.
-- File 2: Sales invoices, invoice items, cash receipts, and authenticated demo users.
-- Forward-only deterministic seed data for Zharfa Demo 1. Relative to now() to prevent demo staleness.

-- ============================================================================
-- 1. Sales Invoices (96 posted spread over ~95 days + 3 void + 2 draft = 101 invoices)
-- ============================================================================

-- 96 Posted Invoices (relative to now() covering current month, previous month, and month before)
INSERT INTO sales_invoice (invoice_id, customer_id, issued_at, status, note_fa)
SELECT
    s,
    CASE
        WHEN s IN (1, 25, 50, 75) THEN 1  -- Customer 1: zero-opening-balance new debtor
        WHEN s IN (2, 26, 51, 76) THEN 2  -- Customer 2: debtor with no receipts
        WHEN s IN (3, 27) THEN 3          -- Customer 3: receipts will exceed invoices (negative balance)
        ELSE ((s % 22) + 4)               -- Customers 4..25
    END,
    now() - ((96 - s) * INTERVAL '23 hours'),
    'posted',
    'فاکتور فروش رسمی دوره آزمایشی'
FROM generate_series(1, 96) AS s;

-- 3 Void Invoices (excluded from sales KPIs)
INSERT INTO sales_invoice (invoice_id, customer_id, issued_at, status, note_fa) VALUES
(97, 4, now() - INTERVAL '12 days', 'void', 'فاکتور ابطال‌شده به درخواست خریدار'),
(98, 7, now() - INTERVAL '42 days', 'void', 'فاکتور باطل‌شده به دلیل خطای سیستمی'),
(99, 14, now() - INTERVAL '72 days', 'void', 'فاکتور ابطال‌شده پیش از بارگیری');

-- 2 Draft Invoices (excluded from sales KPIs)
INSERT INTO sales_invoice (invoice_id, customer_id, issued_at, status, note_fa) VALUES
(100, 5, now() - INTERVAL '3 days', 'draft', 'پیش‌نویس فاکتور در انتظار تأیید'),
(101, 10, now() - INTERVAL '28 days', 'draft', 'پیش‌نویس فاکتور در انتظار صدور');

-- ============================================================================
-- 2. Sales Invoice Items (≥ 260 lines; 3 items per posted invoice = 288 + 10 = 298 lines)
--    Includes 3 explicit negative-qty return lines and many discounted lines.
-- ============================================================================

-- Lines for posted invoices 1..96 (Line 1 of 3)
INSERT INTO sales_invoice_item (invoice_id, product_id, qty, unit_price, line_discount)
SELECT
    s,
    ((s * 2 + 1) % 36) + 1,
    (4 + (s % 6))::numeric(18,3),
    (120000.00 + ((s % 10) * 15000.00))::numeric(18,2),
    CASE WHEN (s % 3) = 0 THEN 25000.00 ELSE 0.00 END
FROM generate_series(1, 96) AS s;

-- Lines for posted invoices 1..96 (Line 2 of 3)
INSERT INTO sales_invoice_item (invoice_id, product_id, qty, unit_price, line_discount)
SELECT
    s,
    ((s * 3 + 2) % 36) + 1,
    (3 + (s % 5))::numeric(18,3),
    (240000.00 + ((s % 8) * 20000.00))::numeric(18,2),
    CASE WHEN (s % 4) = 0 THEN 40000.00 ELSE 0.00 END
FROM generate_series(1, 96) AS s;

-- Lines for posted invoices 1..96 (Line 3 of 3) — With 3 explicit negative-qty return lines
INSERT INTO sales_invoice_item (invoice_id, product_id, qty, unit_price, line_discount)
SELECT
    s,
    ((s * 5 + 3) % 36) + 1,
    CASE
        WHEN s = 12 THEN -3.000   -- Explicit return line 1
        WHEN s = 42 THEN -5.000   -- Explicit return line 2
        WHEN s = 72 THEN -2.000   -- Explicit return line 3
        ELSE (2 + (s % 4))::numeric(18,3)
    END,
    (350000.00 + ((s % 7) * 30000.00))::numeric(18,2),
    CASE
        WHEN s IN (12, 42, 72) THEN 0.00
        WHEN (s % 5) = 0 THEN 50000.00
        ELSE 0.00
    END
FROM generate_series(1, 96) AS s;

-- Lines for void/draft invoices (excluded from approved views because headers are void/draft)
INSERT INTO sales_invoice_item (invoice_id, product_id, qty, unit_price, line_discount) VALUES
(97, 1, 5.000, 150000.00, 0.00),
(97, 3, 2.000, 200000.00, 10000.00),
(98, 9, 10.000, 80000.00, 0.00),
(98, 11, 4.000, 320000.00, 0.00),
(99, 17, 6.000, 180000.00, 20000.00),
(99, 19, 3.000, 210000.00, 0.00),
(100, 25, 8.000, 95000.00, 0.00),
(100, 27, 4.000, 140000.00, 5000.00),
(101, 33, 5.000, 260000.00, 0.00),
(101, 35, 2.000, 110000.00, 0.00);

-- ============================================================================
-- 3. Receipts (56 receipts spread over ~90 days, multiple in current month)
-- ============================================================================
-- Customer 2: zero receipts (at least one customer with no receipts).
-- Customer 3: receipts exceed posted invoice net (negative balance).
-- Customer 1: zero-opening-balance new debtor (balance > 0).

INSERT INTO receipt (receipt_id, customer_id, received_at, amount)
SELECT
    s,
    CASE
        WHEN s = 1 THEN 1           -- Customer 1 receipt (partial payment, remains in debt)
        WHEN s IN (10, 30) THEN 3   -- Customer 3 receipts (total 38,000,000, exceeds invoices -> negative balance)
        ELSE ((s % 22) + 4)         -- Customers 4..25
    END,
    now() - ((56 - s) * INTERVAL '38 hours'),
    CASE
        WHEN s = 1 THEN 10000000.00
        WHEN s = 10 THEN 18000000.00
        WHEN s = 30 THEN 20000000.00
        ELSE (12000000.00 + ((s % 8) * 4500000.00))
    END
FROM generate_series(1, 56) AS s;

-- ============================================================================
-- 4. App Users (seeded manager and admin demo accounts)
-- ============================================================================
-- Final PBKDF2-HMAC-SHA256 hash strings (100000 iterations, base64 salt and hash).
-- manager: manager123
-- admin: admin123
INSERT INTO app_user (user_id, username, password_hash, role, is_active) VALUES
(1, 'manager', 'pbkdf2-sha256$100000$WmhhcmZhTWFuYWdlclNhbHQ=$qDo2DRXwIxpj1TkwTRfWGNdvjjOPvpb9EyqPAEG7xgM=', 'manager', true),
(2, 'admin', 'pbkdf2-sha256$100000$WmhhcmZhQWRtaW5TYWx0MDE=$K7MXh4JtGn+NlEHffa5n3nnjvKTtOgPRlpHn0fcHyUk=', 'admin', true);
