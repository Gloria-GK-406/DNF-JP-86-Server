# 镇魂武器开光（emancipate）：客户端静态逆向（A21）

调查日期 2026-11-07，只读。承接 `client-re.md`（那一轮只覆盖设计图 → `0x0019`）与 `pvf-inventory.md`（PVF 盘点）。
本文回答：客户端如何解析 `[emancipate]` 系列标签、开光界面从哪里打开、请求/响应协议、龙跃券 2/3 是否同路、catalog 现状。

## 证据输入

| 项 | 值 |
|---|---|
| 反汇编对象 | `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe`，本次实测 SHA-256 `d7618a102251f1db724f2514c74a4cc855df26e6a4bc308f8ee9f2397db0a99a`，ImageBase `0x00400000` |
| 地址约定 | 下文地址一律 **VA**。协议页机器区写的是 RVA（VA − 0x400000）：协议页构包点 `0x00B662C9 / 0x00C116A2 / 0x02016D1A` 与 handler `0x00D1A950` 即本文 `0x00F662C9 / 0x010116A2 / 0x02416D1A` 与 `0x0111A950` |
| 构包/读包原语（VA） | GetWriter `0x0274CA50`、MakeHeader `0x0274D1E0`、WriteU8 `0x0274D2E0`、WriteU16 `0x0274D310`、WriteU32 `0x0274D340`、Finalize `0x0274E6D0`；ReadU8 `0x027C5230`、ReadU16 `0x027C52A0`、ReadU32 `0x027C5310`（`a21-census-anchors.md` 的 RVA + 0x400000，并用 `0x0241B212` 的已知 `0x0019` 构包点核对过） |
| 加密字面量 | 用 ClientHack `Docs/a21-client-cartography.md` 的字面量算法（`ClientHack/tools/literal_cipher.py` 同算法，改为读本 exe）对 `.code` 段全量试解，得 36,114 条 |
| 文案库 | `DNFClient-A21-debug/dstr.dat`（SHA-256 `da151f22…df43d7`），用服务端仓 `Tool/ClientStringPatch/patch_sao_strings.py` 的 `decode` 解出 `FILE/102`（简中）与 `FILE/104`（日文），均为 UTF-8 |
| 抓包 | 无 `0x010F` 线级样本（协议页「尚无观察记录」） |
| 参照 | 协调方转述的 ServerS4A21 `ChronicleGrowthRequest.cs` / `ChronicleGrowthAckBuilder.cs` / `InventoryEmancipateService.cs`（UpstreamReference，仅线索）；另一线程已报的 `0x00C21B70`、`0x015B2F30`、`0x0111A950` 骨架（本文复核并补全） |

来源标签：ClientStatic（客户端静态指令/数据）、ContentStatic（PVF）、UpstreamReference、CurrentImplementation、WireCaptured、Unknown。置信度：Confirmed / High / Hypothesis。

## 结论摘要

