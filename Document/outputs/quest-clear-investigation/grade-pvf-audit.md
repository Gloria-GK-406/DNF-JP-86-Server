# A21任务分类与特殊奖励交叉核对

2026-09-23，只读扫描当前Script.pvf的n_quest/quest.lst，共3441条。以下为PVF标签事实，不把英文字段直接当成客户端中文分类。每个标签取首次出现，职业条件块不覆盖基础分类。

| PVF grade | 数量 |
|---|---:|
| `[epic]` | 757 |
| `[circle]` | 480 |
| `[achievement]` | 466 |
| `[crack]` | 393 |
| `[challenge]` | 291 |
| `[common unique]` | 275 |
| `[title]` | 253 |
| `[side]` | 180 |
| `[daily]` | 127 |
| `[account achievement]` | 100 |
| `[normaly repeat]` | 63 |
| `[system]` | 15 |
| `[daily random]` | 14 |
| `[special]` | 7 |
| `[special daily]` | 7 |
| `[blackhole]` | 4 |
| `[urgent]` | 4 |
| `[realization]` | 2 |
| `[wedding]` | 2 |
| `[sub]` | 1 |

## 关键交叉项

| grade | 转职类声明 | 永久属性声明 | 非item奖励类型 |
|---|---:|---:|---:|
| `[epic]` | 0 | 0 | 0 |
| `[side]` | 0 | 10 | 0 |
| `[common unique]` | 70 | 0 | 61 |
| `[achievement]` | 252 | 0 | 150 |
| `[system]` | 0 | 0 | 2 |
| `[realization]` | 1 | 0 | 2 |

## 奖励类型按grade分布

- `[circle]` / `[circle dungeon]`：480。
- `[crack]` / `[crack of dimension]`：393。
- `[achievement]` / `[awakening type]`：84。
- `[achievement]` / `[title]`：63。
- `[common unique]` / `[grow type]`：42。
- `[normaly repeat]` / `[creature evolution]`：11。
- `[common unique]` / `[ridable]`：10。
- `[common unique]` / `[expert job]`：4。
- `[common unique]` / `[event skill]`：3。
- `[achievement]` / `[hell challenge]`：3。
- `[system]` / `[slot expansion]`：2。
- `[common unique]` / `[event creature evolution]`：2。
- `[realization]` / `[awakening type]`：2。

## 属性与装备槽样本

- 649 穿戴辅助装备的资格 (2/2)：`[system]`，等级['60', '99']。
- 650 佩戴魔法石的资格 (2/2)：`[system]`，等级['65', '99']。
- 13502 [个性技能]异次元碎片：`[side]`，等级['16', '99']。
- 13504 [个性技能]血蝎女王：`[side]`，等级['26', '99']。
- 13507 [个性技能]贪欲的终结：`[side]`，等级['36', '99']。
- 13510 [个性技能]消除异次元裂缝：`[side]`，等级['78', '99']。
- 13513 [个性技能]烈焰之赫尔索斯：`[side]`，等级['83', '99']。
- 12683 [个性技能] 疯狂的世界：`[side]`，等级['53', '99']。
- 12690 [个性技能]亚丁·巴德的贪欲：`[side]`，等级['45', '99']。
- 12689 [个性技能]寻找烈焰波迪尔：`[side]`，等级['68', '99']。
- 12694 [个性技能] 决战哈尔特山：`[side]`，等级['63', '99']。
- 3043 [个性技能]库尔图洛·玛努斯：`[side]`，等级['85', '99']。
- 14820 [个性技能]亚丁·巴德的贪欲：`[circle]`，等级['45', '99']。

主线757条的上述特殊声明均为零。普通任务中文分类映射仍需客户端证据；[normal]在当前清单为零，不能将服务端枚举名称当客户端界面证据。
