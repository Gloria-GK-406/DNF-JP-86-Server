# 变异证据（EV-002）

被变异文件：`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs`
基准 `sha256`：`374063f23db284ef...`（每次变异后从文件级备份还原并核对，四次全部回到该值）
手法：文件级备份 + `sha256sum`。**全程未使用任何 git 还原类命令。**
判据范围：`dotnet test Server/tests/Dfo.Server.Protocol.Tests`（当前 559 通过 / 0 失败）。

| 变异 | 改了什么 | 红几条 | 指名的判据 | 还原 |
|---|---|---:|---|---|
| **M1** | 装扮栏 `=> true` 改 `=> false` | 8 | `EntryWriterContainerShapeTests` 四条（`TheAvatarListCarriesTheBlockWhateverTheItemIs`、`TheAvatarListsBlockIsTwoLengthPrefixedRuns`、`AvatarListEntriesLineUpBackToBack`、`AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList`）＋ `SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes` ＋ 字节锚 | OK |
| **M2** | 装备栏 `=> itemKind == 8` 改 `=> true` | 2 | `TheEquipmentListCarriesTheBlockOnlyForAnAvatar` | OK |
| **M3** | 其余容器 `_ => false` 改 `_ => itemKind == 8` | 6 | **`AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar`** ＋ `EntryWriterAvatarShapeTests` 两条 | OK |
| **M4** | `WireJewelBytes` 由 18 退回 A12 的 30 | 9 | 全部形状判据 ＋ `SelectCharacterPayload…` ＋ 字节锚 | OK |

## M3 是本轮最要紧的一条

上一个成果（`f5c00fb8`）出事的正是这一半——它把「其余容器不带块」这件事**交给了补集**，
没有任何用例正面钉住它。本轮把它写成正面要求（RQ-003），M3 证明它现在会红。

## M2 与 M3 下字节锚都没红，这不是巧合

字节锚的 fixture 里**没有装备栏容器**（入场序列只送 Main/Avatar/PersonalCargo/Pet/AccountCargo），
**主背包里也没有 kind 8 的物品**。所以这两条规则被破坏时，字节锚一声不吭。
**专门的容器形状判据不是冗余，它是这两条规则唯一的看守。**

## M4 说明什么

珠宝槽长度退回 A12 的 30 会红 9 条——但**这九条全部来自本轮新写或本轮更新的断言**。
在本轮之前，30 和 18 对判据是无差别的，对实机也是（自描述长度 + 全零珠宝槽）。
这条长度是靠**读上游 A21 源码**定下来的，不是靠判据或实机发现的。