1. **开光走 `CMD 0x010F UPGRADE_CHRONICLE`，不是 `0x0019`。** `0x010F` 是所有 `[emancipate ticket]` 券的统一请求：券 0/1（勇者装备改造）、2/3（龙跃）、4/6/7/8–19（转换书，含镇魂 19）、5（等级开光）共用同一个 19B 布局和同一个响应 handler，只是由 4 个不同窗口发出、文案按券类型分支。——ClientStatic / Confirmed。前一轮把 `0x010F` 定为"只属能力開放"是片面的（那是其中 `0x171` 窗口一支）。
2. **触发方式：右键使用券 → 光标进入"选装备"模式 → 点背包里的目标装备 → 打开窗口 → 点「进化/変換」→ 确认框 → 发 `0x010F`。** 镇魂券 19 打开窗口 `0x173`（简中标题"进化"，日文"変換"）。没有 NPC 菜单入口。——ClientStatic / Confirmed（右键/使用券为 `[emancipate ticket]` 物品的使用分支 High）。
3. **匹配规则**：券 `[emancipate ticket]` 值必须等于目标装备 `[emancipate type]` 值，且目标必须有 `[emancipate]` 块；镇魂 19 的点选检查还硬编码 `type == 0x13`。不匹配文案 `0x8A1B`「该装备无法进化 / 該当装備は変換できません。」——ClientStatic / Confirmed。
4. **镇魂 19 的请求里材料槽/材料 ID 恒为 `0xFFFF / 0x0000FFFF`**（窗口 `0x173` 发送函数写常量），材料由服务端按目标装备 `[emancipate][input]` 自行扣。——ClientStatic / Confirmed。
5. **客户端本地预检**：城镇内、材料够（按 `[input]` 计数，含 `[emancipate material revision]` 修正）、目标装备已修理、不带异界气息、在背包中。**不检查金币**，不检查锁定（锁定只在服务端失败码 `0xD6` 有文案）。——ClientStatic / Confirmed（"不检查金币"= 在所有已读预检路径里未见，High）。
6. **成功响应不带新物品**：`0x010F` 成功体只有 `u8 + u8 N + N×{u8 list,u16 slot,u32 count}` 的扣减行，客户端只做减量/移除、弹「装备转换成功！」。目标装备换成释魂模板必须靠后续库存通知（`NOTI 0x000E` 对目标槽"先清后填"）。——ClientStatic / Confirmed（handler 不写入任何新物品）；通知形态 High。
7. **龙跃 2/3**：同一个 `0x010F` 协议、同一 handler，但走窗口 `0x172`（"升级 / アップグレード"），检查 `[equipment grow]` 等级与熟练度，材料槽同样恒 `0xFFFF`。熟练度/成长值另有 `NOTI 0x01E1 GROWTH_ITEM_RENEW_DATA`、`NOTI 0x01E2 GROWTH_ITEM_CHANGE_DATA`（本次未逆）。——ClientStatic / Confirmed（同协议）；成长通知关系 Hypothesis。
8. **catalog**：`0x010F` 不在 `catalog.md`；仅 `protocol/game/index.md` 记"布局已闭合 19B"，协议页无样本、无人工结论；服务端无实现（`Server/src` 无 `0x010F`/emancipate 处理）。——CurrentImplementation / Confirmed。

## 1. PVF 标签解析（第 1 问）

### 1.1 标签字面量与引用点

字面量全部加密存放（明文搜索为 0），解密后定位到 `DNFShared\GameScript\RDARScriptItem.cpp` 翻译单元（`0x029663D0..0x02999F40`）。

| 标签 | 字面量 VA | 引用指令 VA | 归属 |
|---|---|---|---|
| `[emancipate]` | `0x03636648` | `0x029929E5`（注册为标签 id `0x1319`） | 装备脚本 |
| `[input]` | `0x032F265C` | `0x02994C25` | `[emancipate]` 子块 |
| `[output]` | `0x036359B0` | `0x02994CF3` | 同上 |
| `[emancipate explain]` | `0x0363597C` | `0x02994DB5` | 同上 |
| `[equipment grow]` | `0x03635950` | `0x02994E06` | 同上 |
| `[emancipate type]` | `0x03635924` | `0x02994E7A` | 同上 |
| `[emancipate ticket]` | `0x0363B818` | `0x0299A095` | 可堆叠脚本 |
| `[emancipate grade max]` / `amplify max` / `genuineGrade max` | `0x0363B648` / `0x0363B60C` / `0x0363B5C8` | `0x0299A89A` / `0x0299A8FA` / `0x0299A961` | 可堆叠脚本 |
| `[emancipate material revision]` | `0x0363B580` | `0x0299A9C8` | 可堆叠脚本 |
| `[emancipate grow apply]`…`[/…]` | `0x0363B548` / `0x0363B50C` | `0x0299AA60` | 可堆叠脚本 |
| `[equipment level emancipate]` | `0x0363B798` | — | 可堆叠脚本（券 5） |
| `[emancipate growth item upgrade]` | `0x0362EFF8` | `0x02936767` | 未展开（Unknown） |

ClientStatic / Confirmed。装备脚本解析是"标签字符串→id 的静态表 + 按 id 跳表"：id `0x1319` 的跳表项（索引表 `0x02997FD8`、目标表 `0x02997F90`）经 `0x029929D8` 落到 `0x02994BC5`，该处循环读 `[input]/[output]/[emancipate explain]/[equipment grow]/[emancipate type]` 子标签。

### 1.2 数据结构

**装备模板**（解析器里 `ebx`/`[ebp-0x1584]`，运行时由物品对象 vfunc `+0x138` 取得）：

