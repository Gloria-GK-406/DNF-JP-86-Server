---
kind: rks-spec-result
spec:
  path: ./spec.md
state: complete
---

# 实施结果：移除会话登录快照

## 交付

- `ISelectedSessionRuntimeDirectory` 只保存精确会话身份、发布状态和位置持久化标记；不再保存或替换 `SelectCharacterResult`。
- 新增带连接代际前后校验的只读当前视图查询。后续角色投影从角色、物品、账号、任务与世界领域读取；登录专属恢复和初始化不在查询时重复执行。
- 城镇、组队、副本、任务、角色查看等消费者已迁移。经验加成和队伍血量等局部事实改读对应领域；会话物品租约及退场脏写回从生产生命周期移除。
- 区域名册在每次刷新内按精确会话复用 `AreaState` 查询结果，避免 N 人区域重建约 N² 份完整角色视图；缓存不跨刷新。
- 更新 `AGENTS.md` 与 `Document/SelectedSessionCurrentView.md`，增补耐久、旧连接替换及区域名册查询次数测试。

## 范围和边界

本任务未更改耐久规则、协议布局、数据库或客户端；未合并主干、部署、重启服务或进行客户端实机验收。低层 `EntryInventoryLeaseSnapshot`/持久化接口仍用于对账测试，但不在生产生命周期注册或调用。完整审查结论见 `reviews/change-review.md`，最终验证见 `verification/completion-evidence.md`。

技术债复核：没有新增未解决的结构性缺口或降低约束的兼容运行路径，故无债务 ID 变更。仓库未采用 `docs/plan/` 体系，且项目文档约定存放 `Document/`；未为一张空表引入新的规划目录。客户端实机验收是契约明确排除的后续工作，不记作已完成验证。
