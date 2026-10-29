---
kind: rks-verification
decision: manual_required
---

# 暗精灵遗迹候选验证

验证对象为 `codex/darkelf-ruins-75` 的服务端候选 `0.2.11`，基线 `main@81be4dfd`。
共享入场、运行种类、能力表和清场钩子已按新主干接入，保留遗忘之地的怪物、房间与传送扩展。
结论限于当前实现和自动验证；完整功能交付仍须按 [实机步骤](user-test.md) 获得 A21 用户反馈。

## 自动结果

所有写测试使用临时库；真实内容测试显式设置
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
证据根目录为工作区忽略的 `artifacts/darkelf-rebase-20261029/`。

| 检查 | 结果 | 日志 |
|---|---|---|
| 完整解决方案构建 | 通过，0警告/0错误 | `build-81be4dfd.log` |
| 完整解决方案 `Category=Unit` | 1666通过，0失败/0跳过；无匹配分类的项目不代表全量通过 | `unit-81be.log` |
| 遗迹、遗忘之地、修理券及房间/移动/复活/运行种类/依赖图领域回归 | 286通过，0失败/0跳过 | `application-81be.log` |
| 活动PVF遗迹与遗忘之地内容 | 12通过，0失败/0跳过 | `content-81be.log` |
| 遗迹codec、遗忘之地时限、注册、协议结构、选图、入场、清场 | 214通过，0失败/0跳过 | `protocol-81be-fixed.log` |
| GM前端测试 | 158通过，13文件 | `gm-test.log` |
| GM前端生产构建 | 通过；存在Vite大块体积提示 | `gm-build.log` |
| 协议文档检查器自测 | 通过 | `docsync-self.log` |
| 协议文档全量同步 | 与原基线相同18项既存失败，本次新增0；不能标为全量通过 | `docsync.log`及原任务基线日志 |

GM源码在本次两个主干基点之间没有变化。协议入场测试夹具补齐遗忘之地空实现后，受影响测试214项通过。
自动测试不证明 A21 原生入口、传送或专用结果已通过实机验收。

## 部署与数据保护

新包部署记录位于 `artifacts/darkelf-rebase-20261029/deployment/`。
切换前重新核实进程EXE、创建时间、默认端口归属、有效配置及环境覆盖。
使用 SQLite backup API 备份活动玩家库；先以副本库和独立端口验证迁移、健康及GM页面，再停止原服务并生成最终一致备份。
默认端口与原玩家库路径见 [实机测试流程](user-test.md)。不对玩家库运行测试夹具或重置资料。
`prior-process.json`、`prior-appsettings.json` 和 `prior-runtime.json` 保留实际原服务恢复依据；
`final-backup-manifest.json`、`probe-result.json`、`live-result.json` 保存备份、迁移与启动结果。
此文档提交时，新包部署尚待本轮执行结果；实际状态以这些记录为准。

## 待实机验证

- A21 原生入口、曜日/次数显示、升星提示与三个区域共同变化。
- 专用结算两阶段请求顺序、02F9奖励槽语义、02F7第三字段、02FA真实语义。
- 随机对象尾、重访重生、艾丽西亚门、守护者战斗和地图机关。
- 原生组队、断线重登、跨日/月及背包满邮件交付。
- 本候选尚未创建PR，未运行云审核或CI；用户实机通过前不合并。
