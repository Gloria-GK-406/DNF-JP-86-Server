---
kind: rks-spec-result
contract:
  path: ./TaskContract.md
  revision: 1
spec:
  path: ./spec.md
outcome: completed
---

# Spec Result: 装载侧的重复指名拒绝，与三处「没有判据撑着」的取值校验的覆盖

## 结果

四条缺口全部闭合，正常数据上的行为逐值不变。

1. **装载侧补上了与保存侧对称的拒绝。** `InventoryEntryAssembly` 新增 `RequireOneNameEach`，在角色与账号货柜两条入场读取到行之后各调用一次，按**世界行身份**（`core_uid`）判定，因而对堆叠物与具名物一视同仁。
2. **三条取值校验各自获得会红的判据**：`GameInventoryItemEntry.Write` 的「非时装带细节」与「珠宝槽恰好 30 字节」，以及 `InventoryEntryAssembly.WithAvatarDetail` 的畸形细节三个分支。
3. **「时装六值落到新建的世界行上」获得正向覆盖。** 原先那条判据靠「行没被删」通过；新判据走真正的重建路径，并以「新行 `item_uid` ≠ 原行」为前提断言。
4. **AGENTS.md 按实况更新。**

## 判据

退出码 **0**，**2250 通过 / 2 跳过 / 0 失败**（HEAD `25dccec6` 基线 2237 / 2 / 0，新增 13 条）。跳过的两条与基线同两条，是 PVF 档案确实加载的标志。逐项读数与加法见 `verification/completion-evidence.md`。

字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 与 `CommandHandlerArchitectureTests.cs` 的 SHA-256 与 HEAD 逐字节相同。

## 每一条新判据都经过变异核实

这是本成果的验收口径：**判据是新的，被钉住的行为多数是旧的**，所以「它会不会红」只能靠把生产校验拿掉来证。五处变异（两处由执行方做、四处由变更评审独立重做）逐一确认对应判据变红、还原后散列一致。其中最要紧的一处：令 `CarriedAvatarsAsync` 恒答空表，HEAD 上只红 1 条，现在红 2 条，而改名后的那条**仍绿**——这正是「那六个值零覆盖」的当场证据，也是本成果存在的理由。

## 对派工前提的两处更正

派工给的两个代码锚点与实际不符，已实测更正并经所有者独立复核确认：`RequireUniqueSlots` 确在两条装载路径上（`:75`、`:103`），故 AGENTS.md 原句在「入场读」粒度上是准确的、未作更正；保存侧真正的拒绝在 `:224-229` 而非 `:145`。后者是承重的——它决定了装载侧必须数世界行而不是数 `ItemIdentity`。

## 未解决的风险

五条，逐条记在 `verification/completion-evidence.md` 的「未解决的风险」一节：称号书盲区、`SqliteWorldItemTable` 既有缺口、一条理由写在两处的漂移点、`Docs/Refactor/TableOwnershipRuling.md` 里改名前的判据名（有意不动，依据已记）、spec 的 AC-013 措辞瑕疵。
