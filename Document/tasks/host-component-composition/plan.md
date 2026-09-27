---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan

## Planning Choice

主体是一条装配/生命周期链，详细决策在此维护即可，不并行修改源码、不新增 Task Unit。

## Approach and Shared Decisions

- Generic Host 一次 Build。HTTP 项目扩展 IHostBuilder，条件调用 ConfigureWebHostDefaults；不自行 Build，不复制公共注册。
- 现有 LauncherApi 更名 HttpApi（连同测试项目），保留历史配置键；目录按功能保留扩展空间。
- 新建 GameServer 装配项目，引用 Networking/Protocol 与预热需要的内容配置；公共 Application/Infrastructure/Persistence 仍由 Host 注册一次。
- 游戏预热由 IHostedLifecycleService.StartingAsync 执行，利用宿主所有 StartingAsync 先于任一 StartAsync 的阶段保障。准备方法接受取消。共享就绪契约放 Abstractions，由游戏组件提供实现，HTTP 只读接口。
- HTTP readiness 与游戏 readiness 分离，注册/引导只需 HTTP ready；签发票据额外需要游戏 ready。不改变签发/消费实现或跨域授权策略。

## Work and Dependencies

### T-001 基线与行为保护（RQ-003/004/005）

检查现有 Host、HTTP、认证集成测试，跑基线。新增单 HTTP 行为用例，观察当前 bootstrap 不可用的 RED；补充组合开关、预热失败和停止用例。审查现有真实 HTTP→TCP 测试是否覆盖共用票据。

### T-002 组件迁移（RQ-001/002/004）

先建立 GameServer 和 HTTP 配置扩展，再把 Program 缩为共同注册和两个组件。复用 IServerModule 通用注册工具或保留在组合根；组件本身不注册公共模块。游戏启动日志和预热迁移到组件，明确就绪状态的开启/停止时机。更新 solution、项目引用与命名。

### T-003 就绪与兼容（RQ-003/005）

HTTP 独立启用时仍有路由；关闭时完全不装 WebHost，避免默认端口。配置从 final Host 配置读取，命令行继续覆盖环境和文件。确保内容根以 exe 为准。执行现有 Host 测试及新增行为用例，修复受项目更名影响的架构检查。

### T-004 完整验证与文档（全部 RQ）

Windows 全解决方案测试；有 PVF 的 Host/相关测试；Linux 隔离构建/运行及发布验证。现有用户进程/数据库均不参与写入。更新 AGENTS 和 Document 架构/发布说明，既有协议知识不标记为新的 ClientVerified。

### T-005 独立审查、收口

新鲜 Sol high 审查整个 diff、任务契约和证据，重点重复容器、提前监听、开关、配置优先级与取消。修正有效问题后运行相应检查，形成唯一完整本地提交，保留工作区等待用户决定集成。

## Verification

SC/AC 对应证据写完成验证；测试报告保留失败基线与修复后结果，不把无 PVF 跳过计作通过。工具或平台限制须明确记录，不伪称实机验收。
