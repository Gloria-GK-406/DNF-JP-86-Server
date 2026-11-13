---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 冒险团按账号派生、通关经验加成与升级推送

## Delivered

- **单一 owner**：冒险团（阿拉德冒险团）由冒险团领域服务按账号从角色等级现算，不落库；PVF `charactermanage.etc` 只经冒险团规则服务读取。
- **发包填值**：
  - USERINFO1 尾部、subtype3 `+3` 写账号的冒险团值；
  - CMD `0x01BA` 按已登录账号回答，选人界面同样有值；
  - 选人列表头 subtype2 保持写 0。
  - 用户实测：选人界面右侧显示冒险团，城内出现冒险团 buff。
- **通关加成**：非账号最高等级角色的通关经验多出冒险团分量，并显示在 NOTI `0x0023` 第 7 格。领主之塔计入、受封顶；死亡之塔、迷妄之塔与击杀经验不加。
- **升级推送**：冒险团升级时推送 NOTI `0x01A0`，策略 LiveOnly，每升一级推一帧。
- **登录动画**：客户端首次进城必播的升级动画，由 ClientHack 新插件 `0210_AdventureLevelUpFix` 消除。该插件在 `DNF-JP-86-ClientHack` 分支 `task/adventure-login-banner`，提交 b41b33d，已部署到 protocol 与 protocol2 两个客户端。
- **数据库**：schema v5 删除 `characters.manage_level`，附 DbMigrate 第 4 步。
- **文档**：迁移说明、四个协议页与 catalog，含客户端静态分析结论。

## Verification

- `verification/completion-evidence.md`

## Repository Result

- 变更的路径：服务端仓 `feature/adventure-group` 相对 Spec baseline 的全部提交，另加 ClientHack 仓 `task/adventure-login-banner` 的 b41b33d。
- Spec baseline：`17eb734d48d4698f466285df002430b19db1f0ae`
- 回滚：不适用
- 是否需要父级回滚：否

## Artifacts

- 审查：`reviews/change-review.md`（含各轮复核）
- 契约评审：`task-contract-review.md`（r5）、`task-contract-review-r6.md`、`task-contract-review-r7.md`
- 用户测试与结果：`Document/tasks/adventure-group/用户测试流程.md`
- 客户端反汇编摘录：`Document/tasks/adventure-group/re-login-banner/`
