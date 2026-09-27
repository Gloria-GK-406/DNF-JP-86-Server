---
kind: rks-review
verdict: approved
---

# 完整变更独立审查 — round 2

## 审查对象与判据

2026-09-27；基线 `d32ecfc9`，分支 `task/host-component-composition` 的当前未提交完整变更，包括新增 HttpApi/GameServer 项目、Abstractions 就绪接口、配置辅助类及新测试。审查依据为 TaskContract revision 1、ready spec/plan、implementation.md、仓库 AGENTS，以及 reviewing-work 的 code/deliverable 判据。未改动生产或测试代码，未提交。

round 2 仅复审 round 1 的环境配置文件复制修正及其直接回归面；其余源码审查证据沿用下列记录。当前审查范围内没有未解决阻塞缺陷，独立完整变更审查通过。

## 已关闭发现（round 1）

### P2 — 环境配置文件失去构建与发布复制规则（已修正）

- 位置：`Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj:1`、`:23`；关联 `Server/src/Dfo.Server.Host/ServerConfiguration.cs:19`。
- 触发与后果：项目内使用 `appsettings.Production.json`、`appsettings.Development.json` 等环境配置文件时，旧 Web SDK 会将其复制到输出和发布目录。迁移到普通 SDK 后只有 `appsettings.json`、`experience.json` 和频道资源设置复制，环境配置文件作为普通 None 项没有复制元数据。启动仍以 exe 目录读取可选环境文件，因而构建/发布后静默忽略这些配置；例如环境文件里的启用开关、端口或数据库路径失效，回落到基础配置。把文件手工放进发布目录仍可用，不能补上原项目构建/发布行为。
- 证据：`dotnet msbuild Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -nologo -getItem:None -getItem:Content` 显示当前项目无 Content 项，复制规则仅覆盖上述三个路径。固定 SDK 10.0.302 的 `Microsoft.NET.Sdk.StaticWebAssets/Sdk/Sdk.StaticWebAssets.StaticAssets.ProjectSystem.props:37` 为原 Web SDK 设置 `**/*.json` 的 `CopyToOutputDirectory=PreserveNewest` 和 `CopyToPublishDirectory=PreserveNewest`；普通 SDK不带此规则。当前仓库仅有基础文件，因此已通过的 smoke 不会暴露这条环境文件回归。
- 影响要求：RQ-005 配置兼容、RQ-006 发布兼容；新组件文档亦明确列出 `appsettings.{Environment}.json`。
- 最小修正：Host 项目显式给 `appsettings.*.json` 设置输出和发布复制，保留当前基础配置与 experience 复制规则；使用临时环境文件验证构建/发布产物包含它，并确认选择对应环境后其值生效。无需回退整个 Web SDK，也无需改业务代码。

round 2 已核对 Host csproj 的 `None Update="appsettings.json;appsettings.*.json"`，明确设置输出和发布两项 PreserveNewest，现有 experience/频道资源规则保留。审閱 `artifacts/env-probe.py`、`env-probe-result.log`、`env-probe-publish.log`：临时 ComponentProbe 配置在 build/publish 目录与源文件逐字节相同；指定 `DOTNET_ENVIRONMENT=ComponentProbe` 后自包含进程正常存活，并采用 Warning 日志级别。探针源文件已删除。此修正满足 round 1 的最低修正条件，不引入新的 provider、业务或生命周期变化。

## 已核对且无阻塞缺陷的范围

- 一个组合根：Program 只有一次 Host.Build；公共 Infrastructure/Persistence/Application 仅在 Host 注册。两个组件扩展同一个 IHostBuilder，不自行 Build/CreateProvider。HttpApi 项目引用只有 Abstractions/Application/Data；GameServer 拥有 Networking/Protocol 装配，Host 不直接引用二者。
- 生命周期：GameServerStartupService.StartingAsync 承接原 PVF 初始化，并传递既有异步接口的取消令牌。宿主 lifecycle 阶段先于所有 hosted service StartAsync；预热阻塞/失败测试检查两个入口均未监听。缺失 PVF 继续允许启动，原同步初始化接口未变。
- 开关与就绪：HTTP 关闭直接不装配 WebHost，没有默认 Kestrel listener；Networking 的现有条件注册保证关闭 TCP/UDP。HTTP 就绪在 ApplicationStarted 才置 true，ApplicationStopping/StopAsync 撤销；游戏就绪同时要求网络启用和宿主已启动且未停止。HTTP 单开 bootstrap/注册可用，票据登录返回原 503 形状。
- 共享认证和存储：认证、事务、票据与持久化实现没有变更。现有真实进程 HTTP→Channel→Game 测试经 HTTP 签发票据，TCP 消费后读取同账号角色列表；这覆盖消费者可观察的身份连续性。HttpApi 路由/报文、传输门及异常响应与原 LauncherApi 的规范化差异只涉及更名和游戏就绪判断。
- 配置优先级：文件、环境变量、命令行以及原 experience 最后覆盖顺序在源码中保持；内容根固定 exe 目录。开关/地址启动时决定的说明与实现一致。上面的环境文件打包缺陷独立于运行时加载顺序。
- 停止与依赖：四组开关测试实际 Start/Stop 后重新 bind 三个端口，覆盖监听释放。solution/测试项目重命名一致，发布仍只生成一个 Host 包，新增组件通过 ProjectReference 传递。

## 检查证据与完成边界

审阅 `artifacts/full-final/*.trx` 汇总为 4095 个结果、3638 passed、0 failed，剩余 457 是跳过结果；`artifacts/pvf-host.log` 为有 PVF 的 Host 24 passed；`artifacts/publish-win.log` 表示 Windows 自包含 Host 在入口关闭条件下启动成功。`git diff --check` 无输出。

Linux 全量及发布检查由主执行者进行，平台完成证据与发布产物的 HTTP/TCP 探测仍由最终 verification 阶段收口。主执行者报告 Linux 首轮全量出现频道握手 15 秒超时、单独 Host 复跑 24/24 通过，完整复跑和发布仍在进行；本审查不将这一待归因执行异常作为已证实代码缺陷，也不将其抹去为已完成验收。Windows 关闭入口 smoke 与跳过项不等于完整双平台验收，当前 approved 不替代 SC-1 至 SC-7 的最终完成证明。
