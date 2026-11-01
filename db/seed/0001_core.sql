-- DEMONSTRATION DATA ONLY — synthetic, not real company data. / -- داده‌های نمایشی (دمو)؛ داده واقعی نیست.
-- File 1: Core reference catalog, warehouse, customers, and inventory movements.
-- Forward-only deterministic seed data for Zharfa Demo 1.

-- 1. Product Categories (5 categories with Persian names)
INSERT INTO product_category (category_id, name_fa, is_active) VALUES
(1, 'آنتی‌بیوتیک‌ها', true),
(2, 'مسکن‌ها و ضدالتهاب', true),
(3, 'داروهای قلب و عروق', true),
(4, 'مکمل‌ها و ویتامین‌ها', true),
(5, 'داروهای گوارشی', true);

-- 2. Products (40 products across 5 categories; unit_fa بسته/عدد; has_batch true on some)
INSERT INTO product (product_id, category_id, name_fa, unit_fa, is_active, has_batch) VALUES
-- Category 1: آنتی‌بیوتیک‌ها
(1, 1, 'کپسول آموکسی‌سیلین ۵۰۰ میلی‌گرم', 'بسته', true, true),
(2, 1, 'کپسول سفالکسین ۵۰۰ میلی‌گرم', 'بسته', true, true),
(3, 1, 'قرص سیپروفلوکساسین ۵۰۰ میلی‌گرم', 'بسته', true, false),
(4, 1, 'کپسول آزیترومایسین ۲۵۰ میلی‌گرم', 'بسته', true, true),
(5, 1, 'قرص کوآموکسی‌کلاو ۶۲۵ میلی‌گرم', 'بسته', true, true),
(6, 1, 'آمپول جنتامایسین ۸۰ میلی‌گرم', 'عدد', true, false),
(7, 1, 'شربت آموکسی‌سیلین ۱۲۵ میلی‌گرم', 'عدد', true, false),
(8, 1, 'کپسول کلیندامایسین ۳۰۰ میلی‌گرم', 'بسته', true, true),
-- Category 2: مسکن‌ها و ضدالتهاب
(9, 2, 'قرص استامینوفن کدوئین', 'بسته', true, false),
(10, 2, 'قرص ایبوپروفن ۴۰۰ میلی‌گرم', 'بسته', true, false),
(11, 2, 'کپسول ژلوفن ۴۰۰ میلی‌گرم', 'بسته', true, true),
(12, 2, 'قرص ناپروکسن ۵۰۰ میلی‌گرم', 'بسته', true, true),
(13, 2, 'قرص دیکلوفناک سدیم ۵۰ میلی‌گرم', 'بسته', true, false),
(14, 2, 'آمپول پیروکسیکام', 'عدد', true, false),
(15, 2, 'پماد پیروکسیکام ۳۰ گرمی', 'عدد', true, false),
(16, 2, 'قطره مسکن دندان', 'عدد', true, false),
-- Category 3: داروهای قلب و عروق
(17, 3, 'قرص آتورواستاتین ۲۰ میلی‌گرم', 'بسته', true, true),
(18, 3, 'قرص لوزارتان ۲۵ میلی‌گرم', 'بسته', true, false),
(19, 3, 'قرص آملودیپین ۵ میلی‌گرم', 'بسته', true, false),
(20, 3, 'قرص متوپرولول ۵۰ میلی‌گرم', 'بسته', true, true),
(21, 3, 'قرص کاپتوپریل ۲۵ میلی‌گرم', 'بسته', true, false),
(22, 3, 'قرص آسپیرین ۸۱ میلی‌گرم', 'بسته', true, false),
(23, 3, 'قرص نیتروگلیسیرین زیرزبانی', 'بسته', true, true),
(24, 3, 'قرص انالاپریل ۲۰ میلی‌گرم', 'بسته', true, false),
-- Category 4: مکمل‌ها و ویتامین‌ها
(25, 4, 'آمپول نوروبیون', 'عدد', true, true),
(26, 4, 'کپسول ویتامین دی ۵۰۰۰۰', 'بسته', true, false),
(27, 4, 'قرص زینک پلاس ۱۰ میلی‌گرم', 'بسته', true, true),
(28, 4, 'قرص جوشان ویتامین سی ۱۰۰۰', 'بسته', true, false),
(29, 4, 'کپسول امگا ۳', 'بسته', true, true),
(30, 4, 'قرص کلسیم د۳', 'بسته', true, false),
(31, 4, 'قرص آهن ففول', 'بسته', true, false),
(32, 4, 'کپسول مولتی ویتامین مینرال', 'بسته', true, true),
-- Category 5: داروهای گوارشی
(33, 5, 'کپسول امپرازول ۲۰ میلی‌گرم', 'بسته', true, false),
(34, 5, 'قرص فاموتیدین ۴۰ میلی‌گرم', 'بسته', true, false),
(35, 5, 'قرص دایمتیکون فعال', 'بسته', true, false),
(36, 5, 'قرص متوکلوپرامید ۱۰ میلی‌گرم', 'بسته', true, true),
(37, 5, 'شربت آلومینیوم ام‌جی‌اس', 'عدد', true, false),
(38, 5, 'کپسول پنتوپرازول ۴۰ میلی‌گرم', 'بسته', true, true),
(39, 5, 'کپسول اسومپرازول ۲۰ میلی‌گرم', 'بسته', true, true),
(40, 5, 'قرص لوپرامید ۲ میلی‌گرم', 'بسته', true, false);

