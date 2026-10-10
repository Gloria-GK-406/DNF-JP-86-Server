# 绿名经验与结算逐怪列表

日期：2026-10-10。范围：服务端经验规则、Cell 分类、个人击杀账本、NOTI 0x0023；无客户端 patch、无 PVF 修改。

## 证据边界

- **ClientStatic / Confirmed**：A21 原版 DNF.exe SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
  使用 `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，镜像基址 `0x00400000`。
  同目录 `capture-evidence.py` 只读输出 dump 身份与相关指令，`client-static.txt` 保存本次结果。以下地址均为 RVA。
- **70Reference**：`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256
  `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。
  `map_monster::isNamedMonster` VA `0x08151182` 使用 50000..51000、56000..60000；
  `getMonsterTotalExp` VA `0x085A23DC` 为 named 叠乘 3，并保留 worth /2。它不是 A21 实机证据。
- **UpstreamReference**：`ServerS4A21/Game/Dungeon/DungeonExperienceCalculator.cs` 与
  `DungeonParticipantExperienceRuntime.cs`、`Network/Builders/Dungeon/DungeonNotificationBuilder.cs`。
  上游读取 DGN `[named monster]`，并以 actor sequence 发送逐对象经验；其 named/类别互斥、没有 /2，未照搬这些差异。
- **CurrentImplementation**：本次自动测试证明服务端计算、去重、字段编码；显示效果仍待用户实机验收。

## A21 分类与结算键

1. `0x02791EF0` 判定三个闭区间：50000..51000、56000..60000、110900..110999；随后查询全局额外名单。
   虚方法包装 `0x014C91A0` 取怪物 Code 后调用此函数；vtable +0x44 的 getter `0x014A7F90` 读 actor +0x370。
   61000..65999 不是这里的 named 范围，不能因工厂分流范围相近就全部按绿名计算。
   全局名单与 DGN tag 的完整客户端装载链尚未证明；DGN 扩展名单来自上游与实际内容证据。
2. `0x01546C60` 创建 52 字节结算行：+0 为类别、+4 为 Code、+0x10 为死亡处理传入的房间怪物序号。
   调用点 `0x00D5554A..0x00D55559` 将 u16 序号扩宽传入。
3. NOTI 0x0023 将 N 对 `(u32 key, u32 exp)` 经 `0x01546A40` 写入字典，面板 `0x021E9595` 用行 +0x10 调用
   `0x01546A60`；未命中返回 -1，面板转成 0。之前服务端总发 N=0，是列表 +0 的直接原因。
4. 分类展示顺序为 Boss、Champion、Named、Ordinary。named 属性与类别独立；经验仍可以叠乘。

## 实现规则

`shared = floor(worth /2 × partyRate × difficultyRate × dungeonWeight × rankRate × namedRate × monsterOverallRate)`

`base = floor(shared × levelGapRate / partySize)`，namedRate 为 3 或 1，其余参数与现有实现一致。

DGN 扩展名单进入不可变 Layout；创建 Cell 时冻结 IsNamedMonster。APC、dummy、深渊模板继续遵循原有经验资格。
死亡完成持久化发奖后，在原有 participant claim 提交中记录 `(Cell, sequence, Code, Rank, IsNamed, Breakdown)`。
每位角色独立去重，失败不记账、重试不重复；结算仅投影 Base 到逐怪列表，类别和成长契约摘要保持原语义。
不增加数据库表，不更改通关经验公式，不把击杀经验再加入通关发奖。

客户端 wire 字典只有 sequence，跨房碰撞时后写覆盖；服务端内部账本仍以 Cell+sequence 隔离。本次保留现有序号生成算法，
未扩大为全副本序号分配重构。理论低经验（等级惩罚、下取整）仍可能合法显示 +0。

## 验收

自动测试报告保存在主仓 `artifacts/named-monster-experience-20261010/tests/`。
实机需重新登录并新开地下城：观察具名怪逐行经验、类别摘要与击杀所得；翻牌正常；结算只增加通关所得。
运行身份与回滚基线记录在同一 artifacts 目录的 `deployment.json`，部署后核验。
