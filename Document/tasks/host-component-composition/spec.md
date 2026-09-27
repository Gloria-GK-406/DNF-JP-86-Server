---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 一个宿主、两个入口组件

## Why and Outcome

Host 表达组件组合，HTTP 与游戏入口可分别演进。它们仍共享同一服务图与事务，不能因重构丢失 HTTP→TCP 登录票据。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | HTTP 组件负责监听、中间件、路由和 HTTP 状态；游戏组件装配 Networking/Protocol 并负责内容预热；Host 不直接引用具体网络/协议程序集 |
| RQ-002 | 公共模块仅由组合根装配；两个入口不创建第二个 IServiceProvider/Host，也不移动认证和存储语义 |
| RQ-003 | 现有 LauncherAuth/Networking 开关与默认关闭保持；HTTP 单开时 bootstrap 可用、注册可用，游戏票据登录在游戏未就绪时返回现有 service_unavailable 形状；二者关闭无监听 |
| RQ-004 | 公共预热在任何 hosted service StartAsync 之前完成；失败时无入口可用。HTTP 的 ready 依赖实际启动完成，关闭即不可用；游戏 ready 对应真实宿主启动且游戏入口启用 |
| RQ-005 | 原路由、帧、地址配置与优先级、资源相对 exe 解析、取消/停止及异常语义保留；不新增业务接口 |
| RQ-006 | 一份 Host 可执行产物继续跨平台自包含发布；启动文档明确组件和开关语义 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-002 | 源码/项目引用检查；既有 HTTP→TCP 真实进程测试通过，不复制数据库或票据注册表 |
| AC-002 | RQ-003 | 关闭/单 TCP/单 HTTP/同时开：监听集合符合配置；单 HTTP bootstrap ready、注册成功、登录 503；同时开登录成功 |
| AC-003 | RQ-004 | 无效 PVF 使启动失败且端口未监听；可控的预热阻塞期间两入口均不监听；停止后无监听并可重启 |
| AC-004 | RQ-005 | 现有 Host/LauncherApi/Networking/Protocol/认证回归通过；有 PVF 可初始化真实内容；错误配置在监听前拒绝 |
| AC-005 | RQ-006 | Windows/Linux 发布包启动、资源查找和 HTTP/TCP 探测通过；更新文档、不改发布数量或版本流程 |

## Success Evidence

运行日志保留在工作区 artifacts；持久化验证总结写 verification/completion-evidence.md。源码重构本身用静态依赖检查和既有回归保护，不用构造器形状伪造 TDD；对新增单 HTTP 就绪与生命周期边界增加行为测试。
