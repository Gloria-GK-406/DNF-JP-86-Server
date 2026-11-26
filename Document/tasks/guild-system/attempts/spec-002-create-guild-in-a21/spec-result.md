---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# A21 创建、会长识别与推荐频道

- A21 原生单人创建支持中文名称与宣传，扣费和会长关系在同一事务内提交。
- 当前角色、旁观详情、重登和换角按持久成员关系投影。公会会长可以设置当前普通频道，频道在 SQLite v15 持久化。
- 用户确认原生频道设置正常，要求继续下一项。[验证记录](verification/completion-evidence.md)

下一项是公会成员名单。名单读取已建立的公会关系，频道从在线会话取值。
