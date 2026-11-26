---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system/attempts/spec-005-guild-member-presence/TaskContract.md
contract_revision: 3
reviewer: /root/guild_presence_contract_review
selection_basis: 独立审查者未参与契约建模、意图对齐或路由选择，按本期公会在线状态与隔离验收风险审查
verdict: approved
---

# 公会成员上下线子任务契约审查

## 来源和意图覆盖

- 用户原话为「把公会成员上下线通知接上吧，名单状态也一起实时更新。做好后开两个客户端，我来测试」。范围表纳入原生提示、已打开名单实时更新、完整会话入口及双端验收，AC-1 至 AC-3 可分别验证界面、通知受众与验收准备。用户授予范围内实现和启动双端的权限，实际客户端反馈由用户给出。
- [根契约](../../TaskContract.md)纳入在线投影和换角、重登恢复，要求按来源记录协议证据并隔离验收。[分期图](../../multi-spec-map.md)记录 SC-01 的身份投影、SC-02 的名单和已封存的 spec-003。[spec-003 结果](../spec-003-view-guild-roster/spec-result.md)记载基础名册及在线频道取自当前会话，并记录用户确认同区域亮色显示。
- [功能开发流程](../../../../FeatureDevelopment.md)要求区分 A21 静态、线路和实机结果，并等待用户实测反馈。契约中的静态结论支持后续编写 Spec，用户尚未验收本期。

## 第 1 版审查记录

1. `depends_on: [spec-003]` 指向已封存并集成的基础名单结果，且条目不是 `TaskContract.md` 相对路径。按 `multi-spec-workflow` 的依赖验证规则，该字段只放待解决的外部前置契约。改成 `depends_on: []`，在正文引用 spec-003 的已交付结果。
2. `contributes_to` 漏掉本期直接覆盖的 SC-01 和 SC-02。AC-1 更新既有名册的身份展示，AC-2 验证重登时的在线投影，均应映射到根条件。若保留 SC-04，写出本期对频道相关状态的具体贡献，根条件里的聊天、邀请等仍由后续阶段验收。
3. 「其他社交功能和发布」合并为 `preserved_upstream`，与根契约的授权范围冲突。社交功能由根契约和分期图承接，在该行引用具体所有者。发布由根契约明确排除，单独标为 `not_applicable`。

## 第 2 版审查记录

第 2 版删除了已封存依赖，补齐了 SC-01、SC-02 的归属及 SC-04 的具体贡献，还明确了原地更新和用户双端反馈的通过条件。范围表仍将发布写入 `preserved_upstream`，第 1 版第 3 项继续阻断。

## 第 3 版复审结论

其他社交功能由根契约和分期图承接。发布及线上变更标为 `not_applicable`，与根契约明确排除的范围以及本轮隔离验收授权一致。第 3 版的意图、范围、验收和授权一致，准许进入子任务路由。A21 实机验收依照 AC-1 等待用户双端测试反馈。

## 五维评估

| 维度 | 证据 | 依据 | 后果 |
| --- | --- | --- | --- |
| quality | observed | 对读第 3 版范围表、AC-1 至 AC-3、根契约、分期图与子任务依赖规则 | 可分别验证提示、名单、受众和重登，发布及线上变更的边界明确 |
| efficiency | proxy | 已交付的名单和在线频道投影，本期一个可独立验收的结果 | 可在现有基础上验证实时状态，预计无需等待其他公会社交功能 |
| cost | proxy | 验收要求为一套已有隔离环境和两个 A21 客户端 | 预期复用上一期测试数据，执行耗时尚未测量 |
| cognitive burden | proxy | 同一角色动作同时对应提示和名单变化，AC-2 单独核对受众及旧会话清理 | 用户可以用双端验证界面，自动化覆盖不易稳定复现的场景 |
| actual human participation | observed | 用户本轮指定功能和双端测试，分期结果记录用户已验证上一期名单亮色 | 用户已经决定本期范围和测试安排，本期结果待其实际操作确认 |
