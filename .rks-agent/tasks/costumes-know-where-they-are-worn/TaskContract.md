---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 时装知道自己穿在哪一格

## Context

### Source Request

所有者原话：

> 直接做吧，做完以后我们再一起测试

这是 **issue #11**。前一件（#10，`80bdb144`）把装扮栏接进了 `MoveItemSpace`，
时装因此能拖动、能走到 `PutOnAsync`，但**仍被拒为 `NotWearable`**——
因为 `PvfEquipmentSlots.ByName` 里没有任何时装的键，档案说不出时装该进哪个槽。
本件补上那些键。

issue #11 的接受条件（原文）：

> - 0..11 的映射逐件量到，每条附上量到它的那行日志
> - `PvfEquipmentSlots.ByName` 补上时装的键，来源标明是**实机量得**而不是档案读出
> - 时装能真的穿上，且判据钉住「错误的槽被拒绝」

**第一条已经在上一件里做完了**（见下）。

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，分支 `main`，HEAD `80bdb144`（工作区干净）。
判据命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
基线（**合并后在 `80bdb144` 实测**）：**2335 通过 / 2 跳过 / 0 失败**，退出码 0。
逐项目：Pvf 60、Networking 52、Protocol 582、Persistence 329、Infrastructure 236(+2 跳过)、Host 6、Application 1070。

**跑判据前必须确认没有 `Dfo.Server.Host` 进程在跑**——它锁 `Dfo.Server.Protocol.dll`，
构建以 MSB3027 失败，看着像判据坏了其实不是。