| 偏移 | 类型 | 来源标签 | 置信度 |
|---|---|---|---|
| `+0x1D9` | u8 | 见到 `[emancipate]` 即置 1（"可开光"标志） | Confirmed |
| `+0x101C` | vector<(u32 id, u32 count)> | `[input]` 成对读到 `[/input]` | Confirmed |
| `+0x1030` | vector<(u32 id, u32 count)> | `[output]` | Confirmed |
| `+0x1044` | string | `[emancipate explain]` | Confirmed |
| `+0x1048` / `+0x1049` | u8 / u8 | `[equipment grow]` 两个整数（龙跃 `1 1`：成长组、当前星级） | 字段 Confirmed；含义 High（`0x02419450` 用 `+0x1048` 查成长表、`+0x1049` 比上限） |
| `+0x1148` | u32 | `[emancipate type]` | Confirmed |

**可堆叠（券）模板**（运行时由物品对象 vfunc `+0x148` 取得）：

| 偏移 | 类型 | 来源标签 | 置信度 |
|---|---|---|---|
| `+0x1D9` | u8 | 见到 `[emancipate ticket]` 置 1（与装备同偏移，物品模板基类字段） | Confirmed |
| `+0x6EC` | i16 | `[emancipate ticket]` 值 | Confirmed |
| `+0x718` | map<key,val> | key 0 = `[emancipate grade max]`，1 = `amplify max`，2 = `genuineGrade max` | Confirmed |
| `+0x740` | vector<(i32 index, i32 percent)> | `[emancipate material revision]` 成对 | Confirmed |
| `+0x754` | 容器 | `[emancipate grow apply]` 块 | 写入点 Confirmed，内部布局 Unknown |

模板 `+0`..`+4` 是受保护（混淆）dword，解出来是物品 ID（`0x016E3C15` 把它连同 `+0x6EC` 交给券上下文）——High。

## 2. 开光界面如何打开与预检（第 2 问）

### 2.1 使用券 → 光标模式

- 使用物品分支 `0x016E3B20`：取物品模板，若 `+0x1D9 != 0`（即带 `[emancipate ticket]`）进入开光流程；窗口 `0x173` 已开 → 文案 `0xEC34`「无法使用其它转换书和设计图。」；`0x016DF240(7)` 为真 → `0x89E9`「该道具将在日后开放」（功能开关，Hypothesis）；否则把（券类型 `+0x6EC`，券物品 ID）交给券上下文 setter `0x00C21B70`。——ClientStatic / Confirmed
- 券上下文单例 getter `0x00C219F0`：`[ctx+0]` = 券类型（读取 `0x00E46B20`），`[ctx+4]` = 券物品 ID（读取 `0x02846D50`）。另一线程报的全局为 `0x039AD0F4`（未在本文复核）。
- `0x00C21B70` 按券类型切光标模式（跳表 `0x00C21D00`/`0x00C21CE0`）：

| 券类型 | 光标模式 | 点装备后打开的窗口（`0x015B2F30` 跳表 `0x015B38B8`/`0x015B3834`） |
|---|---|---|
| 0、1（勇者装备改造券） | `0x28` | `0x171`「改造 / 能力開放」 |
| 2、3（龙跃之息卷轴） | `0x40` | `0x172`「升级 / アップグレード」（另有 `0x280` 窗口相关分支，Unknown） |
| 4、6、7、8–18 | `0x4D`/`0x4E`/`0x5D`/`0x60` | `0x173`「进化 / 変換」 |
| **19（释魂之真灵转换书）** | **`0x61`** | **`0x173`** |
| 5（次元能量补充卷轴） | `0x51` | `0x174`「成长 / 成長」 |

ClientStatic / Confirmed。

### 2.2 点选目标装备（窗口 `0x173` 的资格检查）

`0x01013180`（把点中的物品设为 `[win+0x17C]`）按券类型分派（跳表 `0x01013398`/`0x01013380`），类型 19 调通用物品条件检查 `0x02312480` 的条件 `0x5E`（`0x02318218`）：

