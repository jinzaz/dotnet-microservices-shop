# MSA Store Front

## 本地运行

```powershell
npm install
npm run dev
```

开发服务器监听 `http://localhost:5173`，并将 `/api` 代理到 `MSA.BFF.Web`（`https://localhost:10008`）。登录、注册和注销经 BFF 的 Authorization Code + PKCE 流程完成，浏览器只保存会话 Cookie，不保存 access token 或 refresh token。运行真实数据页面前请启动 BFF、Gateway、Identity、Identity UI、Catalog 及其依赖。

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

演示入口只在开发环境生效，生产构建不会启用演示模式。用户券包、兑换/领取、商品 SKU 与 Review 的消费者接口地址已经在对应 feature API 模块中预留；接口上线后页面会优先使用真实数据。
