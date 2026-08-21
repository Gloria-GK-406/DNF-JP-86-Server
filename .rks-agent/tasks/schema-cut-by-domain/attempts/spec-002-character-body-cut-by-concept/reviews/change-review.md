# Change Review — spec-002 角色本体按概念重切

被评审的完整改动：基线 `9c535a9715ebd142fd197026c80d711a3036abcc` 之上的未提交工作区，30 个已跟踪文件（332 增 790 删）加两个新文件。
验收依据：本 attempt 的 `TaskContract.md`、`spec.md`、`plan.md`，以及设计权威 `Docs/Refactor/TableOwnershipRuling.md`。

评审在一个独立的新上下文中进行，只拿到改动、验收边界与代码。轮次按用户的校准收到一轮：实施层有全量套件与整记录表征守卫兜底，设计层那种十轮深度在这里不划算。

## 判定

`changes_required`，一条 A 级。

## A 级发现

**`character_option_blob` 的「没有」被读成了「空的」，每个角色每次选人的下发帧少四个字节。**

`EntryBlob.Bounded(reader, ..., allowNull: true)` 遇 NULL 返回**空数组**；被它取代的 `EntryBlob.Optional` 遇 NULL 返回 **null**。而下游正是靠这个区分决定发什么：

```csharp
public static byte[] EncodeCharacterOption(byte[]? option)
{
    if (option is not null) return [.. option];
    return new byte[4];
}
```

建角色的 `INSERT INTO character_settings (character_id)` 不指名这一列，所以**每一个角色**它都是 NULL。改动前该帧体是四个零字节，改动后是零字节。正面违反 SC-8 与 RQ-006「选人下发帧逐字节相同」。

全量为什么没红：三份表征用例都播了非空 blob，协议侧用内存态断言；NULL 这条路被多个用例走过，但**没有一条断言它**。评审顺带指出，被破坏的正是那条名为 `HighRiskAvailabilityDistinctionsAreNotZeroFilled` 的协议用例所命名的区分。

同一行还把上限从 `EntryBlob.Option`（64KB）悄悄收成了裸字面量 16384——那是 appearance 的上限，像是从上面一行抄下来的。它是全仓唯一一处不用具名常量的 `Bounded` 调用。

## 整改

- 改回 `EntryBlob.Optional(reader, 0, EntryBlob.Option, "character option")`，两个问题一并消失，并在该行留下为何不能用 `Bounded` 的理由。
- **补上让它漏过去的那个缺口**：新增 `CharacterBodyCreationTests`，15 条断言，覆盖评审指出的两个证据空洞（AC-004 与 AC-005 此前没有任何用例）——建一个角色后六张表各有一行；八个非零默认值逐一读回比对；以及那个刚出问题的区分：一个刚建好的角色，其选项块必须是「没有」而不是「空的」。
- 该守卫已验可证伪：把 `Optional` 换回 `Bounded` 重跑，立刻红一条；换回来即绿。

## B 级发现（记录，未改）

评审列了七条不影响正确性的观察，全部保留：

1. `ReadPcRoomStatesAsync` 丢了两条具名拒绝（`IsDBNull` 与 `[0,255]` 范围检查）。净效果仍是失败，只是异常类型从 `InvalidDataException` 变成 `InvalidCastException` / `OverflowException`；建角色永远写 0，实机不可构造。
2. 孤儿领取守卫成了死代码——合并后单表单主键，孤儿领取结构上不可表示。测试把 `orphan-claim` 换成 `group-out-of-range` 经评审判定**等价**：旧用例已不可构造，新用例覆盖了一条此前没有用例的守卫，覆盖没有净减少。类顶部注释仍把孤儿领取列为判断之一，已过时。
3. 八个非零默认值此前无用例守护——**整改中已补上**（见上）。
4. AC-004 / AC-005 无用例——**整改中已补上**。EV-004 要求的下发帧字节比对仍以全量加整记录表征代替。
5. `expert_job_exp` 的宽度注释说「和一直以来一样」，实际新代码用 `checked` 比旧的 `GetInt32` 更严，不是等价。不可达，但注释说错了它保住的是什么。
6. 文档漂移：库形状声明的类注释仍在讲一张已不存在的表的历史；`Docs/Refactor/CharacterEntryStateCoverage.md` 等仍写旧表名。契约未要求同步。
7. `SqliteEntryLifecycleRecoveryTests` 手搭的 `character_pvp` 没带级联外键，被测表不是出厂形状。不影响该用例断言的内容。

## 评审核实无误的部分

- 六张目标表的列集与裁定的「Target shape」**逐列一致**（28/30/48/8/3/7），无多无缺；表数 53 → 49。
- 两个组合函数 `Subtype0(...)` 45 个参数、`Subtype1(...)` 33 个参数**逐位对照记录定义全部正确**；每一个跨表取值都落位正确。
- 五处 SELECT→记录映射逐序号核对，含 Int32/Int64 宽度，与被删的读取器逐列同宽。
- 表征用例把六张表的每一列都播了不同值，且断言覆盖到 subtype0/subtype1 的每一个字段——评审**没有找到「播不到因而错位不会变红」的列**。A-1 不是列错位，是同一列的空值语义。
- 建角色确实写六张表，`character_condition` 显式给 `pc_room_state=0`。
- `character_pvp` 带上了级联外键；九张源表的键/唯一性/检查/外键逐条追到落点均存续。
- 可补列清单修剪正确（297 → 206 行）；六条索引全部仍指向存在的表。
- 裁定的改动只是追加 M-5 关于 claim 语义的一段，符合契约「先更新裁定」的程序。

## 结论

`passed`，无未解决的阻塞性发现。一条 A 级已修，并补上了让它漏过去的证据缺口；七条 B 级随记录交出。交付物可进入新鲜验证。
