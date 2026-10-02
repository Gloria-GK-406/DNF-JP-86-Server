---
kind: rks-verification
decision: pass
---

# Completion verification: spec-001

2026-10-02，目标为当前工作区完整未提交结果，独立审批 `../reviews/change-review.md` approved（含 overload/重定向修正复核）。

| Condition | Fresh evidence |
|---|---|
| SC-1 / AC-001, AC-002 | `dotnet test ...Host.Tests.csproj -c Release --no-build --filter FullyQualifiedName~LauncherHttpHostStartupTests` exit 0，15 pass/0 fail/0 skip；包括四种 GM/launcher/game 组合及旧全关/非法配置路径。`artifacts/gm-tests/spec-001-host-verified.trx` |
| 原 HTTP 兼容 | `dotnet test ...HttpApi.Tests.csproj -c Release` exit 0，50 pass/0 fail/0 skip；`artifacts/gm-tests/spec-001-httpapi-final.trx` |
| SC-2 / AC-003 | `npm run build` exit 0，TypeScript 检查及 Vite 生产构建通过；使用固定依赖和 package-lock |
| SC-2 / AC-004 | `dotnet publish ...Host.csproj -c Release --no-restore -o artifacts/gm-foundation` exit 0；`python ./verification/static-smoke.py` exit 0：/gm/ 与 /gm/characters 200；缺失 asset、未知 GM API、GM 端口 launcher API 404；发布 JS 存在 |
| 产品/源基线 | `git diff --check` exit 0；源 main 干净且保持 5fbe2dad；没有修改运行中服务/数据库/PVF |

## TDD and corrections

- 新真实启动矩阵在旧基线执行 exit 1：4 fail/0 pass，均因 GM listener 不存在而超时，`spec-001-red.trx`。不是编译失败或测试代理。
- 首次实现暴露 GM-only 依赖登录器 IPublicEndpointSource，按真实异常修复：禁用 launcher 不注册其专用 readiness。
- 发布 smoke 发现 ASP.NET 把 /gm 与 /gm/ 看成相同路由，移除循环重定向；旧 HttpApi 测试发现可选参数破坏方法组，恢复单参 overload。均已独立复核。
- 旧非法配置测试一轮在进程退出等待20秒处超时；没有改变断言或生产逻辑。独立复现7/7和完整重跑15/15通过，保留 `spec-001-host-final.trx` 的首次超时证据，原因未确定。
- 当前前端基础包约685KB（gzip222KB），构建提示500KB建议阈值。最终页面接入后按实际页面拆包；该提示不影响骨架功能。

标准 CI/Release 的 npm 接入属于根 SC-6 的后续集成；当前证据证明运行说明中的先构建前端再发布路径，不宣称标准流水线已接入。
