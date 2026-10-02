---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: GM 项目与启动骨架

## Why and Outcome

管理员可以单独启用后台；开发者构建前端后，服务发布包包含页面。公共 HTTP 宿主接入功能模块，保留原启动器路径。

## Scope

独立模块和静态页面；只公开无玩家数据的健康端点，后续管理端点在管理员模块就绪后提供。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | GM 与 LauncherAuth 独立开关/地址，任一启用共用一个 Kestrel；全关无 HTTP 监听 |
| RQ-002 | 每组路由只在所属配置监听入口可达，旧 launcher 认证行为保持 |
| RQ-003 | React/TS/Vite/Ant Design 中文骨架有一致导航、空状态和环境说明，可构建发布 |
| RQ-004 | 页面及嵌套路由随 Host 发布，API 和缺失静态文件不误返回页面 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | GM-only、Launcher-only、双开、全关与游戏开关的真实启动场景正确 |
| AC-002 | RQ-002 | GM 端口上的 launcher 路由及 launcher 端口上的 GM 路由返回 404；原认证测试通过 |
| AC-003 | RQ-003 | 前端类型检查/构建通过，源码仅使用单套组件库，页面显示当前能力未开放的事实 |
| AC-004 | RQ-004 | 发布输出包含 gm/index.html，访问 /gm/characters 返回 SPA，缺失 assets 返回404 |

## Success Evidence

Host HTTP 集成测试、npm 构建和发布资源检查，独立结果审查。启动矩阵是实际消费者可见结果，能够捕捉旧测试未覆盖的 GM-only 和路由串口回归；不把内部依赖选择写成行为断言。
