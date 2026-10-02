---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-6
depends_on: []
---

# TaskContract: GM 项目与启动骨架

## Context

### Source Request

根合同授权按项目到各模块逐步分 Spec。当前剩余完整能力；先建立可独立启动和发布的模块边界。

### Current Situation

接受基线 5fbe2dad，HTTP 耦合 LauncherAuth；没有 Web 工程。

## Goal

后台与登录器 API 分别启用并共用 Host/Kestrel，建立可构建的中文 React/Ant Design 页面。

## Scope

### In

- HTTP 功能接入点、GmWeb 项目/配置、Host/solution 接线。
- GM 页面与 API 限定其配置监听入口，开关关闭时无监听/路由。
- Vite/React/TypeScript/Ant Design 项目、导航骨架、构建与发布静态资源。
- 启动组合回归与运行说明。

### Out

- 玩家资料与发放 API、管理员认证，归后续子结果。

## Constraints

- 同一 Host 和根容器，不启动第二个服务；已有登录器协议不变。
- 此阶段不开放敏感数据和资产操作。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | GM 可独立于登录器启动；双开、全关、游戏开关不造成隐藏监听或路由串口 | 真实启动矩阵与旧 HTTP 回归 |
| SC-2 | React 页面可构建，发布资源随 Host 复制，嵌套路由可打开 | npm 构建、发布资源和 HTTP 检查 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根合同中的完整用户授权 | 当前项目与启动骨架、验证和本地验收提交 |
