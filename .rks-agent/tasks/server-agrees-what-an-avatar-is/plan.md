---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 服务器对「一件时装是什么」只有一个说法

## Implementation Approach

Spec 的核心成果是「一件时装能被发放给角色并在服务器上完整活下来」，必要支撑是「种类只有一个说法、身份互不相同、连带判定被查明」，外围保证是判据、字节锚、双向核对的变化清单与仓库记载。**量具不是外围**——契约把它列为交付物，所以它是一条要求。

**取证已经做完**，中心结论是：病不在那个 `1`，而在**发放通路造不出时装形状的东西**。`StoredItem` 只带 `SlotIndex`／`Core`／`WorldClass`／`Identity`，时装那六列在类型上就传不进结算；`FillAsync` 的六列只从 `CarriedAvatarsAsync`（库里已有的行）取，所以一件全新时装六列全 NULL，既让唯一性守卫短路放行，又让它下次装载必炸。

**中心做法**：让「时装细节」和 `WorldClass` 走同一条路。`StoredItem.WorldClass` 的 remarks 已经把这个形状写清楚了——「Settlement decides on it, so a write must answer it」。结算要写那六列，所以写入方必须交代它们。这条做法只扩展一个已有的、意图明确的写入契约，不新建通路、不新建表、不动协议。

**顺带收一个真缺口**：细节非空使 `@expire` 非 NULL，`FillAsync` 的时装唯一性守卫**因此对新入库的时装开始真的生效**——今天它对新行是完全短路的。这不是副产品，是本轮要坐实的一条。

**最早的核心任务是 T-002**（档案给时装种类 ＋ 发放通路造出时装形状）。**T-001 排在它之前，是不可避免的执行前提**：没有携带细节的能力，T-002 写出的时装行六列仍为空，落地即是一条「下次装载必炸」的行——把一个已知会炸的中间状态留在树上，比先把通路修通更危险。T-001 今天没有任何供给方，因此它本身行为中性。

**`Core.Value` 的取值裁定**：抽取，不排序。本仓已有同形先例 `CharacterRosterService.EquipmentCore`，其注释写明理由「a sequence would need a row of its own to stay distinct across restarts」。三条替代各自被否，理由见 Risks and Rollback。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs` | `StoredItem` 增加一个承载时装细节的字段，并把「写入时装必须交代它」写进契约；不改任何既有成员的语义 | 现有 `StoredItem` 契约与其 `WorldClass` 的 remarks | 一个能表达「这次写入的时装带着哪六个事实」的写入契约 |
| BD-002 | `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` | 结算写 `items` 六列时优先采用本次写入自带的事实，回捞仅用于重建既有行；随之更正那三条跨方法耦合的记载 | BD-001 的写入契约；现有 `FillAsync`／`LandAsync`／`CarriedAvatarsAsync` | 新入库的时装带齐六列；其唯一性守卫因此对新行生效 |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` | 档案判定为时装的模板，其元数据给出时装那个种类；更正「时装被有意留着不动」这句已不再成立的记载 | 档案对模板的世界类别判定 | 时装模板的种类与服务器各处一致 |
| BD-004 | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | 发放通路对档案判定为时装的模板造出时装形状：互不相同的身份值与六列默认事实；非时装一字不动 | `placement.WorldClass`；BD-001 的写入契约 | 一件可被结算完整写下的时装 |
| BD-005 | `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | 穿戴被拒时结果带上该物品的模板号，并让既有的那条拒绝记录一并说出它；既有的目标槽号字段不动 | `PutOnAsync` 两个拒绝点手上的物品；既有拒绝日志 | 一条含模板号与客户端点名槽号的记录 |
| BD-006 | `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md` | 新建：本轮全部可观察行为变化，含从新增或改变的拒绝与放行路径反向扫出的那一半 | 各任务的实测与变异结果 | 一份双向核对过的变化清单 |
| BD-007 | `AGENTS.md` | 记下本轮做到哪一步、槽号出处的穷尽结论、认不出的类型名与从未被用过的键 | 三份 findings 的结论 | 后来者读得到的如实记载 |
| BD-008 | `Server/tests/` | 只新增判据；既有断言的期望值不得改写，唯一例外是正对应本 Spec 列出的某条行为变化，且须逐条归因 | 各任务的可观察结果 | 覆盖本轮每条要求的判据 |

## Ordered Tasks

### T-001: 世界的写入能说出一件时装带着哪六个事实

- Covers: RQ-002
- Boundaries: BD-001, BD-002, BD-008
- Inputs: `StoredItem` 现有契约；`FillAsync`／`LandAsync`／`CarriedAvatarsAsync` 现状
- Outputs: 一条写入可以自带时装事实；结算把它写进 `items` 六列；今天无供给方，行为中性

#### Steps

1. 给 `StoredItem` 增加承载时装细节的字段，按 `WorldClass` 已有的形状写清它的契约：读的方向不答，写的方向对时装必须答。
2. 让结算写六列时优先用本次写入自带的事实；只有在重建一行既有时装时才回捞既有事实。
3. 更正那三条「今天成立但原因在别的方法里」的耦合记载，使其描述改动后的真实情形；被改动的旧说法按仓规补记修正案而不是抹去。
4. 不给任何调用方接线。本任务结束时仍然没有生产代码供给事实。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，退出码 0；既有 2216 条一条不少、一条不红，跳过仍是同样那 2 条；增量全部来自本任务新增的判据，并按项目逐条归因 |
| CK-002 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Persistence.Tests --nologo` | 新判据证明一次自带事实的写入把六列写进了世界行，且值与写入所给的相同 |
| CK-003 | Non-command: 通读被改动的那几处记载 | 每一处仍然成立的说法保持原样，不再成立的说法已就地更正并写明它此前说的是什么 |

