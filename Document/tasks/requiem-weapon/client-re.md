# 镇魂武器：客户端界面与协议证据（A21 静态调查）

调查日期 2026-11-07，只读。范围：客户端代码与协议；PVF 内容盘点（镇魂/释魂武器、礼盒、设计图的实际 ID 与配方）由另一调查负责，本文不下结论。

## 证据输入

| 项 | 值 |
|---|---|
| 反汇编对象 | `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe`，本次实测 SHA-256 `d7618a102251f1db724f2514c74a4cc855df26e6a4bc308f8ee9f2397db0a99a`，ImageBase `0x00400000` |
| 与协议库地址的对应 | 协议页机器区写的构包点 `0x00856571`/`0x0201B21B`、handler `0x00D36A30` 均为 RVA；本文件里同一位置的 VA 分别是 `0x00C56571`/`0x0241B21B`/`0x01136A30`，指令与协议页一致。下文地址除注明外都是 **VA**（RVA = VA − 0x400000） |
| 文案库 | `DNFClient-A21-debug/dstr.dat`，SHA-256 `da151f228267746221f13f598a388479266a8abf21a0ab542e62753eb5df43d7`，用仓内 `Tool/ClientStringPatch/patch_sao_strings.py` 解码；取资源 `FILE/102`（简中）与 `FILE/104`（日文）。实机显示哪一份未在本次确定；`0x0010.md` 记载 protocol2 客户端实际显示简中，故下文以简中为主、日文并列 |
| 抓包 | 各 `DNFClient-A21-*` 目录现存 `ClientHack.log` 中没有 `0x0019` 帧；唯一线级样本仍是 `0x0019.md` 记载的 1287 设计图 9B 实帧 |
| 上游 | `ServerS4A21/.../InventoryCompoundItemRecipeService.cs`、`InventoryHandler.CompoundItem.cs`、`CompoundItemAckBuilder.cs`（UpstreamReference，仅线索） |

来源标签沿用 catalog 惯例：ClientStatic（客户端静态指令）、WireCaptured、ContentStatic、UpstreamReference、CurrentImplementation、Unknown。

## 结论摘要

1. **客户端里没有专门的"镇魂→释魂开光"协议或界面。** 在 A21 枚举、协议索引与文案库中都找不到 requiem/镇魂/释魂/开光 专属的 CMD 名或 UI 文案（文案库里"镇魂曲"只用于地下城/任务）。客户端唯一能表达"一件装备作为材料、产出另一件装备"的现成路径是**设计图制作窗口 → `CMD 0x0019 COMPOUND_ITEM`**，它的材料计数、预检与回执处理都显式支持"装备作材料"（下文第 1、4 节）。上游 ServerS4A21 也是把释魂系列做成 `[recipe]` 设计图 + 0x0019（注释"释魂系列…设计图说明「该武器为封装状态」"）。——ClientStatic / High（"释魂走设计图"本身仍需 PVF 盘点确认设计图存在且材料含镇魂武器，未确认前为 Hypothesis）。
2. 已排除的近似路径：JP「能力開放」（简中"勇者装备改造"，文案 `0xA505..0xA514`）的窗口发的是 `0x010F UPGRADE_CHRONICLE`（构包 VA `0x02416D1A`），属于勇者/编年史装备，不是镇魂武器——ClientStatic / High。`0x0362 TRANSFORMATION_ITEM`（VA `0x015108E2`，8B：`u16 slotA,u32 itemIdA,u16 slotB`）所在区域文案是封装/品级调整箱/宠物封印，未见与镇魂相关——关联 Unknown。
3. 镇魂武器礼盒若是可选礼包，走已实现的 `0x00A0 USE_BOOSTER_ITEM` 选择形态；`0x0207` 为装扮礼包。文案 `0x11DCA/0x11DCC`（"兑换成功，获得终极镇魂武器礼盒"/"+15不死鸟武器兑换成终极镇魂礼盒"）只有简中、日文表为空，客户端引用点 VA `0x00F403B4`，与 PHOENIX_WEAPON_EVENT 系列相关的推测为 Hypothesis，JP 版本是否开放 Unknown。
4. **当前服务端与客户端的三处不匹配**（CurrentImplementation 对照 ClientStatic，见第 3、6 节）：失败码 `NoRoom→0x7E` 在 0x0019 handler 里显示"正在适用防沉迷系统的限制"；`MissingMaterial→0x16`、其它→`2` 在 0x0019 handler 与通用兜底表里都无文案（静默）；配方领域服务找材料的容器不含装备栏，镇魂武器作为材料会被判缺料。

