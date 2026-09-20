# T01 漂流洞穴列表为空：A21 客户端闭合结论

日期：2026-09-20。范围仅限 A21 静态客户端、现有 PVF 提取物、T01 wire 与主线程归档的只读运行时读取；没有启动或操作客户端，没有修改角色、数据库、生产代码或共享协议文档。

## 结论

漂流洞穴 150、151、152、153、155 没有进入普通选图记录，直接原因是 **A21 客户端要求地下城定义 quest list 中至少有一条常规任务出现在当前任务容器或已完成任务树；T01 角色两处都没有这些漂流洞穴主线任务**。

当前角色等级 85，等级门已排除。真实 `driftcave.wdm` 十项已完整加载，world-map key 23 也正确。空 `NOTI 0x0005` 只代表没有逐地下城 clearState 条目，不是这道列表可见性门；`NOTI 0x001B` 前十字节也不是 region/mode。

详细指令和运行时证据见 [`ordinary-dungeon-filter-evidence.md`](ordinary-dungeon-filter-evidence.md)。

## 实际构建链

```text
选图入口 RVA 0x02338990
  -> T01 分支把 key 23 传给 VMP 0x053558DF
  -> RVA 0x02339D06 读取 select+0x44C = 23
  -> [VA 0x03A5C934] 查得 driftcave.wdm
  -> RVA 0x02339D21 调 ordinary builder 0x0231D490
  -> 遍历 raw+0x6C..+0x70，stride 12
  -> WDM 条目门 0x00A64AC0
  -> dungeon quest-list 门 0x00A62D90
  -> false 时 RVA 0x0231D748 跳到下一条 WDM 记录
```

真实 WDM manager 是绝对 VA `0x03A5C934`，运行时 type=13。绝对 VA `0x03A5C96C` 的 type=40，唯一缓存资源为 `advancealtar.stm`；此前把它及 RVA `0x02330A50` 当作普通 WDM 路径的证据已删除。

运行时 `driftcave.wdm` 的十项为：

```text
(150,-1,0) (151,-1,0) (152,-1,0) (153,-1,0) (154,1895,0)
(155,-1,0) (239,1925,1) (239,1927,1) (8007,-1,0) (2011,13505,0)
```

这证明 150..155 在 WDM 解析阶段并未丢失。

## 任务门的运行时输入

`0x00A62D90` 读取 dungeon definition `+0x168` 的 quest list，并查两个客户端状态：

- `[manager+8, manager+0xC)`：12 字节记录，T01 只有 quest 2489，与 DB active quest 一致。
- `manager+0xB0` 树：T01 keys 为 101、1016、1776..1782，与 DB 和客户端 `NOTI 0x0164` clear bitmap 一致。

150 的常规主线任务是 1856、1857、1859、1863、1864；151、152、153、155 也各自列出后续漂流洞穴主线。这些任务资源 `raw+0x9C=0`，但没有一条在当前或已完成状态中命中，因此函数返回 false。调用者此时 `BL=1`，在 RVA `0x0231D748` 直接跳过该地下城。

8007 的 quest list 只有 `-1`，同一函数返回 true。154 的 WDM 条目另带 quest 1895，先经过另一道前置门；不把它的完整路径与其余五图合并。

## 证据边界

该证据证明的是 **T01 角色状态下的 A21 客户端过滤行为**。它不能单独证明原版服务端应自动发放哪条任务，也不能用客户端提示替代服务端准入政策。服务端应按 PVF 前置链和领域规则决定应激活/完成的任务，再通过正常任务协议投影给客户端。

运行时还观察到 8007 通过任务门，但用户视觉上仍为空；8007 的后续展示规则未闭合。这不影响 150..155 被任务门排除的结论。

## 最小验证

在隔离测试角色上保持等级、位置、WDM、`NOTI 0x0005` 与 `NOTI 0x001B` 不变，只通过服务端正常任务投影让 150 的关联主线之一进入当前任务容器或已完成树，再重开选图。预期 `0x00A62D90(150)` 改为 true，RVA `0x0231D748` 不再跳过 150。

## 证据身份

- A21 `DNF.exe` SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- dump SHA-256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；image base `0x00400000`。
- `live/t01-dungeon-quest-gate.json` SHA-256：`D90BEEBBA7EBF110C3E3A449B227E6A86640824BD35724B9B2A4E3D604207E02`。
- `live/t01-quest-resource-types.json` SHA-256：`ED513CCCAE58765E6710938F166ED22419AEE2F0DD698CC0B0FB0A9DE2551935`。
- 静态长摘录：`ordinary-wdm-consumer-231d490.txt`、`wdm-entry-predicate-a64ac0.txt`。

证据级别：控制流、布局与本次容器值为 `Confirmed / A21 static + read-only RPM`。