| 顺序 | 条件 | 失败文案（简中 / 日文） | 置信度 |
|---|---|---|---|
| 1 | 物品是装备（vfunc `+0x2C(2)`）且有装备模板 | 静默 | Confirmed |
| 2 | 模板 `+0x1D9 != 0` 且 `+0x1148 == 0x13` | `0x8A1B` 该装备无法进化 / 該当装備は変換できません。 | Confirmed |
| 3 | `0x0167A7C0` 为真（已修理/耐久满） | `0x8A28` 未修理的装备无法进化 / 修理していない装備は変換できません。 | 分支 Confirmed；"耐久"按文案 High |
| 4 | `0x05260C3D` 为假（无异界气息） | `0x8A29` 感染了异界气息的道具无法进化 / 異界の気運が宿ったアイテムは変換できません。 | 同上 |
| 通过 | 返回 2 → 调窗口初始化 `0x010124B0`，打开 `0x173` | — | Confirmed |

穿戴中/快捷栏：点选检查里没有单独判定（光标只在背包点选，High）；服务端失败码 `0x13` 有专用文案（第 3.4 节）。锁定：客户端未见本地判定（Hypothesis：交服务端，失败码 `0xD6`）。

### 2.3 窗口 `0x173` 初始化 `0x010124B0`

- 构造函数 `0x01011F60`（`push 0x173`），文案字段：`+0x22C`=`0x8A1F`「进化/変換」（标题），`+0x230`=`0x8A20`「只能在城镇内进行进化」，`+0x234`=`0x8A21`「没有可进化的装备」，`+0x238`=`0x8A22`「缺少进化所需的道具」。——Confirmed
- 把目标装备模板的 `[input]`（`+0x101C`）拷到窗口 `+0x180` 作材料列表，`[output]`（`+0x1030`）拷到 `+0x194`；用 `[output]` 首项 ID 构造预览物品（`0x01A85220`，存 `+0x1A8`）。——Confirmed
- 属性继承预览：`0x0100FCF0(券类型)` 对类型 **1、3、4、6、7、18、19 返回真**，对 2、5、8–17 返回假；为真时把目标的强化/增幅/锻造/附魔等受保护值逐项拷到预览物品，并经 `0x00C21E50` 按券 `[emancipate grade/amplify/genuineGrade max]` 截断。——表 Confirmed；"拷贝属性"含义 High。与 PVF 说明一致：19 保留强化/增幅/锻造/附魔/异次元属性；龙跃 2（初级）不保留、3（高级）保留。

### 2.4 点「进化」→ 确认框 `0x01011B40`

按券类型（跳表 `0x01011F4C`/`0x01011F3C`）选确认文案：

| 券类型 | 确认文案 |
|---|---|
| 4、18 | 若有 max 截断：`0x8A23`「…强化最高为+%d、增幅最高为+%d、锻造最高为第%d阶段。异次元属性和附魔效果保留。」；否则 `0x8A1E`「…强化、增幅、锻造及附魔效果将被重置。」 |
| 6、7、**19** | **`0xBA66`「确定要转化该物品吗？转化成功时，将保留强化、增幅、锻造和附魔效果。」/「該当アイテムを変換しますか？強化, 増幅, 精錬, 魔法付与効果は維持されます。」** |
| 8–17 | `0x8A1E`（重置） |
| 5 | 无文案 |

确认回调 `0x01011830`（按钮 0）以数量 1 调发送函数 `0x01011400`。——ClientStatic / Confirmed

### 2.5 发送前预检 `0x01011400`

| 顺序 | 条件 | 结果 | 置信度 |
|---|---|---|---|
| 1 | `0x0163E1E0() == 1`（城镇状态） | 否 → `[+0x230]` = `0x8A20`，关窗 | 分支 Confirmed；"城镇"按文案 High |
| 2 | `0x016ED890() > 0`（待回执锁） | 直接返回 | Confirmed；"锁"含义 Hypothesis |
| 3 | 可做次数 `0x0100FD90() >= 1` | 否 → `[+0x238]` = `0x8A22` 缺少进化所需的道具 | Confirmed |
| 4 | 目标仍在背包（`0x016EF980` 返回槽 ≠ −1） | 否 → `[+0x234]` = `0x8A21` | Confirmed |
| 5 | 券槽（按券 ID 查 `0x016EF580`）与目标槽均 ≥ 0 | 否则静默返回 | Confirmed |
| 6 | 券 `+0x6EC` == 目标装备模板 `+0x1148` | 否 → `0x8A1B`，关闭窗口 `0x173` | Confirmed |
| 7 | 通过 → `0x016F4F40(0xC)`（推测上待回执锁）→ 构包 | | 构包 Confirmed；锁 Hypothesis |