### T-002: 档案说是时装时，发放通路造出一件完整的时装

- Depends on: T-001
- Covers: RQ-001, RQ-002, RQ-003
- Boundaries: BD-003, BD-004, BD-008
- Inputs: T-001 的写入契约；档案对模板的世界类别判定
- Outputs: 一件经发放通路入库的时装，种类、身份、细节三者齐备且可被重新装载

#### Steps

1. 让档案元数据对判定为时装的模板给出时装那个种类；更正同处「时装被有意留着不动」那句已不再成立的记载。
2. 发放通路在落地一件时装时造出时装形状：一个抽取得到的、大于零的身份值，加六列默认事实——珠宝槽 30 个零字节，两个颜色为零，清除标识为零，删除日期为零，到期日取 32 位有符号秒数的上界。
3. 到期日这个取值在代码里写明它是**占位裁定**：本仓语义中该列非空的含义是「这一行是时装」而非「这件时装会过期」，而线上那一格写的是剩余秒数；上界是当前可辩护的「不过期」表达，实机会检验客户端如何渲染它。
4. 非时装模板的落地路径一字不动。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；用例数相对 CK-001 的增量全部来自新增判据 |
| CK-005 | Non-command: 对真实档案取一个时装模板，检查发放通路交给结算的种类 | 与服务器各处判定时装所用的种类相同 |
| CK-006 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 新判据走通「发放 → 装载 → 存盘 → 再装载」，全程无拒绝无回滚，物品仍在，身份与细节与入库时相同 |
| CK-007 | Non-command: 备份文件并核散列后，把发放通路造出的细节改成空，重跑 CK-006 的判据，再由备份还原并核散列 | 该判据变红；还原后恢复绿。证明这条通路真的被判据钉住，而不是碰巧通过 |

### T-003: 两件时装不会共用一个身份，撞号被拒而不是静默留下

- Depends on: T-002
- Covers: RQ-004
- Boundaries: BD-002, BD-008
- Inputs: T-002 造出的时装；世界的时装唯一性守卫
- Outputs: 守卫对新入库的时装真的生效，且这一点有判据

#### Steps

