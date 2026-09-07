# MSA Store 前端实施计划

## 目标与边界

首期交付 React + TypeScript + Vite 前端骨架，以及首页、登录注册页、个人中心和订单列表。个人中心对接现有 Identity Service 的资料、地址和积分接口；订单列表独立对接 Ordering Service；首页在认证后读取 Catalog Service 商品列表。商品详情、购物车、结算、订单详情和支付保留清晰的路由与 API 边界，后续按业务阶段接入。

## 架构

- `app/`：路由、全局 Provider 与认证边界。
- `shared/`：设计系统、布局、HTTP 客户端、运行时配置和通用工具。
- `features/`：按 `auth`、`catalog`、`profile` 业务能力组织 API、类型、状态和组件。
- `pages/`：只负责页面级组合，避免直接访问微服务或保存业务状态。
- 所有服务请求经 API Gateway：`/identity`、`/catalog`、`/basket`、`/order`、`/payment` 等前缀，不跨服务拼装数据。

## 首期 API 对接

| 能力 | Gateway 路径 | 前端策略 |
| --- | --- | --- |
| 用户注册 | `POST /identity/api/auth/register` | 按 Identity 密码规则校验，注册成功后自动建立登录会话 |
| Cookie 登录 | `POST /identity/api/auth/login` | 为 Identity 自身的 Cookie 认证建立会话 |
| OAuth 令牌 | `POST /identity/connect/token` | 使用仓库现有 OpenIddict password grant，访问其他服务 |
| 当前用户 | `GET/PUT /identity/api/users/me` | 展示与编辑资料 |
| 地址 | `/identity/api/users/me/addresses` | 列表、新增、编辑、删除、设为默认 |
| 积分 | `/identity/api/users/me/points` | 账户汇总与明细并行读取 |
| 我的订单 | `GET /order/api/orders` | 按当前用户和履约状态读取，页面不拼接其他服务业务数据 |
| 商品 | `GET /catalog/api/products` | 认证后读取；访客展示静态编辑精选内容 |

当前后端没有注销端点，首期“退出登录”会清理浏览器内令牌与前端缓存；生产环境建议补充服务端 Cookie 注销。长期建议将 password grant 收敛为 Authorization Code + PKCE 或 BFF 会话。

## 后续阶段

1. 商品详情：Catalog 查询、图片媒体模型、Inventory 可售库存。
2. 购物车：Basket 查询/增删改、优惠码、并发版本处理。
3. 结算下单：地址快照、价格确认、Ordering 幂等键与 Saga 状态。
4. 支付：支付渠道选择、跳转/二维码、回调后的订单状态轮询。
5. 订单详情：订单行、履约时间线、取消/退款允许状态。

## 视觉系统

- 真白背景 `#ffffff`，深墨蓝文本 `#071a3d`，主操作橙 `#ff3d12`，信息带矿物蓝 `#0b4dbf`。
- 内容容器最大 1336px；开放式分区、细边框和低强度阴影，避免默认卡片堆叠。
- 中文优先的系统无衬线字体栈；控件字号明确为 14–16px。
- 图标统一为 1.75px 圆角描边；动效控制在 160–240ms，并尊重 `prefers-reduced-motion`。
- 设计参考见 `docs/design/`，页面使用的独立生成素材见 `public/images/`。
