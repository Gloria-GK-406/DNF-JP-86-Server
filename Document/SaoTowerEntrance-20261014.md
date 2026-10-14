# SAO 入口恢复与协议调查（2026-10-14）

## 本次修改

用户授权：“那就是没加入到入口是吧，你先加进去然后看看协议是用什么驱动的。”

当前工作分支 `task/next-round-20261013`。本次恢复入口并研究协议；根据入口未显示的实机反馈，追加活动开关接线，不实现 SAO 战斗、门票、推进或奖励服务。

**ContentStatic / Confirmed**：客户端原有 `worldmap/ui/Towers.xui` 已声明
`IDC_WORLDMAP_BUTTON6`，`dungeonIndex=3600`，位置 `(280,150)`，采用
`CNUIControlWorldmapBalloonForRDAR`。原有 `worldmap/Towers.wdm` 的 `[dungeon]`
列表没有 3600；补入 `3600 -1` 后保留全部原入口及顺序。没有改 DGN、XUI 或 EXE。

此前按 `[worldmap pattern info] 1 3` 推测图标重叠不成立；实际布局由 XUI 确定，不将这两个参数解释为屏幕坐标。

`Tool/PvfSaoTowerEntrancePatch` 按客户端、服务端各自的 PVF 生成补丁；二者原始档案存在差异，不能互相覆盖。工具校验已知入口列表、SAO 标记和原生按钮，拒绝重复应用或覆盖输出。

## 内容驱动

**ContentStatic / Confirmed**：

- 首层 `dungeon/Towers/SAO/SAO_TowerOfDespair001.dgn` 名为“艾恩葛朗特”，带 `[tower of despair sao]`、`[no fatigue]`。
- `etc/sao_towerofdespair.etc` 声明每日入场 3 次、最高 100 层，逐层映射 1→3600 至 100→3699；SAO coin 为 10004324。
- 首层声明所需物品 `10004322 1 1`，使用 `[event monster2]`。不能把它直接当成普通地图拼装。
- ETC 的最低等级 10 与首层 DGN 的最低等级 39 不一致，后续功能设计需裁决，不在入口恢复时猜测修改。

## 协议如何驱动

| 阶段 | 当前证据 | 结论与边界 |
| --- | --- | --- |
| 选图入口 | PVF 的 WDM 列表和原生 XUI 按钮 | 初次实机仍隐藏；还需要 EVENT_INFO 活动214，用户已确认追加活动214后入口出现，入场请求待取样 |
| 点击入场 | 原生按钮采用共用选图控件；普通请求是 CMD0010 | **Hypothesis**：沿用 0010，地下城 ID 为 3600；尚无 SAO 点击实帧，不排除特殊分支 |
| 轮次与层数 | NOTI03E9 客户端处理函数 | **ClientStatic / Confirmed**：先读 u32 TryRound，再读 u32 TryFloor；写入 SAO 状态并刷新选图数字 |
| 层数映射 | ETC 的 100 项映射 | 内容提供映射，服务端仍需负责持久化状态和合法目标选择；客户端是否自动重映射入场 ID 待取样 |
| 战斗入场 | 首层 DGN 的特殊事件怪物与现有公共入场链 | 需要 SAO 专用领域处理；不是只补 WDM 就能完整游玩 |
| 通关奖励 | NOTI03EA 客户端处理函数与 sao_clearreward.etc | 专用奖励 UI 通知；字段静态读取如下，最终语义和发送时机待验证 |

普通 CMD0010 的 15B 分支已有独立证据：u32 dungeonId、u8 difficulty、其余状态字段，见
[0010](../protocol/game/client-to-server/0x0010.md)。不能因此宣称 SAO 分支也已 WireCaptured。

### NOTI03E9

绑定只读 B1 EXE SHA-256：`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`，ImageBase `00400000`。
处理函数 VA `01184200` / RVA `00D84200`：

1. VA `01184242` 读 u32 到 `[ebp-418]`；`0118424E` 读 u32 到 `[ebp-414]`。
2. 分别调用 `00E13B50`、`00E13B60` 写 SAO 对象 `+1D0`、`+1D4`。
3. 第二字段传入 `02714AF0`；该函数拆出百、十、个位并设置选图控件图像。
4. 格式串 `Event Dungeon SAO Tower : TryRound[%d], TryFloor[%d]` 的参数顺序直接确认字段名称。

处理函数可见读取为两个 u32。`TryRound` 的业务单位、初始值及循环含义尚未确认。
可选通关公告将 TryFloor=1 显示为 100，否则显示 TryFloor-1，提示它可能代表下一挑战层；不能误当作刚通关层。
没有实帧，完整生命周期和边界仍待验证。

### NOTI03EA

同一 B1 的处理函数 VA `00E15850` / RVA `00A15850`。**ClientStatic / Confirmed** 的主路径读取：

```text
u32 headerA
u16 headerB
repeat 8:
    u8 countA
    repeat countA: u32 valueA, u32 valueB
u32 headerC
u16 headerD
repeat 8:
    u8 countB
    repeat countB: u32 valueC, u32 valueD
```