可做次数 `0x0100FD90`：遍历窗口材料列表 `+0x180` 的每个 `(id, need)`，`need` 先经 `0x00F652C0` 用券的 `[emancipate material revision]`（`+0x740`，index 为 −1 或匹配材料序号时按百分比修正）调整；`have` = 主背包 `0x016EF410(id)` + 全局 `[0x03B3DFB8]` 容器 32 格中同 ID 的件数（该容器身份 Unknown）；结果 = min(have/need)，上限 0xFF；`[output]` 中有装备则上限 1。——Confirmed。**整条路径未见金币读取或比较**——High（PVF 也无开光金币字段；服务端若收费属自定规则）。

## 3. 协议（第 3 问）

### 3.1 请求 `CMD 0x010F`，19B

四个构包点布局完全一致（`0x00F662BD`/`0x01011696`/`0x02416D0E` 处 `push 0x10F`）：

| 偏移 | 宽 | 含义 | 窗口 `0x173`（镇魂 19）实际取值 | 置信度 |
|---:|---:|---|---|---|
| 0 | u16 | 券所在主背包槽 | `0x016EF580(券ID)` | Confirmed |
| 2 | u32 | 券物品 ID | 券上下文 `[ctx+4]` | Confirmed |
| 6 | u16 | 目标装备槽 | `0x016EF980(目标物品)` | Confirmed |
| 8 | u32 | 目标装备物品 ID | 目标物品 vfunc `+0x28` 返回实例数据 `+0x14` 的受保护值 | 宽度/来源 Confirmed；"模板 ID"含义 High |
| 12 | u8 | 校验/混淆字节 | 目标物品 vfunc `+0x148` 返回对象的受保护 dword 解码后的低 8 位（`0x0101163D..0x01011694`，按对象地址低 4 位选 5 种解码器之一） | 来源 Confirmed；若该对象是物品模板，则等于目标模板 ID 的低字节——Hypothesis；服务端用途 Unknown |
| 13 | u16 | 材料槽 | **常量 `0xFFFF`** | Confirmed |
| 15 | u32 | 材料物品 ID | **常量 `0x0000FFFF`** | Confirmed |

其它窗口：

| 发送函数 | 窗口 | 券类型 | 材料字段 |
|---|---|---|---|
| `0x01011400` | `0x173` 进化/変換 | 4、6–19 | 恒 `0xFFFF / 0xFFFF` |
| `0x02416BA0` | `0x171`/`0x172`（同一基类，构造 `0x02417900`） | 0/1、2/3 | 恒 `0xFFFF / 0xFFFF` |
| `0x00F661D0` | `0x174` 成长/成長 | 5 | 窗口 `+0x1DC` 列表首项：其 ID 所在槽 + ID；列表空则 `0xFFFF / 0xFFFF` |

ClientStatic / Confirmed。上游把 13..18 称为"材料（碎片）"，在 A21 客户端里只有券 5 会填。

**与 `0x0019` 的关键差异**：`0x010F` 直接给出目标装备的槽与 ID，服务端能精确定位那一件实例，不需要"按模板唯一"推断。

### 3.2 响应 handler `0x0111A950`

入口先 `0x016F3360`（推测释放待回执锁，Hypothesis）。状态字节 `[ebp+0xC]`、失败码 `[ebp+0x10]` 由 CMD 分派预读。

**成功（状态 ≠ 0）**：

```text
u8  flag                     ; [ebp-2]，仅券 5 分支使用（选 0x33A 窗口参数），其余读后不用
if 券类型 != 5:
  u8  N
  N × { u8 list, u16 slot, u32 count }
```

- 每行：`list` 经 `0x01648AC0` 选容器（0 或 0x1D → 主背包 `[0x03A5C9BC]`，1 → 装扮栏 `[0x03A5C9D0]`，其它见该函数）；取该槽物品，`当前数量 < count` 或 `== count` → `0x016F1300` 整格移除；否则减 `count`。——Confirmed
- 然后按券类型弹提示（跳表 `0x0111AC7C`）：0/1 → `0xA511`「勇者装备改造完成」；2/3 → `0x6A3F`「已完成装备升级 / 装備の成長が完了しました。」并刷新 `[0x03A5C980]` 相关界面；**4 及 6–19 → `0xEC3B`「装备转换成功！/ 装備変換に成功しました。」**；5 → 打开窗口 `0x33A`。——Confirmed
- **handler 不创建、不写入任何新物品，也不改目标槽模板。** 释魂武器出现在背包只能依赖服务端后续通知。——Confirmed