## 1. 触发与请求：设计图制作窗口 → CMD 0x0019

### 1.1 触发

- WireCaptured / Confirmed（既有）：角色在背包槽 101 使用 1287 设计图，点击"制作"，客户端发 `0x0019` 9B `65000000 00 0100 00 01`（见 `protocol/game/client-to-server/0x0019.md`）。即"右键/使用背包里的 `[recipe]` 设计图 → 打开制作窗口 → 点「制作」"。
- ClientStatic / Confirmed：窗口打开函数 VA `0x0241CD80`（`Open(itemObj, mode)`，虚表槽 `0x035C803C`），`[this+0x26C] = (mode==1)`。打开前依次拒绝：决斗场（文案 `0x4B43`）、交易中（`0x11475`）、开店中（`0x7D0`）、窗口 `0x3C`/`0x3D` 已开；随后把物品对象动态转换为设计图类（RTTI `0x0397B188`），失败则不开。
- ClientStatic / Confirmed：「制作」按钮 VA `0x0241BBF0`（虚表槽 `0x035C8048`）：不在城镇 → 文案 `0x3A5`「只能在城镇内合成物品」；`mode==1` → 弹数量对话框（`[this+0x29D]==1` 时类型 6，可输数量；否则类型 5），回调 VA `0x025B0205` 调发送函数；`mode==0` → 先跑预检 `0x0241B430`（第 4 节），通过后以数量 1 调发送函数 `0x0241B180`。
- mode 1（按模板 ID 制作）由谁打开：Unknown（未找到调用 `Open` 的具体位置；推测为生产 NPC/已学配方列表，Hypothesis）。

### 1.2 请求布局（9B 路径）

发送函数 VA `0x0241B180`（构包点 VA `0x0241B21B` / RVA `0x0201B21B`）：

```text
0x0241B186  call 0x16ed890 (ecx=[0x3a5c9bc]) ; >0 直接返回（待回执锁，Hypothesis）
0x0241B1A5  call 0x241a8f0([this+0x1b0])     ; 可制作次数
0x0241B1B0  cmp eax, count ; < count → 文案 0x3A1「合成需要的材料不足」，不发
0x0241B1DA  cmp byte [this+0x26c],0           ; mode
            mode1: edi=[this+0x198]（设计图模板 ID）
            mode0: edi=0x16ef580([this+0x198])（背包内该设计图的槽号；-1 → 文案 0x3A2「缺少设计图」）
0x0241B212  push 0x19 ; MakeHeader
WriteU32 edi ; WriteU8 (mode!=0) ; WriteU16 count ; WriteU8 0 ; WriteU8 1
```

| 偏移 | 宽 | 含义 | 置信度 |
|---:|---:|---|---|
| 0 | u32 | mode0：设计图所在主背包槽号；mode1：设计图模板 ID | ClientStatic / Confirmed（来源指令）；含义 High |
| 4 | u8 | mode（`[this+0x26C]!=0`） | Confirmed |
| 5 | u16 | 制作数量 | Confirmed |
| 7 | u8 | 常量 0 | Confirmed（值）；用途 Unknown |
| 8 | u8 | 常量 1 | Confirmed（值）；用途 Unknown |

**请求里没有作为材料的装备（镇魂武器）的槽号、UID 或模板**，也没有材料清单。服务端必须自己按设计图 PVF 找材料实例。客户端靠预检保证该装备在背包/快捷栏里只有 1 件（第 4 节），所以按模板 ID 唯一定位是客户端设计好的契约——ClientStatic / High。

另一个 7B 构包点 VA `0x00C56571`（RVA `0x00856571`）：`WriteU32 槽号(由[obj+0xC]模板ID查得) , WriteU8 0 , WriteU16 1`，发送后写 `[0x03993EB8]=0xA`；调用者 `0x00C565B0` 依赖窗口 `0xA2`/`0x1D`。触发场景 Unknown。

## 2. 响应：CMD 0x0019 与后续通知

handler VA `0x01136A30`（RVA `0x00D36A30`），状态字节由 CMD 分派读出后传入 `[ebp+0xC]`。

### 2.1 成功（状态=1）

