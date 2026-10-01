# GM Web 管理后台

开发分支基于 0.2.0。当前阶段提供独立 HTTP 模块和中文页面骨架，管理功能按任务验收结果继续接入。

## 构建与启动

```powershell
cd Web/gm-admin
npm ci
npm run build
cd ../..
dotnet build Server/86JP.Server.slnx -c Release
dotnet run --project Server/src/Dfo.Server.Host -c Release --no-build -- --GmWeb:Enabled=true --GmWeb:ListenUrl=http://127.0.0.1:18081
```

访问 `http://127.0.0.1:18081/gm/`。前端开发模式 `npm run dev`，Vite 将 `/api/gm` 转发至本机 18081；开发者修改实际后端监听地址时同步修改代理目标。

`GmWeb:Enabled` 默认 false，`GmWeb:ListenUrl` 默认本机 18081。与 `LauncherAuth:*`、`Networking:*` 开关独立；全部 HTTP 关闭时不启动 Kestrel。启用多个 HTTP 功能时使用不同端口，功能路由只在各自端口提供。Host 与各模块共享同一容器、运行态和数据库。

前端先构建，再执行 dotnet build/publish；Host 项目把 `Web/gm-admin/dist` 复制到输出/发布目录的 `wwwroot/gm`。服务运行不需要 Node。没有前端资源时，页面返回 503；缺失 assets 返回 404，未知 API 不会返回 SPA。

管理员认证和玩家资料将在后续阶段接入；骨架不提供资产操作。运行的客户端、PVF 和现有游戏服务不受当前隔离构建影响。
