# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已确认业务store、内部索引及协议边界并授权重构。具体槽位证据是执行中的调查输入，不改变目标。主线为一次完整边界迁移，部署和实机另行进行。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户要求契约和顺序执行。先改容器而漏迁发放/事件/持久化适配会产生错误落位，单次结尾检查成本高；先核定映射、再模型与操作、最后投影的显式Plan能防止此类返工。三个store共享移动与事务边界，应作为一个可编译、可回归的整体交付。
- Reroute conditions: 发现不能通过存储适配兼容现有数据、必须破坏式改库或需客户端补丁时，停止相关变更并重审范围。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 相比Lite需明确迁移依赖和覆盖面；只在隔离工作区测试且不触碰运行库，无需额外生产变更审批。完整结果由独立reviewer审阅后验证。

## Workspace

复用当前空闲工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，分支 `task/solo-play-roadmap`。已接受基线 `9c63059147ebccac99bcfb9ca0bfec63f64ddb1f`，启动前状态干净。该目录不因名称变化重建；当前新增文件仅本契约目录。
