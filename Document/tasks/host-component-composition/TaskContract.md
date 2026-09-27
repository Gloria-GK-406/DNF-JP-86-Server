---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 单进程 Host、HTTP 与游戏组件分层

## Context

### Source Request

用户明确：不拆进程，把 HTTP 与游戏网络下拆为组件项目，在 main 注册，共用持久化。随后授权：“对，这看起来就比较合理了。起一个任务契约完成吧。需要细致一点，影响的东西还是挺多的。”

### Current Situation

基线 main d32ecfc9。Program 同时承担 Host/WebApplication 选择、HTTP 监听和路由、游戏模块装配及 PVF 预热。LauncherApi 已是类库但不是完整启动组件。登录票据、在线状态及事务入口是当前进程中的共享实例。

## Goal

唯一启动项目成为组合根；HTTP 与游戏组件拥有各自启动装配，保持单进程、共享 DI/业务/持久化，现有登录、协议和发布部署行为继续可用。

## Scope

### In

- Host 及 HTTP/游戏组件的项目依赖、注册、配置、启动/停止和预热边界。
- HTTP 独立启停与自身就绪、游戏就绪的区分；为更新等 HTTP 功能留下自然归属。
- 相关架构文档、回归测试、Windows/Linux 构建及隔离运行验证。

### Out

- 多进程、RPC、数据库迁移、登录票据持久化、认证规则改变。
- 实现更新清单等新业务、配置 HTTPS、修改客户端或线上数据库。
- 推送、PR、合并、tag、替换现有部署。

## Constraints

- 用户边界：一个进程一个根 DI 容器；两个入口共享现有事务、认证、在线状态与持久化。
- 兼容边界：保留 HTTP 路径/报文、游戏帧、现有配置键、入口名称与自包含发布方式。
- 生命周期风险：游戏预热失败不得先开放入口；HTTP 关闭不得残留监听器；关闭时停止监听并释放资源。
- 保留领域分层；HTTP 不依赖游戏传输或具体游戏协议。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Host 只承担组合，HTTP/游戏组件拥有自身装配与启动细节 | 项目引用与源码审查、架构断言 |
| SC-2 | HTTP 签发的票据可由同进程 TCP 消费，原单次/过期等规则保留 | 真实 Host HTTP→Game 回归及认证测试 |
| SC-3 | HTTP、TCP 开关正确，无隐藏监听；HTTP 就绪不伪装游戏就绪 | 组合矩阵与启停检查 |
| SC-4 | PVF 预热先于入口，失败阻止监听；原无 PVF 启动仍可用 | 成功/失败启动检查及原 Host 回归 |
| SC-5 | 配置、路由、业务协议及数据库语义无非预期变化 | 全解决方案与有 PVF 的相关测试、差异审查 |
| SC-6 | Windows/Linux 单一发布包仍可启动，包含新增程序集/资源 | 两平台发布运行检查，独立数据和端口 |
| SC-7 | 隔离交付并记录验收边界 | 干净本地结果提交、文档及验证记录 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “起一个任务契约完成吧。需要细致一点，影响的东西还是挺多的。” | 契约、隔离实现、测试、独立审查、一次本地结果提交 |

## Requires Confirmation

- 后续推送/合并或替换用户实际运行环境。
