# 循环地下城单人阶段收尾（2026-10-10）

## 验收结论

用户在 `7aad7fe6` 部署后确认“播报符合预期”，随后确认单人循环主线已跑完一圈，并授权更新协议与 GitHub issue。
将 **1123 已测试场景下的单人正常流程** 作为已接受阶段：立即入场、自动接任务、难度选择、房间战斗与移动、Boss 通关、掉落/任务奖励、结算翻牌及再次挑战。
这是用户整体接受的范围；不等于每个区域、每项奖励数量、所有失败分支均已做独立差分实验。
其中经验播报有本次明确的正向反馈；其余环节结合此前持续测试与本次阶段接受记录，不编造新的原始包或数值读数。

协议流见 [循环入口与重试](../protocol/patterns/flow-cycle-dungeon.md)，实现演化见 [单人闭环](CycleDungeon-20261009.md)。

## 已交付范围

| 环节 | 当前行为 | 证据边界 |
|---|---|---|
| 选图与准备 | 客户端从当前区域 PVF 池选择地图/任务，CMD0308 只绑定准备意图 | WireCaptured、ClientStatic、CurrentImplementation；本轮单人流程接受 |
| 自动接取及入场 | 客户端自行接任务并发 CMD0010；服务端按 Cycle 意图分流 | CurrentImplementation；本轮单人流程接受 |
| 难度 | 尊重请求值，在该地图已解锁且支持的档位中取最近值，同距离取低 | 自动回归；未将多人不同权限矩阵升级为实机通过 |
| 房间与怪物 | typed token 解析；保留敌对 APC/Boss、dummy、对象模板身份；合法空物件掉落不阻塞房间创建 | 实际 PVF 解析与专项回归；本轮主线整体接受，未宣称全部脚本/波次已运行 |
| 通关与奖励 | 敌对 Boss 决定通关；循环任务消费与奖励同事务，任务可重复 | CurrentImplementation、自动回归；本轮单人流程接受 |
| 击杀与结算经验 | named 独立 ×3、与类别倍率叠乘；个人账本记录已发奖励；NOTI0023 逐怪列表只展示 | ClientStatic、411 项相关回归；用户确认当前播报符合预期 |
| 再次挑战 | 000F 旧目标可由同区域合法 0308 重新绑定；旧任务由客户端取消 | CurrentImplementation、回归；本轮单人流程接受 |

## 运行与回归身份

- 分支：`task/monster-declaration-parser`；功能提交：`7aad7fe691b27f1966c57325ef02bc4f86651c52`。
- 已部署版本：`0.2.3+named-monster-experience.20261010.7aad7fe6`。
- 最近部署核验：PID 33564，六个 TCP 端口、health/bootstrap ready；这是部署时快照，不充当后续实时状态。
- 构建零警告/错误；最新相关测试 Application 208、Protocol 179、Infrastructure 24，合计 411 通过、无失败/跳过，包含实际 PVF。
- 测试、程序集哈希、数据库备份和回滚目录：主仓 `artifacts/named-monster-experience-20261010/deployment.json` 及 `tests/*.trx`。
- 本次只更新文档、GitHub 状态；不重启、不改玩家数据、不升级版本、不执行 PR/合并或分支清理。

## 保留的未验收范围

- 组队自动接任务、共同难度、逐成员拒绝、离队及并发边界。
- 其他区域候选池和全部特殊怪物/条件脚本的实际运行。
- 满包、疲劳不足、材料不足、断线重进、重启恢复等完整异常矩阵；已有自动保护不替代实机证明。
- 客户端逐怪经验字典仅用 sequence；跨房同序号后写覆盖。服务端内部仍按 Cell+sequence 隔离，不会合并发奖。
- `0048` 的“下一个地下城”或关联地图续关与循环“再次挑战”不同，仍由 #119 跟踪。

## GitHub 收口范围

- [#68 循环地下城](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/68)：单人主线已接受；原始完成条件包含多人，继续 OPEN，剩余验收列为未完成。
- [#65 通用地下城运行时](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/65)：同步怪物身份、清场、合法空掉落和逐怪经验进展，保留完整运行时边界。
- [#104 普通地下城验收](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/104)：说明本次覆盖循环单人，不能据此关闭普通/组队全矩阵。
- [#119 下一个地下城](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/119)：排除已修复的循环重试，保留原续关问题。
- [#124 掉落重构](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/124)：补充合法空物件掉落修复；不关闭个人隔离、概率及性能等未完成范围。

功能代码和本次文档尚在本地工作分支；issue 更新不表示已 PR 合入 main。
