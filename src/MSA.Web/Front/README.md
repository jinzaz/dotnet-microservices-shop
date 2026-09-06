# MSA Store Front

## 本地运行

```powershell
npm install
npm run dev
```

开发服务器监听 `http://localhost:5173`，并将微服务前缀代理到 API Gateway `https://localhost:10000`。运行真实数据页面前请启动 Gateway、Identity、Catalog 及其依赖。

## 命令

- `npm run dev`：开发服务器
- `npm run check`：TypeScript 检查
- `npm run build`：生产构建
- `npm run preview`：预览生产构建

个人中心可在开发环境用 `/account?demo=1` 做纯前端视觉与交互验收。该入口在生产构建中不可用。