读取点分别为 `00E158E6/00E158EF`、`00E15A46/00E15A69/00E15A72`、
`00E15AC4/00E15ACD`、`00E15AF6/00E15B09/00E15B12`。
该主路径长度为 `28 + 8 × (全部 countA 与 countB 之和)`；这是静态主路径结构，不是抓包闭合证明。
每组为固定八个槽位，不代表游戏允许八人入场。

**ClientStatic / High**：每对值传入奖励物品显示函数 `025F8DC0`；第一个值用于物品模板/名称查询，第二个用于数量类显示，符合 itemId/quantity。两组奖励的确切业务分类、四个标量的含义未知。函数也会检查当前 UI/模块状态后提前返回，因此发送时序需要结合实机验证。

原始指令摘录见 [客户端静态证据](evidence/sao-client-static-20261014.txt)。自动生成区未手改；人工补证见
[03E9](../protocol/game/server-to-client/NOTI-0x03E9.md) 和 [03EA](../protocol/game/server-to-client/NOTI-0x03EA.md)。

## 部署与验证

已更新常用客户端 `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf` 与运行服务端 PVF 配置。
服务端二进制未变，运行版本 `0.2.5-online-expiry.20261014+snapshot.105b3d1c91c1ea54`。

| 档案 | 原始 SHA-256 | 补丁后 SHA-256 |
| --- | --- | --- |
| Server | 49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A | 9DF742F973C79A10882D80C6D3A46FB34F6691A26A4777F6F261E37061662832 |
| protocol2 Client | F997177D1D7DE7BDD2D832D8533D09A40CB5AC05EFABFCDC5369881914C980C5 | EFDB5B6469949A55427C1058A82187EADF6A59A1F06EBAE239BC339499E4641B |

- 工具 Release 构建无警告/错误；实际两份档案验证均为仅改 1 个文件，新增 2 个 token，未改原 token。
- 逐项核对 712010 个档案条目，712009 个其他条目不变；旧压缩块、名称池/哈希区字节不变。
- 对已打补丁档案再次应用被拒绝，未创建输出。
- 运行配置语义对比仅 `Pvf.ArchivePath` 改变；服务启动 ready，GM HTTP 200，31 个监听端口，SQLite 完整性通过且 schema 不变。
- 部署记录：`artifacts/sao-entrance-20261014/deployment-verification.json`。备份位于同目录 `rollback/`，含原配置、客户端 PVF 和数据库；部署脚本为一次性脚本，不重复运行。
- 尚未更新其他客户端、OneKey 或 ReleasePackage。用户重启 protocol2 后反馈没有入口；追加活动开关修复见下，点击入场采样仍待完成。

本次尚无 SAO 成功入场或 WireCaptured 结论，也没有宣称塔玩法已经可用。下一步先确认真实请求，再设计专用层数状态、入场与通关推进服务。


## 追加：活动开关是第二道入口条件

**ClientVerified（否定结果）**：用户重启客户端后截图仍没有“艾恩葛朗特”。不能把档案补丁通过当作界面恢复完成。

**RuntimeObserved / Confirmed**：只读核对运行中的 protocol2 客户端 PID44824（2026-10-14 18:32:24启动），PVF hash与前述补丁一致。
客户端地下城缓存已经加载3600，DGN info+44C=1；选图最终列表却只有11000、11001、11006、11007、11008。
活动表只有2208、2365，没有214。它证明客户端已经读到补丁中的地下城，排除启动错目录/旧PVF这一假设。

**ClientStatic / Confirmed**：原生选图添加函数 VA02736B90 / RVA02336B90，
在 VA02736CB9..02736CEB 取得DGN，调用VA0171C4D0读取info+44C；该标记为1时，
调用活动管理器检查0xD6（十进制214），未开启就跳到02737646退出，不添加选图行。
活动检查VA014172D0以传入ID查询当前活动树，缺失即返回false。
证据见 [活动过滤静态指令与只读现场](evidence/sao-visibility-gate-20261014.txt)。
这里的214是活动身份；`sao aincrad event`字符串映射中的0x49D是另一种分类值，不能混为活动ID或opcode。

由此，入口驱动关系补全为：

```text
PVF Towers.wdm声明3600 + 原生XUI按钮
    + NOTI006C EVENT_INFO包含已开启的活动214
    → 客户端选图过滤允许添加SAO入口
    → 点击后的实际入场请求仍待取样
```

### 追加修复

- 新增 `SaoTowerEntranceEventDomainService`，注册到既有 `IManagedEventDomainService` 集合。
- 使用现有 `IEventPersistence` 配置启停与有效期；默认关闭。本地测试环境显式开启214，未改签到、马战配置。
- 登录/选角色沿用 `IEventManagementDomainService.GetEventInfoListAsync` → 现有 `EventInformationMessage` → NOTI006C，未新增网络旁路或客户端内存修改。
- LinkKey采用客户端已有的 `sao aincrad event/0/0` 分类。该入口活动仅用于本次协议调查，不能据此认为SAO战斗/奖励服务可用。
- 新增三项回归：开启后进入登录活动列表、关闭后重开数据库仍关闭、账号与有效期边界。测试不依赖真实PVF；首次有效RED为“活动未注册”，早先测试访问内部codec的编译错误不计为行为RED。

