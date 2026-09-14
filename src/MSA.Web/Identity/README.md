# MSA Identity Web

MSA 统一身份的登录、注册、密码重置与 OAuth 授权确认页面。开发服务器监听 `http://localhost:5174`，并将 `/api` 与 `/connect` 代理到 API Gateway `https://localhost:10000`。

```powershell
npm install
npm run dev
```

常用命令：

- `npm run check`：TypeScript 检查
- `npm run build`：生产构建
- `npm run preview`：预览生产构建

登录入口由 Identity Service 携带本地 `returnUrl` 跳转到 `/login`；登录或注册成功后，页面回到原始授权请求。密码找回使用账户已验证的手机号。商城地址可通过 `VITE_STORE_URL` 配置。
