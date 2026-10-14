# SAO 入口恢复与协议调查（2026-10-14）

## 本次修改

用户授权：“那就是没加入到入口是吧，你先加进去然后看看协议是用什么驱动的。”

当前工作分支 `task/next-round-20261013`。本次只恢复入口并研究协议，不实现 SAO 战斗、门票、推进或奖励服务。

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
| 选图入口 | PVF 的 WDM 列表和原生 XUI 按钮 | 本次恢复静态入口；仍待重启客户端确认实际可见性 |
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
- 尚未更新其他客户端、OneKey 或 ReleasePackage。用户重启 protocol2 后确认入口并点击入场的采样仍待完成。

本次没有 SAO 的 ClientVerified/WireCaptured 结论，也没有宣称塔玩法已经可用。下一步先确认真实请求，再设计专用层数状态、入场与通关推进服务。
