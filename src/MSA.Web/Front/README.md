# MSA Store Front

## 本地运行

```powershell
npm install
npm run dev
```

开发服务器监听 `http://localhost:5173`，并将微服务前缀代理到 API Gateway `https://localhost:10000`。运行真实数据页面前请启动 Gateway、Identity、Catalog 及其依赖。

仓库父目录 `C#.NET` 含有 Vite 不支持的 `#` 路径字符；`npm run dev` 会先将 Windows 临时目录规范化为长路径，再创建无 `#` 的目录联接，并在正常退出时清理。这样可以同时避开 `#` 与 `ADMINI~1` 一类 8.3 短路径限制，无需调整现有仓库路径。

## 命令

- `npm run dev`：开发服务器
- `npm run check`：TypeScript 检查
- `npm run build`：生产构建
- `npm run preview`：预览生产构建

个人中心可在开发环境用 `/account?demo=1` 做纯前端视觉与交互验收。该入口在生产构建中不可用。
