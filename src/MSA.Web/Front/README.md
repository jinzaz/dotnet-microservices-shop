# MSA Store Front

## 本地运行

```powershell
npm install
npm run dev
```

开发服务器监听 `http://localhost:5173`，并将 `/api` 与 `/bff` 代理到 `MSA.BFF.Web`（`https://localhost:10008`）。登录、注册和注销经 BFF 的 Authorization Code + PKCE 流程完成，浏览器只保存会话 Cookie，不保存 access token 或 refresh token。运行真实数据页面前请启动 BFF、Gateway、Identity、Identity UI、Catalog 及其依赖。

仓库父目录 `C#.NET` 含有 Vite 不支持的 `#` 路径字符；`npm run dev` 会先将 Windows 临时目录规范化为长路径，再创建无 `#` 的目录联接，并在正常退出时清理。这样可以同时避开 `#` 与 `ADMINI~1` 一类 8.3 短路径限制，无需调整现有仓库路径。

## 命令

- `npm run dev`：开发服务器
- `npm run check`：TypeScript 检查
- `npm run build`：生产构建
- `npm run preview`：预览生产构建

开发环境可通过下列入口做纯前端展示与交互验收：

- `/account?demo=1`：账户、积分与地址
- `/coupons?demo=1`：优惠券筛选、复制与兑换
- `/products/editorial-pour-over?demo=1`：商品图片、SKU、评价与加入购物袋
- `/checkout?demo=1`：地址、配送、支付、优惠与提交订单
- `/orders?demo=1`：订单列表

演示入口只在开发环境生效，生产构建不会启用演示模式。

## 业务对接

- 商品：`/catalog/products`（分页、搜索、分类）、`/catalog/products/{id}`、`/catalog/categories`，匿名可浏览已发布商品。
- 购物袋：登录后以 BasketService 为准，所有修改携带 `If-Match` 版本号，412 时自动重载重试；未登录时保存在本地，登录后自动合并。
- 下单：提交时携带 `sourceBasketId/sourceBasketVersion`，由 BasketService 在订单受理后清空对应版本的购物袋。
- 订单：列表分页、详情、未付款订单取消（`POST /order/orders/{id}/cancel`）、再次购买。
- 售后（预留）：`src/features/after-sales` 定义了售后类型、状态、申请/撤销/寄回的接口契约（`/order/after-sales`、`/order/orders/{id}/after-sales`）。后端未上线时接口返回 404，页面显示“即将开放”且不丢失已填写内容。
- 用户券包、商品评价的消费者接口仍未提供，相关页面保留演示数据或空状态。
