# A21 PVF：决斗段位与地图

来源：`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`（修订版 PVF，SHA-256 `e1f3a25c…`），
2026-11-14 用 `Tool/PvfProbe dump` 读取。只读档案，未反汇编任何二进制。70 档案
（`Research/tw70-pvf-archive/Script.20130627.pvf`）当前 `Dfo.Pvf` 打不开（签名不符），没有拿来旁证。

## etc/pvp_ref.etc

根级段（`AtRoot()` 后）与本任务相关的：

| 段 | 形状 | A21 值 |
|---|---|---|
| `[pvp experience max grade]` | 单个 Int32 | `21` |
| `[pvp experience]`…`[/pvp experience]` | (段号, 阈值) 对 ×21 | `1 12000, 2 27000, 3 47000, 4 77000, 5 125000, 6 185000, 7 265000, 8 385000, 9 529000, 10 709000, 11 949000, 12 1309000, 13 1597000, 14 1957000, 15 2437000, 16 3157000, 17 3589000, 18 4129000, 19 4849000, 20 5649000, 21 9999999` |
| `[pvp grade point]`…`[/pvp grade point]` | (段号, 胜经验, 负经验) 三元组 ×21（段 0–20） | 每段胜 `1000`；负：段0 `0`、1 `-429`、2 `-460`、3 `-493`、4 `-527`、5 `-563`、6 `-600`、7 `-639`、8 `-681`、9 `-724`、10 `-756`、11 `-788`、12 `-820`、13 `-852`、14 `-884`、15 `-916`、16 `-948`、17 `-980`、18 `-1012`、19 `-1044`、20 `-1076` |
| `[pvp experience loss]`（无闭合） | 两个 Int32 | `20 0` |
| `[pvp normal battle time]` | 毫秒 | `180000` |
| `[pvp relay battle time]` | 毫秒 | `120000` |
| `[pvp normal battle time for tournament]` / `[pvp relay battle time for tournament]` | 毫秒 | `240000` / `180000`（一期不用） |

其他根级段（一期不读）：`[last season pvp reward]` 块（内含重复的 `[reward enable]`、`[reward npc index] 325`、
`[pvp grade] 段 x` 与 `[item]`，所以必须 `AtRoot()` 再读）、`[pvp rank rate grade] 20`、`[pvp rank total cnt] 1500`、
`[pvp rank rate]`、`[pvp direct wait] 5`、`[pvp timeout] 20`、`[pvp rematch] 3`、`[pvp reward item index] 10012664`、
`[pvp reward item count] 5`、`[pvp reward item limit per day] 10`、`[pvp record order]`（职业字符串）、
`[pvp warmup reward]` 块、`[pvp match exp set] 100 -1 -1 50 -1 50`、`[pvp manual match etc set] 45 -1 1 45 -1 0`、
`[reward item by warm up step]`。

### 解读（本仓采用）

- 段位 0–20，满级段 = 20。三处一致：`[pvp experience loss]` 首值 20、`[pvp grade point]` 正好 21 行（0–20）、
  阈值表第 21 行 `9999999` 是哨兵。`[pvp experience max grade] 21` 是阈值行数，不是可达段位。
- 段 g 的下限 = 第 g 行阈值（段 0 为 0），上限 = 第 g+1 行阈值；段 20 的上限是哨兵 `9999999`。
- 胜经验每段恒为 1000，负经验段 0 为 0。按真实数值，一局最多跨一个段；"可跨多段"只能在更陡的表上演示
  （测试用合成表证明）。从一致状态出发，经验下限 0 在真实表上也碰不到（每段下限都大于该段 |负经验|），同样用合成表证明。
- `[pvp experience loss]` 第二值 `0` 语义未知；加载只要求两个值、首值 = 阈值行数 − 1。

### 与上游（ServerS4A21 `PvpRecord.cs` `PvpExperienceRules.Parse`）的差异

- 读取的段相同：`[pvp experience max grade]`、`[pvp experience]`、`[pvp experience loss]`、`[pvp grade point]`；
  满级段同样取 `[pvp experience loss]` 首值。上游另要求第二值为 0，本仓不约束（语义未知）。
- 上游按正则从文本抽数，本仓按 token 读、并只读根级段。
- 上游段 0 胜照加 `[pvp grade point]` 的 1000，满级段胜负都为 0；本仓按 Spec 经验决策表：段 0 首胜直接升到段 1
  下限（12000），满级段胜 0、负照扣。这是 Spec/TaskContract 的已定规则，不是档案读法差异。
- 另一种可能的读法：`[pvp experience loss] 20 0` 意为"段 ≥20 输不扣"。这与 Spec 决策 5b（满级段输了照扣）相反；
  档案本身给段 20 的负经验 `-1076`，本仓按 Spec 执行，记此处备查。
- 上游没有读对战时限；本仓读 `[pvp normal battle time]` / `[pvp relay battle time]`。

## etc/pvpmapbasicdata.etc

```
[pvp map order] 50000 50042 50041 50036 50025 50035 [/pvp map order]
[pvp map basic data] 50000 21 34 1 1 1 0 [/pvp map basic data]
[pvp map basic data] 50042 12 28 1 1 1 0 …
[pvp map basic data] 50041  9 26 1 1 1 0
[pvp map basic data] 50036  6 24 1 1 1 0
[pvp map basic data] 50025 15 30 1 1 1 0
[pvp map basic data] 50035 18 32 1 1 1 0
[pvp map basic data] 50100  0  0 0 1 0 1   （50101、50102、50103 同形，不在 order 中）
```

- `[pvp map order]` 6 项；房间地图字节是 order 下标，0 = 随机（Spec RQ-013），合法下标 0–5，随机从 1–5 抽。
- `[pvp map basic data]` 每行 7 个 Int32，首值为地图号；其余列语义未证。上游把第 5 列（下标 4）当"可用"标志并
  据此过滤可选下标；A21 中 order 内 6 张图该列都是 1，结果相同。本仓不使用未证列，只要求 order 中每张图都有
  basic data 行、每行 7 值。

## 加载失败

两份内容源的必需段缺失或形状不符时 `Load()` 抛 `InvalidDataException`。注意：现行 `ContentWarmup` 对预热失败
只记 Warning、不阻止启动（`Dfo.Server.GameServer/ContentWarmup.cs` 的既定设计），因此"缺段即启动失败"
目前只做到"预热时报警 + 首次使用即抛"。
