# 升级券：参考与实现（2026-10-07）

## 故障与内容

用户角色 1123 使用升级券，服务端收到 CMD01A2 `05 00 00`，但旧实现没有该请求注册，未扣券、未升级。运行日志与响应结构见[协议记录](../protocol/game/client-to-server/0x01A2.md)。

当前 PVF 中物品 10006124 是“升级券（Lv1～85）”：`[action type] [level up ticket]`、minimum 1、maximum 85、`[levelup range] 1`、cooldown 1000ms，禁止 `hardcore mode`。这些是 ContentStatic；不修改 PVF，也不硬编码该物品 ID。

## 70Reference 与上游

- 70 ELF `Research/1995chen-dnf/other/等级补丁/70/df_game_r`：`CUser::increase_status(short)`（VA 086657FC）调用 `CalLevelUpItemCheck`（08689D06）、`CalLevelUpItemState`（08689D74）。检查硬编码物品和等级区间，计算“下一等级累计经验起点－当前累计经验”，达到下一等级起点。没有从 enum 名称推断存在 A21 同名 dispatcher；这些是静态参考，不是 A21 实包证据。
- 上游 `ServerS4A21/Server/DfoServer/Network/Handlers/InventoryHandler.Experience.cs` 的 `Handle_REQUEST_EVENT_SERVER_LEVEL_UP` 解析背包槽，调用 `Game/Inventory/ExperienceItemUseService.cs` 的 `UseLevelUpTicketBySlot`：检查真实券、等级/期限/上限，消耗 1 张券，升 1 级，补 SP、免费技能，清理旧主线，提交后同步背包/角色/经验/任务。
- 上游业务成功 ACK 为 `00 00`。首次实现改为本体 12B 非零结果分支，实测升级后却弹出旧“免费转移角色”窗口；补追确认该分支在等级 >=60 时固定显示旧活动 UI。当前改回上游的静默结束回执 `00 00`：不弹窗，不在本地扣券，状态以后续 NOTI 为准。静态路径及定位见协议记录，修正效果待实机验收。70 取证可复现脚本为 `abyss-research-20261007/evidence/70-inspect.py`。

## 当前实现策略

1. CMD 只负责协议校验和调用命令；领域服务获取真实角色、背包与任务事实，规则服务读取 PVF 内容。
2. 目标等级为 `min(当前等级 + PVF levelup range, 服务端上限 86)`，经验设置到该等级累计经验起点；满级、超出券使用范围、极限模式限制、期限/冷却或无效物品均拒绝且不扣券。
3. 扣券、冷却、任务标记/接受列表/追踪清理、等级及免费技能协调使用统一 effect 与同一 SQLite 事务。失败整体回滚，不通过普通经验奖励产生溢出胶囊。
4. 旧故事清理限定：有效 Epic 主线、`epic_N_*.qst` 故事阶段 N 小于目标等级、最低等级也已满足、符合职业/转职要求；排除隐藏、活动、未识别阶段及已完成任务。不发任务金币、道具、额外 SP/TP，不执行任务的转职、觉醒或深渊资格奖励处理。
5. 同一区域多个故事阶段可能共享 minimum 17，而故事阶段为 17/19；使用独立 `StoryStageLevel` 保留 19 级阶段，避免升到 18 就过度清理。
6. 单级券阶段边界与上游一致。当前复用现有任务追赶券的直接标记策略，不逐个模拟领取/对话/前置分支；多级券清理目标等级之前的阶段。此处是本仓策略，不能宣称完全复刻上游。PVF 文案称“外传”，上游筛选的是 Epic，本实现同样没有扩大到 Side 等其他任务等级。
7. 成功提交后静默 CMD ACK 在绝对背包更新之前发送，随后投影经验/点数、任务和技能。失败只回拒绝，未提交时不发成功或等级状态。公共 effect 重放更新当前状态，不重复扣券或升级。

## 验证与边界

- 完整解决方案构建：0 警告、0 错误。
- Application 相关回归：111 通过、2 因未提供 PVF 跳过。其中新增升级券 8 例全部通过，包含真实命令/持久背包扣 1 张、等级写入失败恢复券与任务、任务写入失败、满级/极限模式拒绝、跨共享 minimum 的阶段保护、多级范围截断和无旧任务仍可升级。
- Protocol 相关回归：142 通过、0 跳过；其中升级券 9 例包括实际请求注册、真实帧编码、2B 静默成功/2B 拒绝、回执后源槽清空和经验同步顺序，以及短包/非法槽/不支持尾值在进入领域前拒绝。
- Infrastructure 内容回归使用 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`：34 通过、1 跳过。新增两项真实升级券声明和故事阶段测试均执行并通过；跳过项不属于新增功能。
- Host 组合架构检查：1 通过。

首次版本已部署，服务器记录角色 2 多次升级成功，用户反馈伴随旧活动弹窗。当前修正只变更回执与回归，不修改客户端或恢复玩家测试数据；静默响应为 ClientStatic，修正后 UI、背包、技能、任务和拒绝效果仍需人工验收。