既知偶发失败，按「异常类型 + 栈根 + 复跑即绿」判别，**不按用例名**：
`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根
`CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`。
**建契约前遇到过一次**（Application 套件 1 红，单独复跑 1070 全绿）；
**当时没抓到栈根**，只有「复跑即绿」这一条依据，如实记。

#### 根因

`WornSlotForAsync` → `ItemPlacementRuleService` → `PvfEquipmentSlotDefinitionSource`：
读档案的 `[equipment type]` 字符串，拿它查 `PvfEquipmentSlots.ByName`，查不到返回 `null`，
`PutOnAsync` 于是拒为 `NotWearable`。

**`ByName` 里一个以 `avatar]` 结尾的键都没有**（本轮实扫零命中），它从 `["[weapon]"] = 12` 开始。
而**表自己的注释早就写着**「Zero through eleven are the avatar slots, so the worn pieces begin
at twelve」——知道有这 12 格，只是从没填。

#### 映射：三个独立来源互相印证

**来源一：实机量得（2026-09-07，所有者操作）。** 11 件骑士时装放进主背包后逐件拖到装扮槽，
服务器逐条记下 `Move item space … item {ItemId} {From}->{To} refused as NotWearable`。

**来源二：档案读出（2026-09-08，本轮探针）。** 逐件读它自己的 `[equipment type]`。

**来源三：上游 `ServerS4A21` 的 `Game/ItemUpgrade/EquipmentType.cs` 枚举**（本轮读出）。

| 槽 | 实机量到的 itemId | 档案的 `[equipment type]` | 上游枚举名 |
|---:|---|---|---|
| 0 | 413550000 | `[hat avatar]` | `HatAvatar = 0` |
| 1 | 413560000 | `[hair avatar]` | `HairAvatar = 1` |
| 2 | 413570000 | `[face avatar]` | `FaceAvatar = 2` |
| 3 | 413500000 | `[coat avatar]` | `CoatAvatar = 3` |
| 4 | 413510000 | `[pants avatar]` | `PantsAvatar = 4` |
| 5 | 413540000 | `[shoes avatar]` | `ShoesAvatar = 5` |
| 6 | 413520000 | `[breast avatar]` | `BreastAvatar = 6` |
| 7 | 413530000 | `[waist avatar]` | `WaistAvatar = 7` |
| 8 | 413580000 | `[skin avatar]` | `SkinAvatar = 8` |
| 9 | 413590000 | `[aurora avatar]` | `AuroraAvatar = 9` |
| 10 | 413600000 | `[weapon avatar]` | `WeaponAvatar = 10` |

**三列逐行吻合，无一处冲突。** 上衣 `413500000 → 3` 另有两次更早的独立观测（主背包 `6->3`、
装扮栏 `0->3`），合计**三次不同源格、同一目标槽**。

#### 槽 11：上游有名字，本档案里没有东西

上游枚举有 `AuraSkinAvatar = 11`（别名 `AuroraIllusionAvatar`）。
**本档案里没有任何模板声明它**——本轮做过穷尽扫描：

```
全档案 equipment 索引 118479 条，以 `avatar]` 结尾的 [equipment type] 恰好 11 种，
覆盖 66328 个模板：hat 8325 / hair 7929 / face 7028 / coat 8391 / pants 8194 /
shoes 8070 / breast 7565 / waist 7000 / skin 2954 / aurora 423 / weapon 449
```

**没有第 12 种。** 所以槽 11 没有键可补——不是漏了，是档案里没有东西会用它。

#### 一处已被本轮证据推翻的记载

`PvfEquipmentSlots` 的注释与 `protocol/game/server-to-client/NOTI-0x0002.md` 都写着
「A21 在**武器之前**插了 `AuraSkinAvatar`（槽位 11）」。
上游枚举明写 `WeaponAvatar = 10`、`AuraSkinAvatar = 11`——**11 在 10 之后，不是之前**。

#### 上一件在 `PutOnAsync` 留了一处点名等本件回来

`80bdb144` 的 `PutOnAsync` 不判断「被顶下来那件能否回到源格」，理由是
**今天装扮槽上站不了任何东西**（`ByName` 最低到 12）。注释明写：

> **That stops being true the day those slots get names** -- which is exactly what giving
> costumes a slot map amounts to. Whoever does that has to come back here and decide whether
> what comes off a costume slot can go back where the arriving costume came from.

**本件正是那一天。这个判断必须做，且必须写下结论。**

## Goal

让档案说得出时装穿在哪一格，从而时装能真的穿上；并把上一件点名留给本件的那个判断做掉。

## Scope

### In

- `PvfEquipmentSlots.ByName` 补上 **11 个**时装键，值为上表槽号。
  **来源标注要分层**：槽号**实机量得**、键名**档案读出**、两者与**上游枚举**逐行吻合。
- **做掉 `PutOnAsync` 留下的那个判断**：装扮槽有了名字之后，
  「被顶下来那件能否回到源格」是否需要判定？结论与理由都要写进那段注释，
  取代现在那段「今天站不了任何东西所以无从发生」。
- 判据：11 个键各自的槽号；**错误的槽被拒**（`ItemCannotOccupySlot`）；
  时装能真的穿上（走通 `PutOnAsync`）；以及上一条判断的结论。
- 记载：更正「`AuraSkinAvatar` 在武器之前」这一处（两个出处）；
  更新 `AGENTS.md` 的量具说明书——**量具的用途到此结束**，它是为量这张表造的，表已量全。

### Out

- **不做 issue #13**（耳环/契约兽/快捷栏）。上一件已因「越出授权面」回退过一次，不得顺手再改。
- **不给槽 11 补键**：档案里没有任何模板声明它（穷尽扫描所得）。
  若日后档案换版出现了，那是另一件。
- **不动 `listParam16`**（issue #12）。
- **不改任何协议帧的字节布局，不新增协议帧。**
- **不做实机验证**——所有者说「做完以后我们再一起测试」，那是**本件交付之后**的事，
  不是本件的验收手段。

## Constraints

- **被授权改变的可观察行为**：时装从「拖到装扮槽被拒为 `NotWearable`」变为「能穿上」，
  以及随之而来的拒绝（拖到错误的槽 → `ItemCannotOccupySlot`）。
  **其余任何行为变化都要停下报告**，尤其是非时装物品的穿戴行为。
- **`ByName` 现有的每一个键与值不得改动**（`[weapon]`=12 起的那些）。它们有既有判据钉着。
- 既有判据的期望值与断言**不得为迁就实现而改写**；正对应上述授权变化者除外，
  且每处改写要单独说明它对应哪一条。
- `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 必须继续通过。
- `TreatWarningsAsErrors` 开启，CS9113 是错误。
- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host。
- 每个任务单元结束时判据必须全绿，不允许攒红。
- **不得触碰**主检出的 `ClientHack/`、`.worktrees/client-debug-lab`、
  `.worktrees/entries-are-what-the-client-reads`。