完整应用回归3885项通过；GM活动HTTP回归11项通过。无数据库schema变更。

### 追加部署

当前进程PID50792，运行目录 `artifacts/sao-activity-20261014/runtime`，版本
`0.2.5-sao-entrance.20261014+snapshot.1cd86aaf0d4b6916`。
源身份由本次ApplicationModule与新增服务文件内容计算，和部署Application DLL SHA一起写入部署证据。

活动214测试有效期为2026-10-01至2037-12-31。部署只向现有event_configuration增加214，
原2208/2365行逐字保留；已备份数据库和原运行配置，完整性检查通过、schema一致、启动ready、GM200、31端口齐全。
客户端PVF未再次更改，也未部署EXE/DLL patch。

记录：`artifacts/sao-activity-20261014/deployment-verification.json`；备份与活动行回退输入在同目录 `rollback/`。
仅恢复入口列表的部署记录仍保留在 `artifacts/sao-entrance-20261014`，不应将旧进程PID当作当前实例。
第二轮用户确认入口出现，但提示需要“克雷娅的庇护”；入口显示已在该客户端验收，入场请求仍待取样。


## 追加：门票历史期限与 GM 发放（2026-10-14）

**ClientVerified（有限范围）**：用户确认入口已经出现，客户端提示需要“克雷娅的庇护”。这证明活动214修复后的入口显示；不证明入场、战斗或奖励可用。

**ContentStatic / Confirmed**：物品ID10004322，路径
`stackable/event/chn_event_20141111_sao_event/10004322_material_enter.stk`，名称“克雷娅的庇护”，材料类，
说明是亡者峡谷“艾恩葛朗特”的入场券，原声明 `[expiration date] 2014-12-30 06:00:00`。
客户端、服务端原PVF均为此历史期限。

**CurrentImplementation / Confirmed**：GM邮件选择策略 `PermanentOrdinaryItemRules.Allows` 要求
`DefaultPeriodDays==0 && AbsoluteExpiration==0`，因此该门票被搜索用途过滤；发放准备也检查期限并拒绝已过期物品。
这不是缺少名称索引，不能用绕过GM准入或者强塞数据库的方式代替修复内容。

### 内容修复及验证

`Tool/PvfSaoTowerEntrancePatch` 增加 `--ticket` 模式，只对已知ID10004322、名称与历史期限完全匹配的STK删除
`[expiration date]`及其值两个token。其他条目、入口、DGN、XUI和固定过期物品规则不变。

客户端、服务端分别从自己的现用PVF生成补丁。两份均逐条验证712010个档案条目，仅1个STK变化，712009个其他条目不变；
已有压缩块、哈希/名称池字节不变。已打补丁的档案再次应用被拒绝且未生成输出。
原入口模式从原始服务器PVF生成的hash仍严格等于已接受的9DF742F9…，无入口模式回归。
工具Release构建0警告/错误；没有修改服务器程序集或数据库schema。

| 档案 | 本轮部署SHA-256 |
| --- | --- |
| Server PVF | 8FAECD8525E831D17A25CD062B7991CB1E663D3009643BF4C9D2800355A60264 |
| protocol2 Client PVF | D6D5D56EE057D191562D27E14E2DF64D25D011A3CF7C47577475AACAF6528068 |

用户明确回复“已退出”后更新protocol2档案。服务仍使用 `artifacts/sao-activity-20261014/runtime` 原程序集，
当前PID55204，版本仍为 `0.2.5-sao-entrance.20261014+snapshot.1cd86aaf0d4b6916`，仅运行配置PVF路径改变。
数据库已备份，完整性ok、schema一致；ready、GM200、31监听齐全。
本轮证据与回退输入位于 `artifacts/sao-ticket-20261014/`，旧部署脚本为一次性操作，不重复运行。

### 门票发放

GM Web原搜索弹窗保留了过期模板的旧查询结果；精确ID10004322重新查询后显示“克雷娅的庇护”，材料、Lv1、稀有。
通过现有GM邮件预览和发送流程给角色名1123（角色ID2）发放10张；未放宽GM规则或直接改物品数据库。

- 标题：SAO入口测试门票。
- 操作编号：9c60435a-1399-4596-96dc-4e4dbeddb6e8。
- 邮件ID57，附件10004322×10，`expire_time=0`；SQLite只读查询再次核实。
- GM发送详情显示已持久化提交，玩家可领取。邮件本身仍为普通30天有效，不等于门票物品有30天期限。
- 发放证据：`artifacts/sao-ticket-20261014/mail-receipt.json`。

尚待用户重启客户端、领取后点击一次入场；当前SAO专用入场/层数推进/奖励仍未实现。
