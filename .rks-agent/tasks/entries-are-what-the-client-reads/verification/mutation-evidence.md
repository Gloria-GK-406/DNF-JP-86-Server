# 变异证据（EV-002）

被变异的文件：`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs`
基准 `sha256`：`83468f8d57bbf94327a137e0e6b9bea09ae910d083c421ad15a7d13cb91778c2`

手法：**文件级备份 + `sha256sum` 还原并核散列**，每次变异用完立刻还原。
**全程未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）。
下表是在 review 阶段的整改**之后**重做的一遍——生产文件的注释在整改中改过，散列随之变化，
所以旧记录不能沿用。

判据范围：`dotnet test Server/tests/Dfo.Server.Protocol.Tests`（当前 546 通过 / 0 失败）。

| 变异 | 拿掉/破坏了什么 | 红了几条 | 指名了哪些判据 | 还原核对 |
|---|---|---:|---|---|
| **M1** | `+0x2F` 的夹紧（`WrittenOptionCount(...)` → 原值） | 7 | `EntryWriterOptionCountTests.TheOptionCountNeverExceedsTheOptionsTheWriterActuallyWrites`、`…NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire` | OK |
| **M2** | `+0x1A` 的上界（改写常量 `3`） | 8 | `EntryWriterOptionCountTests.TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem`、字节锚两条、`InventoryDeltaLiftTests.The_entry_paths_delta_frame_is_unchanged_by_the_lift` | OK |
| **M3** | 把 42 字节尾块加回来 | 4 | `EntryWriterAvatarShapeTests.AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry`、`…AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry`、`SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、字节锚 | OK |
| **M4** | 「非时装携带时装数据」的拒绝 | 1 | `EntryWriterAvatarShapeTests.AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts` | OK |
| **M5** | 「珠宝槽必须恰好 30 字节」的拒绝 | 2 | `EntryWriterAvatarShapeTests.AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused` | OK |

## M1 是本轮最要紧的一条

移除夹紧后，**字节锚 62 帧一帧不差、全套只有 `EntryWriterOptionCountTests` 变红**。
这是实测，用来钉住一件事：`+0x2F` 那条路径在本轮之前**没有任何既有用例走到**，
所以「全量判据全绿」对它什么都证明不了。

**字节锚不变的原因**（生产侧探针实测）：**判据套件里，到达本编码器的每一件物品，
`EmblemSocketCount` 都是 0**。生产环境不然——那正是夹紧存在的理由。手法是在 `Write` 开头插一句「拿到非零值就抛」，跑全量 → 红 9 条，全部在
`EntryWriterOptionCountTests` 里，其余每个项目全绿。夹紧对既有判据全是空操作。

这一条的说法被改错过两次（初版写「没有 fixture 设过该字段」，第一次整改又写成「第七处
会流经」），都由独立评审发现。经过与错法记在 `../behavior-change-list.md` 与 `../spec.md`。

## M1 顺带证明的一件事

`NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire` 穷举 `0..255`。它在 M1 下变红，
所以**不是恒真断言**。