**失败（状态 = 0）**：`u8 码`，`码-1` 查跳表（索引 `0x0111ACCC`，目标 `0x0111AC94`，范围 1..0xD9），部分码再按券类型二级分派。券 19 适用：

| 码 | 券 19 文案（简中 / 日文） | 其它券类型差异 |
|---:|---|---|
| 0x01 | `0xEC01` 错误信息。/ 間違った情報です。 | 同 |
| 0x04 | `0xA508` 该物品格中缺少指定物品 / 該当スロットにアイテムがありません。 | 同 |
| 0x07 | `0x8A28` 未修理的装备无法进化 | 0/1 `0xA50B`；2/3 `0x6A36`；5 `0xEBFF` |
| 0x0D | `0xEC04` 装备Lv过高。 | 同 |
| 0x0E | `0xEC03` 装备Lv过低。 | 同 |
| 0x11 | `0xA507` 无法对当前物品使用该功能 / 該当機能のアイテムではありません。 | 同 |
| 0x13 | `0x8A27` 物品快捷栏或穿戴中的道具无法进化 | 2/3 `0x6A45`；5 `0xEBFE`；0/1 `0xA50A` |
| 0x16 | `0x8A26` 缺少进化所需的道具 / 変換に必要なアイテムが足りません。 | 0/1 `0xA509`；2/3 `0x6A44`；5 `0xEBFD` |
| 0x1A | `0xEC05` 装备删除失败。 | 同 |
| 0x1B | `0xEC25` 该物品禁止使用 / 許可されていないアイテムです。 | 同 |
| 0xAF | `0x8A29` 感染了异界气息的道具无法进化 | 0–3 `0xA50D`；5 `0xEC00` |
| 0xD6 | `0xA4F8` 在物品已锁定状态下，您无法进行当前操作。 | 同 |
| 0xD9 | `0xEC02` 装备Lv不符合。 | 同 |
| 其它 | 静默 | |

ClientStatic / Confirmed（码→文案 id 由跳表读出，文案为本次 dstr 解码）。

### 3.3 成功后客户端如何得到释魂模板

- CMD 成功体只扣减。若服务端在扣减行里写目标槽（`list=0, slot=目标槽, count=1`），客户端会把镇魂武器整格移除；随后需要 `NOTI 0x000E UPDATE_ITEM_LIST` 把释魂武器放入（同槽或新槽）。若不写目标槽，则 `NOTI 0x000E` 必须对同一槽做"同格不同 ID"替换——catalog 记录该情形客户端有限制、修复方案是"先清后填"（CurrentImplementation，待实机）。——推导 High；哪一种更稳需实机。
- 券（10150749）与 `[input]` 五种材料的扣减可以放进扣减行（`list=0`，堆叠数 `count`），也可以只靠 `NOTI 0x000E` 的绝对值。——High
- 属性继承：客户端预览按 2.3 节拷贝目标的强化/增幅/锻造/附魔并按券 max 截断（券 19 无 max 字段 → 不截断）。新物品真实属性完全由服务端在 `NOTI 0x000E` 里给出，客户端不会自己改写。——High

## 4. 龙跃券 2/3（第 4 问）

- **同协议**：`0x010F` 同一 19B 布局、同一响应 handler；发送函数为 `0x02416BA0`（`0x171`/`0x172` 共用），材料字段恒 `0xFFFF`。——ClientStatic / Confirmed
- **不同窗口与规则**：窗口 `0x172`（构造 `0x02417E30`，文案 `0x6A39` 升级、`0x6A3A` 装备升级信息、`0x6A3B` 只能在城镇中升级装备、`0x6A3C` 没有可以升级的装备、`0x6A44` 缺少装备升级所需道具）。点选检查 `0x02419450`：
  - 目标需有 `[emancipate]`（`+0x1D9`）且 vfunc `+0x124` 为真，否则 `0x6A37`「无法用于该装备」；
  - 用 `[equipment grow]` 的组（`+0x1048`）查成长表 `0x03A75B34`（`etc/equipment_grow.etc`，High），当前星 `+0x1049` 已达上限 → `0x6A43`「该装备已达到升级上限」；
  - 物品熟练度（实例受保护值 `+0x88/+0x8C`）< 该星级所需值（默认 100）→ `0x6A41`「可以在成长值达到100%时使用」；
  - 未修理 → `0x6A36`；异界气息 → `0x6A38`；
  - 通过后 `0x02417F20` 载入 `[input]` 材料（`10000224` 小铁柱的火炉使用券 ×1）。
  ——ClientStatic / Confirmed（分支与文案）；字段含义 High。