1. 加判据证明两件分别发放的时装拿到互不相同的身份值，且都被世界接受。
2. 加判据证明当一次入库携带的身份值与世界里已有的时装相同时，写入被拒而不是静默写下。
3. 在记载里写明这条守卫此前对新入库的行是完全短路的，以及它现在为什么不再短路。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Persistence.Tests --nologo` | 两件时装并存的判据通过；撞号被拒的判据通过 |
| CK-009 | Non-command: 备份并核散列后，把守卫的条件改成恒真，重跑 CK-008，再还原并核散列 | 撞号判据变红；还原后恢复绿 |
| CK-010 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败 |

### T-004: 种类改变之后，两条既有判定各自的结局被坐实

- Depends on: T-002
- Covers: RQ-005, RQ-006
- Boundaries: BD-008
- Inputs: 时装的新种类；快捷栏准入规则与到达落点规则
- Outputs: 一处变化与一处不变，各自有判据而非推演

#### Steps

1. 加判据证明一件时装不能被放进快捷栏的格子。记载里写明这不是新规则——排除项本就写着时装，此前失效只因发放侧标错了种类。
2. 加判据证明一件时装到达时被考虑的落点范围与改动前相同：只有它自己那一段，不含快捷栏。
3. 把这两条各自记进变化清单的对应栏：一条是变化，一条是查过的不变。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 快捷栏拒绝的判据通过；落点范围不变的判据通过 |
| CK-012 | Non-command: 备份并核散列后，把快捷栏排除项里的时装那一项去掉，重跑 CK-011，再还原并核散列 | 快捷栏判据变红；还原后恢复绿 |

### T-005: 服务器说不出一件时装该穿在哪一格时，把客户端点名的那一格记下来

- Depends on: T-002
- Covers: RQ-007
- Boundaries: BD-005, BD-008
- Inputs: 穿戴被拒时手上的物品；既有的那条拒绝记录
- Outputs: 一条含模板号与目标槽号的记录，供实机读出映射

#### Steps

1. 让穿戴被拒的结果带上该物品的模板号——两个拒绝点手上都已经有它。
2. 让既有的那条拒绝记录一并说出模板号。目标槽号那一项已经在记，不改它。
3. 不新建日志通路、不新建类型、不改协议、不扩充任何架构判据的豁免清单。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Protocol.Tests --nologo` | 新判据断言：一件时装被请求穿到某一格而遭拒时，留下的记录含该物品的模板号与请求点名的那一格 |
| CK-014 | Non-command: 备份并核散列后，把记录里的模板号换成常量，重跑 CK-013，再还原并核散列 | 该判据变红；还原后恢复绿 |
| CK-015 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；命令处理器与通知处理器的注入面判据仍然通过 |

### T-006: 行为变化清单，两个方向都扫过

- Depends on: T-002, T-003, T-004, T-005
- Covers: RQ-008, RQ-010
- Boundaries: BD-006
- Inputs: 各任务的实测与变异结果；入场帧的字节比对
- Outputs: 一份完整性由实测支撑的变化清单，与一张字节差异归因表

#### Steps

1. 正向扫：从本轮改动的每一处起因往下游追，列出它触达的每一条判定。
2. 反向扫：把本轮新增或改变的拒绝与放行路径逐条列出，对每条回答它在生产上是否可达、有无判据覆盖。**不得以一次全量判据全绿代替这一步**——全绿恰恰意味着没有既有用例走到那些新路径上。
3. 每条给出位置（按成员名索引，不按行号）、生产可达性与判据有无。给出数目时同时列出构成它的各项与计数口径。
4. 比对入场帧的整表字节。若有差异，逐处归因到清单里的某一条；无法归因的差异即为缺陷，不得放过。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | Non-command: 对清单里每一条声称「有判据覆盖」的路径，逐条做一次变异实测 | 每条都能让至少一个判据变红；不能变红的条目改写为「无覆盖」 |
| CK-017 | Non-command: 比对入场帧整表的字节 | 要么逐字符不变，要么每一处差异都归因到清单里的某一条 |
| CK-018 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；用例数相对 2216 的差额逐条归因 |

### T-007: 仓库约定如实记下这一轮做到哪一步

- Depends on: T-006
- Covers: RQ-009
- Boundaries: BD-007
- Inputs: 三份 findings 的结论；T-006 的清单
- Outputs: 后来者读得到的记载，含没做到的部分

#### Steps

