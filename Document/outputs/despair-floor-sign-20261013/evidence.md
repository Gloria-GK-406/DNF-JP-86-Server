# 绝望之塔场景楼层牌

日期：2026-10-13。用户报告：实际挑战第 7 层，场景墙上的数字仍为 1。

## 资源与房间

ContentStatic / Confirmed：部署 PVF 的 `etc/towerofdespair.etc` 将第 7 层绑定到地下城 11014、展示 APC 6603。`dungeon/Towers/ZTowerOfDespair007.dgn` 是 1×1、出生点与 BOSS 点均为 (0,0) 的普通迷宫；MAP 池按 `[dungeon] 11014` 选择 `map/towerofdespair_down/15136despair007.map`。该 MAP 自带 NPC 10000、敌对 BOSS APC 10410，并在 `[passive object]` 放置 10386，位置 (350,-100,0)。

10386 对应 `passiveobject/ActionObject/Map/towerofdespair/banner_down.obj`。基础动画为 `map/towerofdespair_down/animation/banner.ani`，另外声明 `DTdeco_banner_0.ani` 至 `DTdeco_banner_9.ani`；楼层数字由客户端组合这些动画，不是每层单独画死在 MAP 中。

## A21 读取与渲染

ClientStatic / Confirmed：客户端 `DNF.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；复核 dump `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin` SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`。以下均为 PE VA，镜像基址 0x00400000。

- `DUNGEON_INFO` handler `01151E10`：`01152522` 读取小地图组数，循环读取每组条目；其后 `01152BF6` 读取一个 u16 到局部 `-0x27c`，`01152C19` 写入当前场景资料 `+0x43c`。无小地图组时该字段位于 body offset 12..13。
- 随后 `01152C1F` 再读 u16，`01152C3E` 写入场景 `+0x444`；再读两个 u8，`01152CD1` 读取既有 offset 18 的 u32 哨兵。不能将 offset 12 的 `01 00` 拆成“固定 u8 1 + 保留字节 0”。第二个 u16 的其他模式用途不在本次修复范围。
- 被动物件工厂 `01BB5B76` 以 `ID - 10381` 索引 `01BC0A14` 的压缩跳表；10386 命中索引 2，落到 `01BB54EF`，调用构造器 `01BC3370`，再调用数字动画初始化 `01BC2F00`。
- `01BC2F00` 的普通地下城分支：`01BC2F5A` 请求模式 11，`01BC2F6A` 取得场景资料，`01BC2F6F` 读取其 `+0x43c`。小于 10 时选对应数字动画；10..99 通过除以 10 计算十位和个位；后续分支处理 100。模式 12 的分支使用 `+0x444`，不据此推定模式 12 的业务含义。

因此本次原因是 `DUNGEON_INFO` 一直发送第一层数字，不是选图进度、地图池选择、`START_MAP` 的 offset 9 房间状态值，也无需修改客户端／PVF。70Reference 的 `CBattle_Field::MakePacket` 在 START_MAP 尾部携带 TOD 层数，只作为版本差异记录，未搬用到 A21。

## 修复与验证

CurrentImplementation：`EncodeDungeonInfo` 将 offset 12 的 u16 改为已冻结的 `layout.Despair.Floor`，普通地下城保持 1；整个无图标报文仍为 32 字节，其他字段不变。楼层来自实际入场 plan，不读取通关后可能已推进的下一层。

自动回归：1、7、10、100 层的入场包楼层字段，及除这两个字节外与普通基线完全一致。RED 为第 7／10／100 层收到 1；修复后通过。测试文件为 `DungeonEntryCodecTests.cs`，TRX 位于 `artifacts/despair-floor-sign-20261013/verification/`。

未部署、无本轮实帧与 ClientVerified。部署后须实机核对：进入第 7 层显示 7；通关留在该场景仍显示 7；下一层入场显示 8；跨十位与第 100 层的数字由客户端原有渲染处理。
