# A21 客户端对重复字段的处理（只读静态取证）

日期：2026-11-14
性质：ClientStatic
镜像：`DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144`，地址为 VA（RVA + 0x400000）
脚本与中间件：会话 scratchpad `dupfields/`（扫描器只引用编译好的 `Dfo.Pvf.dll`）

## 结论

- `[attach type]`、`[stack limit]`、`[usable period]`、`[equipment type]`、`[stackable type]` 五个字段，客户端处理器都是普通赋值：重复时**后一次覆盖前一次**，不判重、不报错（Confirmed）。
- 修订版 PVF 中这五个字段的重复共 95 处，**全部在顶层**，没有嵌在任何带闭合标签的块里。
- 只有这五个单值字段适用"后者覆盖"。客户端对重复的处理是逐个处理器决定的，例如：
  - `[usable job]` 只置位不清零，重复时取并集；
  - `[package data]` 先清空再读；
  - `[package data selection]` 每次追加一组。

## 处理器

### stackable 导入 `02999F40`

| 字段 | 处理器 | 行为 |
|---|---|---|
| `[attach type]` | `0299F402` | 查表后写 `[obj+0xC]`；值不认识时不写，保留前值 |
| `[stack limit]` | `029A2127` | 写 `[obj+0x6A0]` |
| `[usable period]` | `029A224F` | 经加密整数 setter 写 `[obj+0x1B4]` |
| `[stackable type]` | `0299FA14` | 写类型名 `+0x160`、类型码 `+0x390`、子类型 `+0x108` |

### 装备导入 `0298B0A0`（由 `02758A2A` 调用）

| 字段 | 处理器 | 行为 |
|---|---|---|
| `[attach type]` | `0298D30B` | 写 `[obj+0xC]` |
| `[equipment type]` | `0298DD3C` | 写类型名、类型码 `+0x3C0`、子类型 `+0x108` |
| `[usable period]` | `0298DF67` | 写 `[obj+0x1B4]` |

## 值不同的重复实例（必须按后者处理）

**`[stack limit]`**
- 10096013、10096014、490000412：1000 → 1
- 10004319：1000 → 100
- 10004858–10004865：1000 → 10
- 10006146：3 → 1000

**`[attach type]`**
- 7224：trade → free
- 10002224、2683715、8048、8049、8267、8268、8221、8225、7981–7984：trade → account

**`[stackable type]`**
- 10099183：`[cera booster]` → `[booster]`
- 490001834：`[expert town potion]` → `[waste]`

## 对旧方案的更正

旧方案说称号 26243、26244 的 `[equipment type]` 有一次写在条件块里。实际两次都在顶层，值都是 `[title name] 1`。
