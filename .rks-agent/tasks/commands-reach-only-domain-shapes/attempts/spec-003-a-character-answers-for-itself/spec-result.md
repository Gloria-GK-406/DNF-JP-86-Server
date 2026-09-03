---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 角色自己回答自己的数

## Delivered

- **所有者实机报告的那个缺陷修好了**：升级之后客户端拿到的剩余技能点，从「登录那一刻的陈值」
  变为按新等级重算的值，不必再重登。
- 角色实例能回答自己的等级、经验与剩余 SP/TP，问的时候才算，不持有缓存。
- 剩余点数的算术只有一处实现——上提到拥有档案答案的规则服务上；入场用手边的数据供料，
  角色实例用从表读到的数据供料。**两个供料方，一个算法。**
- 四条发经验帧的路径全部改向角色实例取数，且**四条都有会红的判据守着**。此前只有副本结算
  一条有，且它是靠空引用而非值不对而红。
- `ICharacter.CanUseAsync` 此前从会话镜像读自己的等级来判道具门槛——角色实例从镜像读自己的数，
  正是本轮要禁的形状，且就在实例内部。已改为问自己。
- 归档解析加了缓存。改动前每次击杀发帧用的是缓存镜像，改动后每次击杀都要走一遍档案；缓存放在
  **内容源那一层**——记住一个文件说了什么不违背「问的时候才算」，记住一个角色还剩多少就违背了。

## Success Evidence

| Condition | Evidence |
|---|---|
| C-1 | `verification/completion-evidence.md` C-1：实例成员、无缓存字段、三个输入各自的来源 |
| C-2 | 同上 C-2：镜像读取点只剩入场序列两处且已证明只从入场帧工厂可达；会话等级 11 处各自不陈 |
| C-3 | 同上 C-3：四次变异全部变红，评审独立重做结论一致 |
| C-4 | 同上 C-4：算术 28 行对 28 行零差异；判据增量 +17 全部落在新增用例 |
| C-5 | 判据文件 diff 为空，新服务靠合规命名通过 |
| C-6 | `AGENTS.md` 新增一节并如实写下现在做到了哪一步，评审逐句核过无一句比代码强 |

## Repository Result

- Changed paths: 全部在 `Server/`、`AGENTS.md`、`Docs/` 之下，另含本任务产物目录
- Baseline or accepted result: 基线 `68a121196e270b4d90b9916bc83dc7c819769c11`
- Parent rollback required: no

## Successor Guidance

- **契约在本成果内被收窄过一次，所有者需知悉**：C-2/RQ-003 原写「这三个数」，实测只有剩余
  SP/TP 会变陈——会话里的等级由每一条改变它的路径写回。收窄理由与「这是控制者做出的决定」写在
  契约 revision 2 的修订一节。**若所有者认为等级与经验的取数点也该一并重指，那是一个后续成果**，
  代价是给翻牌、进本、接任务各加一次数据库读，并要重新评估教学升级那处（等级是写入命令的判断
  入参）。
- 修订依据里我曾写下一句错的论断（以为副本结算读会话等级「改了反而错」），被评审纠正后作废，
  错误与订正一并留在契约里。留着是因为：一份支撑范围决定的记载里出现过比代码强的论断，正是这个
  Spec 要禁的那类错误。
- 会话运行态聚合**只拆走了角色自己的那个会变陈的数**。坐标、在场、世界内容、背包投影、账号侧
  状态仍在里面；`ISelectCharacterSessionEntryDomainService.ReplaceRuntimeExact` 仍是对
  `ISelectedSessionRuntimeDirectory` 的逐字转发。聚合远没有拆完。
- 注入面判据的反例仍锚在 `ISelectedSessionRuntimeDirectory` 上。谁拆掉它，谁就要换锚点，
  **而且不许顺手放宽规则**——得另找一个仍被拒绝的真容器。
- 越界策略、`delete_flag` 条件、每次击杀多 3 次 SQLite 读、负数加成校验、自由决斗频道的 SP
  语义——五处小的不一致或已知代价，逐条记在验证证据里。
- 这个仓库把 CS9113（主构造器参数未读）当**错误**：「加了接口只挂着不接线」在这里连编译都过不去。

## Artifacts

- `./TaskContract.md`（revision 2，含修订一节）、`./routing-decision.md`、`./sdd-execution.yaml`
- `./spec.md`（revision 2）、`./plan.md`
- `./implementation-evidence.md`
- `./reviews/change-review.md`（verdict `approved`，两轮）
- `./verification/completion-evidence.md`（decision `pass`）
