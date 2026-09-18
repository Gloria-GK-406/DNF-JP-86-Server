# Story TASK-01 裁决

## 可确认内容

- **Confirmed static**：`PartyCategory.Story=0`；Story 专用 `0x000C` builder 使用既有 12-byte/inline-title
  形状，容量走共用规则，target 从当前剧情状态查询后以 `u16` 写入。
- **Confirmed static**：收到的 Story target 存入 party object `+0x64`，被剧情记录查找、当前目标比较与
  进度显示消费；不是 Normal 的固定 0，也没有证据把它当普通 dungeon ID。
- **Confirmed static**：在 category 0 的队伍中，委任队长 UI 显示 dstr 48613 并跳过 C2S `0x0079`；
  非 Story 分支才构造 `0x0079`。
- **Confirmed static**：Story target 存在通知/剧情变化触发的客户端自动重建路径，但触发门的完整业务
  语义仍未知。
- **High**：`ModeTargetRaw` 可作为剧情任务/场景条目 ID 使用；精确资料表名尚未确认，公共 API/持久化
  仍应保留 wire 名 `ModeTargetRaw`。

## 不可实施为硬规则的内容

- 不能把 `0` 或 `0xFFFF` 任一值宣称为 Story wire default：UI 中 `0` 同时是内部刷新门，负值会阻止
  通用提交，但当前 A21 尚无原生 Story create/edit 帧。
- 不能只凭 `>0` 分支推导合法值域；source 是 `i32`，wire 截断为 `u16`，且 0 路径没有被同一检查拒绝。
- 不能解释 raw `[3..6]`/`[7]` 的业务名，也不能在服务端重算或归零；它们与正 target 联动但消费者未闭合。
- 不能从通用“非 Normal 模式不匹配”错误推导 Story dungeon admission predicate。
- 没有静态调用链把 Story 基本设置绑定到公共目录 `0x023C` 或快速匹配 `0x01BE`；两者继续排除。

## 对后续执行的裁决

TASK-01 的静态/上游证据与安全捕获方案已完成，但 Spec AC-001 仍要求至少一份当前 A21 原生 Story 创建
帧。现有服务端已经 byte-exact 保存 category 0 和 opaque target；在真实 create/edit 取样前继续收窄
`PartyModeRuleService` 反而可能拒绝合法客户端。因此：

1. 不物化 T-002，不修改生产代码；
2. Story 子规格以“缺当前 A21 原生 Story create/edit + 特有操作归因”为外部输入 blocker 返回父控制器；
3. 父 Multi Spec 可继续独立的 Cycle 子规格，Story 证据保持可恢复；
4. 取得样本后，新 attempt 必须先裁决 target/default 与 raw `[3..7]`，再进入 TDD。