布局与 `server-to-client/CMD-0x0019.md` 一致（消耗行 7B、产物行），本次补充客户端的处理分支：

- **消耗行** `u8 list, u16 slot, u32 n`（`0x01136A90..0x01136BB5`）：
  - `list==0` 或 `0x24` → 主背包 `[0x03A5C9BC]`；`slot==0` 时对槽对象调 vfunc+0x50(n)（减量）；否则取槽物品，`当前数量(vfunc+0x48) > n` → 减 n，否则 `0x016F1300` **整格移除**。装备数量为 1，扣 1 即整格移除——ClientStatic / Confirmed（分支），装备数量=1 为 High。
  - `list==1` → `[0x03A5C9D0]` 移除该槽。该全局是装扮栏（`Document/avatar-container-entry-shape.md`，High）。
  - `list==3` → `[0x03B3DFB8]` 对象 vfunc+0x710（推测宠物类容器，Unknown）。
  - **对镇魂武器（主背包装备）应写 `list=0`**。上游 `ResolveChangedEntryKind` 对"非堆叠且耗尽"写 1，会被客户端送去装扮栏，属上游错误线索，不要照抄——ClientStatic / High。
- 之后取窗口 `0xF6` 作为结果展示。
- **产物行**首字节（`[ebp-0x3A6]`）分支（`0x01136DFF`）：
  - `==1`：只构造物品对象（`0x0154D560`/`0x01A85220`）加入结果展示，**不写入主背包任何槽**。
  - `!=1`：取主背包对应槽；有物品且可合并（vfunc+0x5C）则改数量（vfunc+0x4C），否则新建对象并 `0x016F2FB0(slot, item,1,1)` 放入。
  - 两种都随后 `0x024C9DC0` 把物品加入结果窗口；最后一行时 `0x01648A20` 播放提示。
  - 当前编码器对 ItemKind==1（装备）写 `1`，因此释魂武器**不会被 CMD 放进背包**，完全依赖后续 `NOTI 0x000E`；写 `0` 则 CMD 自己按槽放入。该字节是否是"容器选择子"（与消耗行同义，1=装扮栏）为 Hypothesis；分支行为本身 Confirmed。上游同样对装备写 `KindEquipment`。是否改为 0 需实机核对。
- 成功体内**没有"制作成功/失败"标志或成功率字段**；客户端只按产物行展示。若存在"开光失败"语义，只能体现在产物/消耗行或失败码上——ClientStatic / High。

### 2.2 失败（状态=0，`u8 错误码`）

`0x011373A6`：`code-4` 查跳表（索引表 VA `0x011375F4`，目标表 VA `0x011375D4`，范围 4..0xD9），表外走通用兜底 `0x026181C0`。显示通道 `0x02304C70(0x209, text)`（消息框）。

| 码 | 文案 id | 简中 | 日文 | 备注 |
|---:|---:|---|---|---|
| 0x04 | `0x2EA` | 物品栏空间不足 | インベントリに空きがありません。 | 满包首选 |
| 0x0E | `0x2F3` | Lv过低， 无法使用 | 制限レベルを満たしていないため… | |
| 0x13 | `0x4B17` | 需要净化装备中蕴含的异界气息。 | 異界の気運を浄化していないアイテムは材料としてご利用できません。 | 装备材料未净化 |
| 0x7E | `0x11175` | 正在适用防沉迷系统的限制 | （乱码） | 另向聊天栏写一行（`0x01137475` 种类 0x2F） |
| 0xB3 | `0x3333` | 活动未开放 | イベント期間ではありません。 | |
| 0xCC | `0x2FF` | 物品栏空间不足\n请清理出一个以上的物品快捷栏… | アイテムが入るインベントリ・スロットがありません… | |
| 0xD9 | `0x4B16` | 只有修理过的物品， 才能作为材料。 | 修理していないアイテムは材料としてご利用できません。 | 装备材料未修理 |
| 其它 | — | 交 `0x026181C0`：只处理 `0x4D/0x69/0x72/0x73/0x7A/0x7B/0x7C/0x86/0x88/0x89/0x8A/0xA7/0xD0/0xD2/0xD6/0xE4/0xE5/0xE6/0xEA/0xED/0xEF…`；**不含 0x02、0x11、0x15、0x16** → 静默 | | |

全部 ClientStatic / Confirmed（跳表与文案 id 由指令读出；文案为本次 dstr 解码）。

