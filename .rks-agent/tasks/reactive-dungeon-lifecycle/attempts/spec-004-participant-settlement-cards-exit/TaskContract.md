---
kind: rks-task-contract
revision: 1
state: complete
parent: ../../TaskContract.md
contributes_to:
  - SC-4
  - SC-5
  - SC-6
  - SC-7
depends_on:
  - ../spec-003-shared-clear-projection/spec-result.md
---

# TaskContract: 参与者结算、共享牌面与独立离场

## Context

共享 DungeonRun 已能只开启一次 ClearOpen，但结算与翻牌仍由单连接 Handler 分别推进：结算未强制依赖 ClearOpen，牌面立即发送，翻牌响应只描述本次点击并固定为 seat 0，也没有自动免费翻牌与旧 timer 隔离。回城虽按角色结束 run，仍缺少与牌面计时一致的失效语义。

## Goal

让每名参与者独立结算和领取自己的奖励，同时由共享 DungeonRun 保存全队牌面占用；牌面在结算后延时出现，免费牌支持自动或手动竞争且只发一次，任一成员可独立回城而不影响其余成员继续结算和翻牌。

## Scope

### In

- 结算必须属于已 ClearOpen 的同一 run，并按参与者 exactly-once。
- DungeonRun 保存共享的 free/paid 列占用及参与者 seat；每名参与者每一面最多选择一次，同一列同一面最多归属一个参与者。
- 翻牌提交返回包含完整共享牌面快照和当前 active/settled recipients 的领域 change。
- 协议投影向当前 frozen generation 发送完整牌面；只有领取者接收自己的背包/金币变化。
- 结算后 2 秒打开牌面，牌面打开后 4 秒自动选择首个可用免费列；手动免费翻牌与自动回调共享同一命令与幂等门。
- timer 回调校验 frozen session、run id、参与者仍 active、牌面阶段；离场、replacement connection 或新 run 使旧回调失效。
- EPLP/GiveUp 只结束发起者；其余成员仍能结算、翻牌和回城。

### Out

- 不持久化短生命周期 timer，不实现服务重启后的翻牌 timer 恢复。
- 不实现副本内重连、中途加入或特殊副本自定义牌面。
- 不改变卡牌奖励生成规则与持久化库存事务语义。
- 不合并、部署或清理工作区。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C4-SC-1 | 未 ClearOpen 不可结算；两名参与者各自只能结算一次 | 领域与协议测试 |
| C4-SC-2 | 两人选择不同列后双方看到同一完整 seat 快照，奖励只发给选择者一次 | 领域、codec 和投影测试 |
| C4-SC-3 | 2 秒牌面、再 4 秒自动免费翻牌；手动/自动竞态只成功一次 | 可控 scheduler 测试 |
| C4-SC-4 | 一人离场或连接被替换后不再收到旧局通知，另一人仍能完成流程 | timer、投影与离场测试 |
| C4-SC-5 | 单人剧情和 ordinary 既有结算/回城回归通过 | 定向回归 |
| C4-SC-6 | Release 构建与完整测试无新增失败 | 构建和 TRX 证据 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根任务授权：“其余的可以按你说的处理。你来进行修改重构吧” | 在隔离工作区实现、验证并提交；不含合并、部署或清理 |