1. 记下本轮做成了什么：时装的种类只有一个说法，发放通路能造出完整的时装，唯一性守卫对新行开始生效。
2. 记下**没做**的部分：时装仍然穿不上，因为槽号一格都没有出处；量具是为此留的。
3. 记下槽号出处的穷尽结论：档案里时装名后面那个数恒为零所以不是槽号；实测里没有任何一帧显示角色穿着时装；上游转写只到带宽度与第 11 格，且自陈不是量出来的。
4. 记下装备类型对照今天认不出的全部类型名，以及其中在档案里从未被任何模板使用过的两个键；每个数目都附带构成它的各项与计数口径。
5. 记下那条无出处的译名：底层判据说的名字、档案里另一个同样以该词开头的名字、以及两者件数的差别。按仓规这是补记修正案，不是抹去原文。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | Non-command: 逐句核对新写下的每一条记载 | 每一句都能被指回一处实测或一处代码；没有一句比代码说得更强 |
| CK-020 | Non-command: 检查记载里出现的每一个数目 | 每个都同时给出构成它的各项与计数口径，读者可自行复算 |
| CK-021 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，退出码 0 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-002 | T-001, T-002 | BD-001, BD-002, BD-004 | CK-002, CK-006, CK-007 |
| RQ-003 | T-002 | BD-004, BD-008 | CK-006, CK-007 |
| RQ-004 | T-003 | BD-002, BD-008 | CK-008, CK-009 |
| RQ-005 | T-004 | BD-008 | CK-011, CK-012 |
| RQ-006 | T-004 | BD-008 | CK-011 |
| RQ-007 | T-005 | BD-005, BD-008 | CK-013, CK-014 |
| RQ-008 | T-006 | BD-006 | CK-016, CK-018 |
| RQ-009 | T-007 | BD-007 | CK-019, CK-020 |
| RQ-010 | T-006 | BD-006 | CK-017 |

## Risks and Rollback

- Risk: 抽取得到的身份值可能与已有时装相撞。今天这类碰撞既不被世界拒绝也不被装载发现，会静默留在库里。缓解正是 T-003：本轮让守卫对新入库的时装真的生效，碰撞因此变成一次可见的拒绝而不是一条坏数据。
- Risk: 到期日的取值是占位裁定。本仓语义中该列非空表示「这一行是时装」，而线上写的是剩余秒数，因此上界值会让客户端看到一个极大的剩余时间。实机会检验它；代码与记载都要写明这是占位而非结论。
- Risk: 让结算改用写入自带的事实，会触碰记载中三条「今天成立但原因在别的方法里」的跨方法耦合。缓解是 T-001 第 3 步把它们逐条重读并就地更正，而不是留着一份描述旧世界的注释。
- 被否掉的替代一：让结算把行的世界身份回填进核心的那个位置。否，因为结算之后调用方手上的核心与库里的核心会在该位置永久不一致，而幸存者匹配的前两趟按整段核心比字节——一次移动就会匹配不上，退化成删旧建新，六列细节随之丢失。
- 被否掉的替代二：给写入契约加一个「预支一个身份」的动词。否，因为「不把身份交给调用方」是被明文写下的设计要求而非一处已被证伪的记载；按仓规改要求必须先停下来问，本轮不走。
- 被否掉的替代三：取消「时装身份」这个概念，改用行本身的世界身份关联那六列。否，理由同上：它要放宽三处明文写下的「身份必须大于零」的要求。**但它值得留给后继考虑**——时装的那个值不上线，而契约生物的身份就是行的世界身份且直接上线，两者今天不统一。
- Rollback: 每个任务结束时判据必须全绿，不允许攒红。任一任务失败时按工作流的精确回滚回到接受基线；变异实测一律用文件级备份加散列核对还原，不使用任何 Git 还原类命令。

## Assumptions

- 时装的那个身份值今天不发给客户端，因此它不需要跨会话稳定，也不需要与行的世界身份一致。唯一的例外是邮箱附件摘要，它读的是另一张表的核心，不经世界表——本轮不动它，但要在记载里点名为残余。
- 结算能从一次写入拿到时装事实之后，既有事实的回捞仍然必要：它服务的是「一行既有时装被重建」这个场景，与新入库不是同一件事。
- 判据基线为 2216 通过、2 跳过、0 失败，退出码 0，已在本工作区实测。两个跳过是内容档案已加载的标志。