与当前实现对照（CurrentImplementation，`Server/src/Dfo.Server.Protocol/Game/OpeningDelivery.cs:66` `OpeningPayloads.Error` 被 `GameCompoundItemCommand.cs:78` 复用）：

- `NoRoom → 0x7E`：在 0x0019 里显示"防沉迷"——错文案。0x0019 应用 `0x04`（或 `0xCC`）。
- `MissingMaterial → 0x16`、其它 → `0x02`、默认 `0x11`：静默。客户端没有"材料不足"的 0x0019 失败码（缺料由客户端本地拦截，文案 `0x3A1`）；若需诚实反馈，只能在上表里挑语义对得上的码，否则静默。
- 装备材料未净化/未修理分别有专用码 `0x13`/`0xD9`。

### 2.3 后续通知

客户端成功分支只处理 CMD 自带的增减；最终绝对值靠 `NOTI 0x000E UPDATE_ITEM_LIST`（当前 `OpeningCompletedHandler` 依序发：CMD → 被替换格清空 → 各容器增量 → 被清空格 → 金币 `0x000E` 虚拟槽）。catalog 中 `NOTI 0x000E` 为 ClientVerified（金币绝对值与分解刷新），"同格不同 ID 先清后填"为 ClientStatic + CurrentImplementation 待实机。镇魂→释魂若产物落在源武器原槽，就是"同格不同 ID"，依赖这条修复——High。

## 3. 客户端本地计算与预检（第 4 问）

### 3.1 可制作次数 `0x0241A8F0`

ClientStatic / Confirmed：

- 产物列表（`[list+0x14]`）：`0x00EE6B80(id)` 判库别（在 `[0x03A5C910]` 中 → 2，否则 1）。任一产物是装备库（2）→ 上限 1；堆叠库（1）→ 上限 = `[0x03A5C914]` 中该物品 `+0x6A0`（堆叠上限）。即**产出装备的设计图一次只能做 1 个**。
- 材料列表（`[list+0]`，每项 `id, need`）：`have = 0x016EF410([0x03A5C9BC], id)`，为 0 再查装扮栏 `[0x03A5C9D0]`；`次数 = min(have / need)`。
- `0x016EF410`：遍历主背包各页；匹配到**可堆叠**物品直接返回其数量；**不可堆叠（装备）按件计数**。所以装备作材料是客户端原生支持的。是否包含快捷栏、是否排除已穿戴：已穿戴装备不在 `[0x03A5C9BC]`（High），快捷栏 Unknown。
- 另一材料列表 `[list+0x40]` 只在第二参数为 1 时参与（用途 Unknown）。
- **不检查金币。** 窗口另有 `%d金币` 显示（文案 `0x45C`，VA `0x0241FB77`），数值来源字段 Unknown。另有负重超限提示 `0x3BA`（`0x0241CAF5`），是否阻断 Unknown。

### 3.2 装备材料预检 `0x0241B430`（只在 mode0 点「制作」时）

对每个材料，若 `0x00EE6B80(id)==2`（装备）：

| 顺序 | 条件 | 结果 | 置信度 |
|---|---|---|---|
| 1 | 主背包中该模板件数 > 1 | 弹 `0x11EEE`「物品或快捷栏中拥有多个[%s]。 请保留1个并把多余的物品存入仓库。」，不发 | Confirmed（日文表此 id 为空） |
| 2 | `0x05260C3D(item)` 为真（受保护函数） | `0x4B17`「需要净化装备中蕴含的异界气息。」，不发 | 分支 Confirmed；"异界气息未净化"按文案 High |
| 3 | 实例 `+0x28` ≠ PVF `+0xC`（vfunc+0x150 / +0x144） | `0x4B16`「只有修理过的物品， 才能作为材料。」，不发 | 分支 Confirmed；当前/最大耐久 High |
| 4 | 实例 `+0x18` ≠ 0，且角色设置 `[[char+0x2AC]+0xAE8]!=1` | 确认框 `0x263`，正文 = 物品名 + `0x4B1A`（简中「操作后该物品的附魔效果将会消失。 确定要继续吗？」；日文同 id 是"背包+快捷栏只能留 1 个"——两表不一致）；确认回调 `0x0241B350` → 以数量 1 发送 | 分支 Confirmed；`+0x18`=附魔 High（据简中文案） |