- 券 2 与 3 的差别在客户端只体现于属性继承判定（`0x0100FCF0`：2 不继承、3 继承）和 `[emancipate material revision]`（券 3 `-1 100`，作用于所需数量）；`[emancipate grow apply]`（`+0x754`）的读取点本次未找到——Unknown。
- 熟练度（成长值）的增长与刷新另走 `NOTI 0x01E1 GROWTH_ITEM_RENEW_DATA` / `NOTI 0x01E2 GROWTH_ITEM_CHANGE_DATA`（字面量 `0x03249B08`/`0x03247BA0`，引用 `0x01188207`/`0x0116B900`）。它们与"升星"本身是否相关未逆——Hypothesis。
- 成功提示 `0x6A3F`；龙跃升星结果同样不在 CMD 里，依赖库存通知。——Confirmed / High

## 5. catalog 与实现现状（第 5 问）

| 项 | 现状 |
|---|---|
| `CMD 0x010F UPGRADE_CHRONICLE` | **未登记 `catalog.md`**；`protocol/game/index.md:245` 记"布局已闭合 19B"；协议页 `game/client-to-server/0x010F.md` 机器区字段含义取自上游（偏移 12 的 u8 为 unknown），人工区"尚无观察记录"；无样本 |
| `0x010F` 响应 | 协议页只登记 handler（RVA `0x00D1A950`），成功/失败布局未写入资料库（本文第 3.2 节可补） |
| `NOTI 0x000E UPDATE_ITEM_LIST` | ClientVerified（金币、分解）；同格不同 ID 先清后填 = ClientStatic + CurrentImplementation，待实机 |
| `NOTI 0x01E1` / `0x01E2` | index 记"布局部分可得"，未实现 |
| 服务端 | `Server/src` 无 `0x010F` 解包器/handler，无 emancipate 内容源；仅 `ItemSnapshot.EmancipateEquipmentLevel`（核心字节 0x4B，属券 5 体系，Hypothesis） |

## 6. 给实现的直接建议（证据驱动）

1. 镇魂→释魂开光按 `0x010F` 实现：解析 19B；以偏移 6/8 定位目标实例、0/2 定位券；忽略 13..18（券 19 恒 `0xFFFF`）；偏移 12 的 u8 先不校验（含义未定）。
2. 服务端校验：券 `[emancipate ticket]` == 目标 `[emancipate type]`（19）；目标在主背包、非穿戴/快捷栏（失败码 `0x13`）、已修理（`0x07`）、无异界（`0xAF`）、未锁定（`0xD6`）；材料按目标 `[emancipate][input]` 计数（不足 `0x16`）；不收金币（客户端无金币概念）。
3. 成功：回 `0x010F` 成功（`u8 flag=0`、`u8 N`、扣减行：券 1、五种材料、视方案含目标槽），再发 `NOTI 0x000E` 放入 `[emancipate][output]` 模板并继承强化/增幅/锻造/附魔/异次元属性；客户端弹「装备转换成功！」。
4. 龙跃 2/3 可复用同一入口，按券字段与 `[equipment grow]` 分支；熟练度门槛（100%）需与 `NOTI 0x01E1/0x01E2` 一并设计。

## 未解问题

- 偏移 12 u8 的确切来源对象（vfunc `+0x148` 在装备物品上返回什么）与服务端用途；需实机改值差分。
- 扣减行是否应包含目标槽，以及 `NOTI 0x000E` 同格替换在 `0x010F` 后的实际表现。
- `[emancipate grow apply]`（`+0x754`）的消费点；券 2 "第 4 阶段起必需"的客户端判定位置。
- `[0x03B3DFB8]` 32 格容器的身份（材料计数也算它）。
- 成功体首字节 `flag` 在非券 5 路径的含义。
- 实机显示语言（简中/日文）。
