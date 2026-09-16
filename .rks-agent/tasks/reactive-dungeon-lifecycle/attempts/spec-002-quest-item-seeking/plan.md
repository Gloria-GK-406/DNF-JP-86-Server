---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 任务物品与 seeking 闭环

## Approach

- 扩展 Quest 内容模型和 PVF 投影，先以纯解析测试和真实 1779 内容测试钉住七元组。
- 新建任务怪物掉落规则服务，由 `MonsterDropService` 将其结果与普通掉落合并后一次编号；规则只接受 QuestLinked 的冻结任务和队长。
- 给任务掉落增加领域 owner，并让地面查询与拾取命令强制归属；MonsterDeath codec 按每条掉落解析 wire owner。
- 抽出可复用的 `SeekingQuestProgressDomainService`，替代角色进入路径中的重复算法，并在任务物品成功入包后、共享地面 commit 前执行。
- 扩展拾取结果携带进度快照，由现有拾取投影向当前 picker 追加完整 carried quest progress；持久化 quest log 是真源，entry runtime 不保存第二份可变任务进度。
- 将共享地面消耗作为 commit-infallible transaction participant 延迟提交，确保 SQLite 是唯一可失败 commit；协议投影只读取事务已提交后的 change。

## Test-first Order

1. RED：真实 PVF 1779 和七元组合法性测试。
2. GREEN：内容模型与解析。
3. RED：QuestLinked/ordinary/范围/持有量任务掉落规则测试。
4. GREEN：规则服务、owner 与 MonsterDrop 合并编号。
5. RED：非 owner 拒绝、15→14→0、失败不 commit、运行期投影测试。
6. GREEN：seeking 服务、命令事务顺序与协议投影。
7. 定向测试、Release 构建、完整测试基线比较、独立审查。