含义：客户端**预期作为材料的装备会消失、附魔丢失**，并只就附魔提示；对强化/增幅/锻造是否保留没有任何提示或判定。上游的"强化/增幅/锻造/附魔/品级保留"是上游自定，不是客户端证据。释魂产物的属性继承规则客户端未给出——Unknown，需 PVF/规则证据。

### 3.3 必须由服务端判定的

客户端只发设计图槽、mode、数量。以下一律服务端权威判定：设计图归属与数量、材料存在/数量/唯一实例、装备材料是否已净化/已修理/已锁定、角色等级与职业限制（失败码 0x0E 存在，客户端本窗口未见等级预检）、金币费用（客户端不校验）、背包空间、活动期（0xB3）、成功率与失败产物（客户端无本地概率逻辑）、产物实例属性（封装/交易/强化继承）。

## 4. 礼盒与设计图来源相关协议现状（第 3、5 问）

| 协议 | 用途（与本功能的关系） | catalog 现状 |
|---|---|---|
| `0x0019 COMPOUND_ITEM` | 设计图制作；释魂候选唯一路径 | 2026-10-01 普通设计图：WireCaptured + ClientStatic + 70Reference + ContentStatic + CurrentImplementation；0.2.0 用户确认 1287 H14 通过。**"既有装备升级"类配方、mode1、随机失败产物明确未开放并拒绝**；领域服务材料搜索容器为 QuickSlot/Consumable/Material/ExpertMaterial/SharedMaterial（`RecipeCraftDomainService.cs:51`），不含装备栏 |
| `0x00A0 USE_BOOSTER_ITEM` | 可选礼包（选择形态 `i16 slot,u8,u8,i32 ids[n],u8 m,(i32,u8)[m]`） | 实现服务端功能（A21，待实机）；支持"明确正选择数的自选礼包"，装备奖励由领域服务创建 |
| `0x0207 OPEN_CERAPACKAGE` | 装扮礼包 | 实现服务端功能（A21，待实机） |
| `0x00D0`/`0x0450` | 魔盒单开/批量 | ClientVerified（赛丽亚魔盒成功路径） |
| `NOTI 0x000E UPDATE_ITEM_LIST` | 结算后库存绝对值 | ClientVerified（金币、分解）；同格换 ID 先清后填待实机 |
| `0x010F UPGRADE_CHRONICLE` | 勇者装备「能力開放」，非镇魂 | 未登记 catalog，未实现 |
| `0x0362 TRANSFORMATION_ITEM`（CMD） | 两物品转换，关系 Unknown | CMD 未登记（catalog 只有同号 NOTI 守护者祭坛） |
| `0x0421..0x0425 PHOENIX_WEAPON_EVENT_*` | 不死鸟武器活动；简中有"兑换终极镇魂礼盒"文案 | 未登记，未实现；JP 是否存在该活动 Unknown |

## 5. 给实现的直接建议（证据驱动，不含设计）

1. 释魂路径先由 PVF 盘点确认：存在以镇魂武器为材料、产出释魂武器的 `[recipe]` 设计图。确认后沿用 `0x0019`，不需要新协议。
2. 0x0019 失败码改为 0x0019 自己的表：满包 `0x04`；装备未净化 `0x13`；未修理 `0xD9`；等级不足 `0x0E`；其余无诚实文案时静默。不要复用 `OpeningPayloads.Error`（`0x7E` 在这里是防沉迷）。
3. 材料搜索需覆盖主背包装备栏；按模板找"唯一一件"，件数 ≠ 1 时拒绝（客户端已拦，服务端兜底）。服务端仍须核对净化/耐久/锁定。
4. 消耗行对装备写 `list=0, slot, 1`；产物行首字节写 0 或 1 的取舍需实机核对（第 2.1 节）。
5. 产出装备的配方客户端只允许数量 1；服务端应拒绝数量 > 1。

## 未解问题

- mode1 窗口由何处打开；9B 尾两字节 `00 01` 的用途。
- 7B 构包点 `0x00C56571` 的触发场景。
- 产物行首字节的语义（是否容器选择子）；写 1 时释魂武器的背包显示是否仅靠 NOTI 正确。
- `0x05260C3D` 判定"异界气息"的具体实例字段。
- 实机显示的文案语言（`0x4B1A` 两语种含义不同）。
- 金币显示的数值来源与客户端是否另有金币预检。
- 镇魂礼盒的 PVF 形态（`[select]` 礼包还是其他），以及 JP 是否有不死鸟兑换活动。
