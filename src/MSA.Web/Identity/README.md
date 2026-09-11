# MSA Identity Web

MSA Store 的登录与注册页面。开发服务器监听 `http://localhost:5174`，并将 `/api` 与 `/connect` 代理到 Identity Service `https://localhost:10002`。

```powershell
npm install
npm run dev
```

常用命令：

- `npm run check`：TypeScript 检查
- `npm run build`：生产构建
- `npm run preview`：预览生产构建

登录入口由 Identity Service 携带本地 `returnUrl` 跳转到 `/login`；登录或注册成功后，页面回到原始授权请求。商城地址可通过 `VITE_STORE_URL` 配置。
