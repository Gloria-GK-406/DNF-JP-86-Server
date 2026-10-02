# GM Web 管理后台

开发分支基于 0.2.0。当前提供独立 HTTP 模块、中文管理员登录、权限菜单与分页审计；玩家查询和资产操作在后续阶段接入。

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

## 管理员首次初始化

管理员与玩家账号独立。首次启动从部署环境配置 `GmWeb:Bootstrap:LoginName`、`GmWeb:Bootstrap:Password`，环境变量对应 `GmWeb__Bootstrap__LoginName`、`GmWeb__Bootstrap__Password`。用户名为 1–64 个 ASCII 字母、数字、点、下划线或短横线；密码 12–256 字符。使用部署秘密注入，避免把密码放在命令行、仓库配置文件或终端历史中。

可选 `GmWeb:Bootstrap:Permissions` 支持逗号分隔 `QueryPlayers,SendMail,GrantCera,ViewAudit`，默认全部。权限表示玩家查询、官方邮件、点券发放、审计查看；本阶段只开放审计业务。已有管理员时 bootstrap 不改变其密码、启用状态和权限。未配置、缺项或无效配置不会阻止 GM 启动，但不会创建管理员；页面显示初始化提示。

会话保持最多 8 小时，服务重启后仍可恢复；退出会撤销旧 Cookie，禁用管理员或改变权限在下一请求生效。数据库只保留密码散列与会话秘密的 SHA-256 摘要。首次初始化后可移除 bootstrap 密码环境变量；后续多管理员或密码管理界面不在当前模块内。

## 访问与审计

默认本机 HTTP；远程管理使用直接 HTTPS 与有效服务证书（Kestrel 的标准证书配置）。服务器采用实际连接判断，不信任请求携带的 `X-Forwarded-*`/`Forwarded`。本阶段没有反向代理信任配置；远程明文管理请求被拒绝。不要用代理头替代 HTTPS。

登录和所有状态写入使用 `X-GM-CSRF` 防护令牌，前端自动取得并提交。令牌来自 `/api/gm/v1/auth/csrf`，与浏览器 Cookie 和当前管理身份匹配；切换身份或重启服务后重新取得。跨站来源及无效令牌返回 403。CSRF 使用进程临时保护密钥，不写操作系统用户默认密钥目录；重启后 GM 会话仍在，旧 CSRF 需刷新。

同来源、同登录名在 15 分钟失败 5 次后限流；同来源累计失败 20 次也限流，防止换登录名绕过。限流窗口与容量有边界，返回 429。登录失败提示不区分账号是否存在。

登录成功、凭据失败、正常退出均写审计，审计页显示时间、操作、管理员、对象、结果和操作编号。审计能力复用已有事务，后续发放操作可以与记录一起提交或回滚。匿名私有请求返回 401，权限不足返回 403，分页参数不合法返回 400。

运行的客户端、PVF 和现有游戏服务不受当前隔离构建影响。验证使用临时数据库；本文件的启动示例面向独立部署，验证时须显式传入独立 `Database:Path`。
