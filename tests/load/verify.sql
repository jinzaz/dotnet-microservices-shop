-- 压测后核对：下单接口返回 202 只代表"已受理"，订单最终确认 / 取消、有没有超卖只能在库里看。
-- 用 psql 分别连订单库和库存库执行，先设置压测开始时间（带时区）和秒杀 SKU：
--   \set since '2026-10-28 15:00:00+08'
--   \set sku 'SKU-FLASH'
-- 状态值：0 Submitted / 1 AwaitingPayment / 2 Confirmed / 3 Cancelled。

-- ===== 订单库 =====

-- 1. 各状态订单数。Submitted / AwaitingPayment 不为 0 说明链路还没跑完，等一会再查。
SELECT "Status", count(*) FROM order_orders
WHERE "CreatedOnUtc" >= :'since'
GROUP BY "Status" ORDER BY "Status";

-- 2. 端到端耗时（建单 → 支付成功确认），这才是用户真正等待的时间。
SELECT count(*) AS confirmed,
       round(percentile_cont(0.50) WITHIN GROUP (ORDER BY ms)::numeric) AS p50_ms,
       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY ms)::numeric) AS p95_ms,
       round(percentile_cont(0.99) WITHIN GROUP (ORDER BY ms)::numeric) AS p99_ms,
       round(max(ms)::numeric) AS max_ms
FROM (SELECT extract(epoch FROM "ConfirmedOnUtc" - "CreatedOnUtc") * 1000 AS ms
      FROM order_orders
      WHERE "CreatedOnUtc" >= :'since' AND "ConfirmedOnUtc" IS NOT NULL) t;

-- 3. 取消原因分布：秒杀里应该主要是库存不足；出现余额不足说明欢迎余额不够这轮压测用。
SELECT "CancellationReason", count(*) FROM order_orders
WHERE "CreatedOnUtc" >= :'since' AND "Status" = 3
GROUP BY "CancellationReason" ORDER BY count(*) DESC;

-- 4. 秒杀：成交件数（未取消的订单）。必须 ≤ 秒杀开始前的库存。
SELECT coalesce(sum(l."Quantity"), 0) AS sold
FROM order_lines l JOIN order_orders o ON o."Id" = l."OrderId"
WHERE o."CreatedOnUtc" >= :'since' AND o."Status" <> 3 AND l."Sku" = upper(:'sku');

-- 5. 幂等：同一个 Idempotency-Key 只能有一个订单（有唯一约束兜底，这里应当查不出行）。
SELECT "UserId", "IdempotencyKey", count(*) FROM order_orders
WHERE "CreatedOnUtc" >= :'since'
GROUP BY "UserId", "IdempotencyKey" HAVING count(*) > 1;

-- ===== 库存库 =====

-- 6. 秒杀 SKU 的库存。CHECK 约束保证 OnHand、Reserved 不会小于 0，
--    零超卖的判定是：开始前的 OnHand - 现在的 OnHand（已提交）+ Reserved（还在预占）= 第 4 条的 sold。
SELECT "Sku", "OnHand", "Reserved", "OnHand" - "Reserved" AS available
FROM inventory_stock_items WHERE "Sku" = upper(:'sku');