-- 3. Warehouse (1 central warehouse)
INSERT INTO warehouse (warehouse_id, name_fa) VALUES
(1, 'انبار مرکزی');

-- 4. Customers (25 customers with synthetic Persian names and varied opening_balance)
INSERT INTO customer (customer_id, name_fa, opening_balance, is_active) VALUES
(1, 'داروخانه دکتر کاظمی', 0.00, true),
(2, 'داروخانه شبانه‌روزی رازی', 250000000.00, true),
(3, 'درمانگاه خیریه امام رضا', 0.00, true),
(4, 'بیمارستان شهید بهشتی', 485000000.00, true),
(5, 'داروخانه مرکزی بوعلی', 320000000.00, true),
(6, 'داروخانه دکتر افشار', 145000000.00, true),
(7, 'داروخانه شفابخش', 85000000.00, true),
(8, 'بیمارستان میلاد', 420000000.00, true),
(9, 'داروخانه دکتر طاهری', 55000000.00, true),
(10, 'داروخانه سلامت', 110000000.00, true),
(11, 'داروخانه دکتر صدر', 18000000.00, true),
(12, 'مرکز درمانی پارس', 95000000.00, true),
(13, 'داروخانه ابن‌سینا', 62000000.00, true),
(14, 'داروخانه لقمان', 230000000.00, true),
(15, 'داروخانه دکتر راد', 42000000.00, true),
(16, 'درمانگاه شبانه‌روزی پگاه', 78000000.00, true),
(17, 'داروخانه دکتر فیروز', 135000000.00, true),
(18, 'داروخانه البرز', 29000000.00, true),
(19, 'داروخانه بهار', 50000000.00, true),
(20, 'بیمارستان پارسیان', 390000000.00, true),
(21, 'داروخانه فرهنگ', 16000000.00, true),
(22, 'داروخانه قائم', 88000000.00, true),
(23, 'داروخانه سینا', 0.00, true),
(24, 'داروخانه اکسیر', 125000000.00, true),
(25, 'درمانگاه ایثار', 45000000.00, true);

-- 5. Stock Movements: Deterministic stock states for 40 products
-- Products 1..30: Healthy (> 10 on hand)
INSERT INTO stock_movement (product_id, warehouse_id, moved_at, qty_delta, reason, batch_flag)
SELECT
    p.product_id,
    1,
    now() - ((80 - p.product_id) * INTERVAL '1 day'),
    (60.000 + (p.product_id * 5.000))::numeric(18,3),
    'purchase-seed',
    p.has_batch
FROM product p
WHERE p.product_id BETWEEN 1 AND 30;

-- Products 31..36: Low stock band (1..10 on hand) — Exactly 6 products
-- Product 31: 20 - 18 = 2 on hand
INSERT INTO stock_movement (product_id, warehouse_id, moved_at, qty_delta, reason, batch_flag) VALUES
(31, 1, now() - INTERVAL '70 days', 20.000, 'purchase-seed', false),
(31, 1, now() - INTERVAL '30 days', -18.000, 'sale', false),
-- Product 32: 30 - 26 = 4 on hand
(32, 1, now() - INTERVAL '65 days', 30.000, 'purchase-seed', true),
(32, 1, now() - INTERVAL '25 days', -26.000, 'sale', true),
-- Product 33: 25 - 20 = 5 on hand
(33, 1, now() - INTERVAL '60 days', 25.000, 'purchase-seed', false),
(33, 1, now() - INTERVAL '20 days', -20.000, 'adjustment', false),
-- Product 34: 20 - 13 = 7 on hand
(34, 1, now() - INTERVAL '55 days', 20.000, 'purchase-seed', false),
(34, 1, now() - INTERVAL '15 days', -13.000, 'sale', false),
-- Product 35: 25 - 16 = 9 on hand
(35, 1, now() - INTERVAL '50 days', 25.000, 'purchase-seed', false),
(35, 1, now() - INTERVAL '10 days', -16.000, 'sale', false),
-- Product 36: 30 - 20 = 10 on hand
(36, 1, now() - INTERVAL '45 days', 30.000, 'purchase-seed', true),
(36, 1, now() - INTERVAL '5 days', -20.000, 'adjustment', true);

-- Products 37..40: Out of stock (<= 0 on hand) — Exactly 4 products
-- Product 37: 20 - 20 = 0 on hand
INSERT INTO stock_movement (product_id, warehouse_id, moved_at, qty_delta, reason, batch_flag) VALUES
(37, 1, now() - INTERVAL '60 days', 20.000, 'purchase-seed', false),
(37, 1, now() - INTERVAL '22 days', -20.000, 'sale', false),
-- Product 38: 15 - 15 = 0 on hand
(38, 1, now() - INTERVAL '55 days', 15.000, 'purchase-seed', true),
(38, 1, now() - INTERVAL '18 days', -15.000, 'adjustment', true),
-- Product 39: 25 - 28 = -3 on hand (negative stock representative case)
(39, 1, now() - INTERVAL '50 days', 25.000, 'purchase-seed', true),
(39, 1, now() - INTERVAL '14 days', -28.000, 'sale', true),
-- Product 40: 10 - 10 = 0 on hand
(40, 1, now() - INTERVAL '40 days', 10.000, 'purchase-seed', false),
(40, 1, now() - INTERVAL '7 days', -10.000, 'sale', false);
