# Resume point

写于用户要求在可恢复处停下的时刻。**这不是终态记录**，只是让下一次接手不必重新推断现场。

## 现在在哪

| 事实 | 值 |
|---|---|
| 工作树 | `D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots` |
| 分支 | `domain-instances-and-snapshots` |
| HEAD | `078d762`（spec-002 的已接受结果） |
| 源分支 main | `531c0bb`，全程未变 |
| 未提交改动 | 18 个路径，全部属于 spec-003 |
| 构建 | 零错误零警告 |
| 全量 | **1173 通过 / 2 跳过 / 0 失败** |

三个子成果里两个已接受：

- `spec-001` 物品规则收回背包 → `7478e2e`
- `spec-002` 人物成为可操作的实例 → `078d762`
- `spec-003` 快照改为领域形状 → **未提交，评审判 `changes_required`**

## spec-003 的确切状态

实施已完成并全绿；第一轮独立评审判 `changes_required`，记录在
`attempts/spec-003-the-snapshot-in-domain-shape/reviews/change-review.md`。
`sdd-execution.yaml` 的游标停在 `review`。

**评审的 A 级已在停下之前修掉**：`CharacterEntryAssembly.RequireProfileInRange` 少了四条宽度拒绝
（`ManageLevel`、`NameTagItemId`、`NameTagExpireTime`、`GuildPowerWar`）——它们是随字段迁出扁平记录
而失去挂靠点的那四条。已补回并使其可从该方法的参数抵达，修后全量仍 1173/2/0。

## 第一轮评审的整改：已全部完成

| # | 事项 | 结果 |
|---|---|---|
| 1 | 补「超宽的行在装载时仍被拒绝」的证据 | `CharacterEntryState_RefusesAValueTooWideForItsFieldWhenItIsLoaded`，七行，含刚补回的那四个字段；演示 S5 拿掉四条拒绝后恰好四行变红 |
| 2 | 收掉三个零引用记录类型 | `CharacterDimensionEntryState`、`CharacterDimensionFlagsState`、`CharacterPvpResultState` 已删，删前逐个证明全仓零引用 |
| 3 | profile 路径上仍在 Application 的四处收窄 | **第二轮评审推翻了「不能下沉」这个理由并实测证明三处可去**（A-2）。三处已去：两处可见位改为 `long` 到写字节处收窄，生物等级那处的 `checked((byte)…)` 本来就是多余的（目标声明为 `int`）。第四处（名录的 manage level，目标确为 `byte`）保留。profile 投影现在**零收窄**，该文件 93→27 |
| 4 | 新守卫覆盖不到 NameTag 那一对 | 夹具改为 77/88 可区分；演示 S6 对调两者后守卫变红 |
| 5 | 契约措辞与实际做法不符 | 契约推进到 revision 2：Scope/In 由「移入协议工程」改为「删除」，并写明理由（那两个记录的全部内容就是摆序与收窄，而两者都已进入写字节那一行） |

另：第一轮的 A 级（`RequireProfileInRange` 少了四条宽度拒绝）已在暂停前修回。

第二轮评审判 `changes_required`，唯一阻塞是上面第 3 行的 A-2，已修。另处理：新用例改为断言
究竟是两条拒绝里的哪一条（它们措辞不同，检查搬错半边仍会拒绝但说错原因）；`spec.md` 的
`contract_revision` 与 RQ-2 已按实际做法订正（入场侧与存储侧共用同一族记录，0/非 0 留作整数、
在写字节处变成 0 或 1 的字节，代价是八个字段由 `bool` 退回 `int`）。

当前 **1180 通过 / 2 跳过 / 0 失败**，构建零警告。

## 下一步

1. 第二轮独立评审，确认整改并检查整改自身是否引入新问题。
2. 通过后写 `verification/completion-evidence.md` 与 `spec-result.md`。
3. 由父控制器做 spec-003 的**唯一一次**已接受结果提交。
4. 更新 `multi-spec-map.md`：child-accepted、推进基线、重算 SC-1 与 SC-6。
5. 根层评审与新鲜验证，封存根记录。实机走查仍未进行。

## 根层还剩什么

根契约十条成功条件里，SC-2/3/4/5 已满足；SC-1、SC-6 待 spec-003 接受后才能判；
SC-7/8/9/10 为 in_progress。根层的实机走查尚未进行。

## 注意事项

- 全程不产生任务级提交；只在评审与新鲜验证通过后由父控制器做每个子成果的唯一一次提交。
- 合并、推送、PR、分支或工作树清理**未获授权**。
- 测试须 `-m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`。
- 已知假红：`Dfo.Server.Host.Tests` 满载下撞 10 秒预算，判据是隔离复跑。
- `RosterScreenQueryCodecTests.cs` 带 BOM 是**改动前就有的**（全仓 10 个文件如此），已原样保留。