- **禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）。
  变异核实用文件级备份 + `sha256sum`，每次用完立刻还原并核散列。
- 记载不得写成比证据强：「实机量得」「档案读出」「上游读出」要分别标明。

## Success Conditions

- **SC-1** `ByName` 有且只有那 11 个时装键，槽号与上表一致；判据逐键钉住，改错会红。
- **SC-2** 一件时装拖到**它自己的**装扮槽能穿上；判据走通 `PutOnAsync` 并证明两端都落了。
- **SC-3** 一件时装拖到**别的**装扮槽被拒为 `ItemCannotOccupySlot`，且拒绝**带模板号**。
- **SC-4** 非时装的穿戴行为**逐值不变**；`ByName` 原有键值一个未改，其既有判据一条未动。
- **SC-5** 上一件留在 `PutOnAsync` 的那个判断被做掉：结论、理由、证据都写进那段注释，
  且**有判据钉住结论**。若结论是「需要加判定」，那就是一处新的行为变化，要按授权面报告。
- **SC-6** 全量判据不低于基线 **2335 / 2 / 0**，退出码 0；
  字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧**逐帧不变**
  （本件不碰编码器；若它变了说明改动波及了不该波及的地方，停下报告）。
- **SC-7** 「`AuraSkinAvatar` 在武器之前」这处记载在**两个出处**都被更正；
  `AGENTS.md` 的量具说明书按「表已量全」更新。
- **SC-8** 交回一份变化清单，**正向与反向都报**、**从代码出发独立扫**，
  且**搜索集要真的被执行**——上一件在这里栽过两次：
  一次范围写对了但没照它执行（`.md` 一次都没扫），
  一次承认之后只补跑了五条 `grep` 里的一条。
  **声明的每一条命令都要真的敲下去，并把命中数写进清单。**
  另：**改完一张表要回头数一遍行数**（上一件的 R-7 随被删小节丢失而表头仍说七条）。

## Authorization

覆盖：任务契约的创建与修订、路由、**一个任务 worktree**、配置内的执行与验证、
经完整评审与新鲜验证之后的**一次本地接受提交**、以及一次未接受尝试的精确工作流回滚。
**不含**合并、推送、建 PR、删除分支或 worktree、清理。
本项目已获所有者常驻授权可调用子代理。

## Requires Confirmation

- 若补上这些键之后，**非时装**的穿戴或移动行为发生任何变化，停下报告。
- 若 `PutOnAsync` 那个判断的结论是「需要加一次判定」，**先报告再做**——
  上一件正是在这里越过授权面并被回退。
- 若发现档案里存在第 12 种以 `avatar]` 结尾的类型（与本轮穷尽扫描矛盾），停下报告。

## Assumptions

- **槽号来自实机观测**（11 件、11 个互不相同的槽、`0..10` 连续无缺口；
  上衣另有两次更早的独立观测）。
- **键名来自档案读出**（本轮探针逐件读 `[equipment type]`）。
- **上游枚举是第三方佐证**，不是规范；引用它的地方要标明「上游读出」。
- **槽 11 无键可补**来自本轮的**穷尽扫描**（118479 条 equipment 索引，11 种类型，66328 个模板）。
  这是当前档案的事实，不是协议的事实。
- 本件之后，NOTI `0x0002` 子类型 1 与 ITEM_LIST 装备栏路径**会第一次真的带着时装外观块上线**
  （那两条路的编码在 `ec2ef1b6` 与更早的成果里已经写好并有判据，但**从未有过真时装走过它们**）。
  这是所有者说「做完以后我们再一起测试」要测的东西，**不是本件的验收条件**。
