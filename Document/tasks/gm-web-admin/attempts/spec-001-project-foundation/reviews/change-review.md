---
kind: rks-review
verdict: approved
---

# Spec-001 项目与启动骨架独立结果审查

测试证据仍以主代理后续新运行结果为准。本审查批准当前代码和运行说明的设计与实现；没有执行 dotnet build/test，也不替代 completion verification 或宣称整个 GM 后台已完成。

## 目标与依据

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`。
- 审查基线：`5fbe2dad35bf871a048ce4667055751e973bd844`；目标为 2026-10-02 当前未提交的完整 spec-001 产品差异及新增文件。
- 读取根/子 TaskContract、spec、routing-decision、sdd-execution、仓库 AGENTS，以及 reviewing-work 的 Code / Deliverable references；本子任务使用 sdd-lite，未配置 Plan 或挑战集。
- 范围：HttpApi 接入点和启动配置、GmWeb 模块、Host/solution 接线、中文 React 页面、前端构建配置与锁文件、静态资源复制、Host 启动测试、`Document/gm-web-admin.md`。

## 决定性证据

1. `HttpApiHostingExtensions.ConfigureHttpApi` 仅收集已启用功能；集合为空即不注册 WebHost，多功能要求不同端口，且共用当前 Host/容器。Launcher 路由仅在 LauncherAuth 启用时注册。
2. `HttpListenerScope` 元数据配合路由之后的 `HttpListenerBoundaryMiddleware` 检查实际 `Connection.LocalPort`，在错误监听入口返回 404；不根据可伪造的 Host 请求头判断端口。GmWeb 的页面与健康 API 同样携带所属端口元数据。
3. 当前 `HttpApiModule.ConfigureServices` 在 Launcher 禁用时提前返回，不注册其 readiness/startup service。已独立读取这一修正及 `HttpApiStartupService`，确认 GM-only 不再经该服务强制解析 Launcher 的 `IPublicEndpointSource`。
4. 旧 Launcher bootstrap/register/login 路径、凭证传输门、响应形状与命令调用保持；增加端口边界没有改动它们的认证逻辑。新增真实 Host theory 覆盖 GM 开启时 Launcher/游戏的四种组合；旧测试继续覆盖 Launcher-only 和全关。
5. `GmWebModule.ServePage` 从 exe 目录下固定 `wwwroot/gm` 出发，规范化完整路径并要求仍位于该根下。缺失带扩展名文件和 assets 返回 404；只对 `/gm` 下无扩展名页面路径回落 index，未知 `/api` 不进入 SPA。首页设置 no-cache，构建出的哈希资源设置 immutable。
6. 前端使用 React/TypeScript/Vite，唯一组件设计库为 Ant Design；basename/base 均为 `/gm`，导航和空状态明确尚无管理能力。package.json 和 package-lock 固定依赖；已读取生成的 dist/index.html，JS/CSS 引用均为 `/gm/assets/...`。
7. Host Content 项的 Link、CopyToOutputDirectory、CopyToPublishDirectory 将预先生成的 dist 文件映射至 `wwwroot/gm`。运行说明明确先 npm ci/build 再 dotnet build/publish，无前端资源时返回 503，运行不要求 Node。

## 当前修正复核

主代理后续真实验证发现首页重定向循环和旧方法组编译兼容问题。已读取两项最新产品修正，当前批准覆盖修正后的完整目标，替代先前目标的审查结论：

- 删除精确 `/gm` 重定向，页面统一使用 `/gm/{**path}`。空路径直接读取 index，不再发出 Location；`/gm` 与 `/gm/` 的路由等价不会再次构成自身重定向。前端 base、资源绝对路径和 BrowserRouter basename 与该入口保持一致。
- 恢复 `MapHttpApiEndpoints(IEndpointRouteBuilder)` 单参数 overload，委托到带 scope 的双参数实现；旧方法组调用保持原签名，新 Host 双参数调用仍携带端口边界。独立检查无重复路由注册或丢失 listener metadata。
- 已只读核对 `artifacts/gm-tests/spec-001-httpapi-final.trx`：50 executed / 50 passed / 0 failed；`spec-001-green-final.trx`：15 executed / 15 passed / 0 failed。前者是修正后的 HttpApi 回归，后者尚按主代理说明为上一轮 Host 证据；新完整目标的 Host 重跑与静态 HTTP smoke 仍由主代理完成。
- 已核对 `artifacts/gm-foundation/wwwroot/gm` 中存在 index.html 与构建出的哈希 JS/CSS；该文件存在性检查不替代实际页面和错误路由响应检查。

## 验证边界与后续收口

主代理应在子任务封口前保留新的启动矩阵/旧 HTTP 回归、npm 类型检查与构建、真实 publish 输出及 `/gm/characters`、assets、未知 API、跨端口路由响应证据。静态审查不能代替这些执行结果。

当前标准 CI/Release 仍未执行 npm build，干净 checkout 的 dotnet publish 单独执行不会生成前端。依据本子 Spec 的“开发者构建前端后”边界，手动构建发布流程可以接受；主代理已将标准管线接入明确留到最终集成 Spec。根 SC-6 封口前必须完成此项，不应将本次批准描述为现有 CI 包已经包含页面。

未发现当前审查范围内需修改的阻塞缺陷。认证、玩家资料及资产端点属于后续子结果，本阶段仅提供页面骨架和不含玩家数据的健康端点，未提前开放敏感操作。

## 当前关键目标指纹

| 文件 | SHA-256 |
|---|---|
| GmWeb/GmWebModule.cs | `479DA81783F1A05371A999CA1C71802B9827079A0A293ECB2571E73854A28C57` |
| HttpApi/HttpApiApplicationExtensions.cs | `CA311AE0829F805517027093B31C95167B38E0C41D5DB1BB07042ADC11F8DC5D` |
| HttpApi/HttpApiModule.cs | `697DED4B22EFFDD96C6ACE2C6565C3FB605C3D8EBB3418841683344B8DB281C9` |
| HttpApi/HttpApiHostingExtensions.cs | `2175C28BD53BE817C3789DA1A72A7820CD3F7B840C88E3688218F329329EDF19` |
| Document/gm-web-admin.md | `CC74BF5F87312C2A01ADF15DE728EF995B813D075FB1E1A06CEA7E18B5183D63` |
