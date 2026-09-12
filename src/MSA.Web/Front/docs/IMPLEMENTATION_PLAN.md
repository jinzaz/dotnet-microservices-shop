# MSA Store 前端实施计划

## 目标与边界

当前交付 React + TypeScript + Vite 商城前端，以及首页轮播、登录注册、个人中心、券包、订单列表、商品详情、本地购物袋与结算下单。个人中心对接 Identity Service 的资料、地址和积分接口；订单列表和创建订单对接 Ordering Service；商品与库存分别对接 Catalog 和 Inventory；购物袋同步 Basket。暂缺的用户券包、商品 SKU 列表和评价接口均保留独立前端契约与演示回退。

## 架构

- `app/`：路由、全局 Provider 与认证边界。
- `shared/`：设计系统、布局、HTTP 客户端、运行时配置和通用工具。
- `features/`：按 `auth`、`catalog`、`basket`、`checkout`、`coupons`、`reviews`、`profile` 业务能力组织 API、类型、状态和组件。
- `pages/`：只负责页面级组合，避免直接访问微服务或保存业务状态。
- 所有服务请求先进入 BFF 的 `/api` 代理，再由 BFF 携带服务端保存的 access token 转发到 API Gateway；浏览器不保存或发送 OAuth token。

## 首期 API 对接

| 能力 | BFF 路径 | 前端策略 |
| --- | --- | --- |
| 登录与注册 | `GET /api/bff/login` | 启动 Authorization Code + PKCE；凭据只在 Identity UI 输入 |
| 登录状态 | `GET /api/bff/user` | 以 BFF 服务端会话为准，不读取浏览器存储 |
| 注销 | `POST /api/bff/logout` | 携带 CSRF 表单令牌并完成本地会话及 Identity 注销 |
| 修改密码 | `POST /api/identity/api/auth/change-password` | BFF 注入 access token，Front 只提交当前密码和新密码 |
| 当前用户 | `GET/PUT /api/identity/api/users/me` | 展示与编辑资料 |
| 地址 | `/api/user/me/addresses` | 列表、新增、编辑、删除、设为默认 |
| 积分 | `/api/user/me/points` | 账户汇总与明细并行读取 |
| 我的订单 | `GET /api/order/orders` | 按当前用户和履约状态读取，页面不拼接其他服务业务数据 |
| 商品 | `GET /api/catalog/products` | 认证后读取；访客展示静态编辑精选内容 |
| 商品详情 | `GET /api/catalog/products/{id}` | 读取真实商品；编辑精选商品使用同结构的本地展示数据 |
| SKU 列表 | `GET /api/catalog/products/{id}/skus` | 后端暂缺，前端已预留并按单 SKU 或演示规格回退 |
| 可售库存 | `GET /api/inventory/{sku}` | 登录用户读取；失败时展示结算确认提示 |
| 购物袋 | `/api/basket` | 新增、改数量、删除、优惠码接口已封装；本地版本化草稿保证页面可操作 |
| 我的优惠券 | `GET /api/catalog/coupons/me` | 后端暂缺消费者接口，页面自动使用演示券包 |
| 兑换/领取优惠券 | `POST /api/catalog/coupons/me/redeem`、`POST /api/catalog/coupons/{id}/claim` | 地址已预留，演示模式支持完整交互 |
| 商品评价 | `GET /api/review/products/{id}/reviews` | Review 上下文需求已成立但服务暂缺，页面使用演示评价 |
| 创建订单 | `POST /api/order/orders` | 发送地址快照、SKU、支付方式、优惠码和 `Idempotency-Key` |

所有非安全方法会先从 `/api/bff/csrf` 获取请求令牌并通过 `X-XSRF-TOKEN` 发送；注销使用表单字段提交同一令牌，以便浏览器继续处理身份提供方的重定向链。

## 后续阶段

1. 后端补充面向消费者的 SKU、用户券包/领券与 Review 查询契约。
2. 将本地购物袋草稿与 Basket ETag 版本合并，完善跨设备恢复与冲突提示。
3. 支付渠道跳转/二维码、回调后的订单状态轮询。
4. 订单详情、订单行、履约时间线、取消/退款允许状态。

## 视觉系统

- 真白背景 `#ffffff`，深墨蓝文本 `#071a3d`，主操作橙 `#ff3d12`，信息带矿物蓝 `#0b4dbf`。
- 内容容器最大 1336px；开放式分区、细边框和低强度阴影，避免默认卡片堆叠。
- 中文优先的系统无衬线字体栈；控件字号明确为 14–16px。
- 图标统一为 1.75px 圆角描边；动效控制在 160–240ms，并尊重 `prefers-reduced-motion`。
- 设计参考见 `docs/design/`，页面使用的独立生成素材见 `public/images/`。
