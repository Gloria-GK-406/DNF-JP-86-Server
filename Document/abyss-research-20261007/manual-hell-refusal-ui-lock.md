# 主动深渊缺票拒绝后选图锁定

状态：根因已定位；修复方式待定，尚未修复或重新部署。2026-10-07。

## 现象与现场证据

缺票入场后正常显示缺票提示，可以返回城镇、再次请求入场，但不能更换地图或难度。

有效故障现场：`artifacts/manual-hell-refusal-20261007/failure-held-ui.json`。PID 38672，选图模块地址 `0x4B8A0000`。只读采样，没有改写客户端内存。

| 字段 | 故障现场 | 意义与证据 |
| --- | --- | --- |
| 模块 `+0x334` | 0（解码后） | 普通入场等待已经解除，ClientRuntimeReadonly |
| `+0x57EAE0` | 1 | 深渊选择缓存仍生效，ClientRuntimeReadonly |
| `+0x57EAE4` | 7 | 缓存的选图候选索引，不是地下城内容 ID |
| `+0x57EAE8` | 0 | 缓存的难度 |

此前 `failure-ui.json` 采样前已经返回城镇，不是有效故障现场；旧控件可见性和悬停索引也不能证明地图控件解锁。

## 客户端静态路径（High）

目标：`E:/Project/DNF/DNFClient-A21-protocol2/DNF.unpacked-b1-debug.exe`，SHA-256 `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`，ImageBase `0x00400000`。下列地址均为 RVA。

1. `0x0232E075..0x0232E091`：主动深渊发送 CMD `0x0010` 后，将缓存标志设为 1，保存地图候选索引和难度。
2. CMD `0x0010` 返回处理 `0x00D0C080`：失败时调用错误展示，然后调用 `0x02313D10(0)` 清普通等待、`0x0231AF90` 刷新相关 UI；这条路径没有清深渊选择缓存。
3. 错误 `0x1B` 展示缺票提示。它不清该缓存；公共的特殊拒绝处理 `0x000C16E0` 只处理 `0x97/0x98`，不处理 `0x1B`。
4. `0x0231E60D`：缓存生效时跳过一条交互更新路径。
5. `0x02320B03..0x02320B2E`：缓存生效且索引/难度有效时，用缓存值覆盖新选图参数。这解释地图和难度同时无法更换。
6. `0x023126C0(0)`：客户端原生缓存清理函数，清标志并将两个缓存值置为 -1。
7. `0x023397C2..0x023397DB`：本地深渊模式操作存在同样的缓存清理分支。用户随后确认点击普通模式后 UI 解锁（ClientVerified，限于该操作与现象；切换后的内存值未再次采样）。

复核输出：`artifacts/manual-hell-refusal-20261007/final-causal-path.asm.txt`、`hell-cache-reset.asm.txt`、`hell-cache-use.asm.txt`、`new-ui-reject-handler.asm.txt`。

## 服务端与参考实现

CurrentImplementation：当前发送 CMD `0x0010` 拒绝 `[00,1B,00]`，恢复入场意图，没有扣票或创建成功入场状态。普通等待标志已清零与返回处理一致。不能把本轮问题解释为完全没收到响应。

70Reference：`CParty::CheckHaveHellPartyPassItem`（VA `0x085A7B8A`）发送 `[00,1B,u8人数,u16成员槽位...]` 后返回失败。列表不是角色 ID：`CheckDestroyConditionSealDoor` 遍历槽位 0～3，VA `0x085A7FF4..0x085A8001` 直接将循环槽位加入缺票列表。单人队长缺票时正文为 `00 1B 01 00 00`。

## 70 外层拒绝链复核（2026-10-07，70Reference / High）

```text
dispatch_template
  -> DisPatcher_SelectDungeon::process
     -> CParty::dungeon_start
        -> CheckHaveHellPartyPassItem
           -> 检查所有有效队员并收集缺票槽位
           -> 广播一次 CMD 0x0010: 00 1B count [u16 slot...]
           -> party+0x1865 = 0，返回 false
        -> 返回 0（VA 0x085A0CC8..0x085A0CD4）
     -> 未将返回 0 写成新的错误，process 返回 0
  -> DisPatcher_SelectDungeon::send
     -> 三个错误字段均为 0，无追加回包
  -> check_state（状态检查，无恢复投影）
```

参数构造函数 `0x08230DD8` 将 `+4/+8/+0xC` 错误字段初始化为 0；`process` 在 `0x081C8E17` 仅将大于零的 dungeon_start 结果写入后两个字段。`send` 在 `0x081C8EE5..0x081C8EF7` 因两个字段均为零直接结束。`send_to_party`（`0x0859D14E`）只逐有效队员发送给定包；`PacketGuard` 析构只归还包缓冲/记录错误，没有隐藏恢复包。

70 普通拒绝不是统一三字节：

| 分支 | 正文 | 收件人 |
| --- | --- | --- |
| 通用前置错误 | `00 error` | 请求者 |
| 同一错误影响全队 | `00 error`；错误 0x16 追加 `u8 slot` | 全队 |
| 请求者与队伍错误不同 | 按各自错误分别发送；队伍错误 0xAD 追加 `u8 slot` | 请求者或全队 |
| 深渊缺票 | `00 1B count [u16 slot...]` | 全队 |

这是 70 的证据，不是 A21 缺票完整回包布局或行为的确认。A21 已分析的 0x1B 分支读公共第三字节后展示提示，没有发现继续读取该列表或清深渊缓存；不能仅复制变长字段就宣称解锁，也不能据此判断必须 patch。应先解决完整拒绝载荷的版本差异，再确认客户端的原生恢复路径。

本轮输出：`artifacts/manual-hell-refusal-20261007/70-refusal-whole-chain.asm.txt`、`70-refusal-dispatch-gates.asm.txt`、`70-party-send.asm.txt`、`70-generic-cmd-error-body.asm.txt`；缺票内层及 dungeon_start 见既有 `Document/abyss-research-20261007/evidence/70-entry.asm.txt`、`70-callers.asm.txt`。没有修改生产源码或部署运行时。

## 修复边界

- 已分析的 CMD `0x000F` 成功响应与 NOTI `0x001B` 选图消息没有该缓存清理调用，不应盲补。
- NOTI `0x0018` 城镇成员消息确实调用缓存清理及取消深渊模式，但它还处理城镇场景；当前会话已离开城镇成员集合进入选图。不能为清一个 UI 标志而伪造城镇成员消息。
- 完整返回城镇可以重置，但会关闭当前选图界面，属于体验变化。
- 保留原选图界面的候选修复：客户端兼容层在主动深渊入场失败时调用原生 `0x023126C0(0)`，保留缺票提示及原有等待/UI恢复。此方案尚未实现、未部署；需明确客户端修改范围并完成真实客户端验证。

验收应覆盖：缺票提示、地图切换、难度切换、继续选择深渊再次请求、返回城镇、材料足够时正常入场。当前只完成根因取证。

## 三个子代理独立复核后的无 patch 方案

用户要求：使用子代理继续追踪，并提出可能不需要 patch 的修复方案。本阶段只研究和提出方案，未实施、未部署。

### 新确认与排除

- A21 CMD 外层 `0123BF26/0123BF73` 读取 result/error，经 `0123C078 → 0123770C` 直接调用注册的拒绝 handler。回调返回后没有消费 70 的缺票成员列表。`0x1B` 的第三字节在已见路径不参与缓存清理；补齐列表不能作为已知修复。
- 现有 ProfileReadFix 修配置文件 API，不是网络解包修复；其源码不干预 CMD0010。此结论未包含部署 DLL 构建身份验证。
- NOTI0003 的角色 wire state 与全局 main state 是不同枚举。wire 0→1 自身不清缓存。全局新状态 0/5 有间接清理路径，但城镇是 main state1，不能混用。
- NOTI0018 前段 `00D936A0/00D936B4` 清缓存并切普通模式；后段 `00D93AA7` 进入城镇 main state1。NOTI001B 开头 `00D901C4` 进入选图 main state2。001B 单独重发不是缓存清理方案。
- 70 缺票标志 party+0x1865 没有已发现的定时器恢复消费者；NOTI00CF 是深渊累计量，不是选图解锁通知。

### 候选修复：真实取消选图，再自动重开

```text
主动深渊入场失败
  → CMD0010 缺票拒绝
  → CancelDungeonSelection：取消当前选图意图，恢复真实原城镇成员关系
  → 完整回城投影：NOTI0003(wire0) → NOTI0017(原位置) → NOTI0018(真实成员)
     [客户端原生清缓存、切普通模式]
  → OpenDungeonSelection：重新记录原入口意图、真实离开城镇成员集合
  → 重新建立选图投影：USERINFO → NOTI0003(wire1) → 001A → 0005 → 001B
```

取消和重开应是明确的领域操作；HTTP/协议 handler 只驱动，不通过伪造 CMD 请求调用旧 handler。主动取消时不伪发 CMD0084 ACK；没有客户端 CMD000F 请求时不伪发其 ACK。若重开过程中客户端自己发送000F，应正确处理真实请求并保证幂等。

第一段的原生清理依据较强；用户已验证手动切普通模式及回城重开恢复，但服务端主动取消序列尚未实机验收。第二段自动重开仍为 Hypothesis：001B 未见必须有000F等待门，但需要真实客户端测试。预期可能短暂关闭并重新打开选图，回到普通模式；不能承诺保留原地图、难度或深渊勾选。

实施时应校验当前会话代次、队伍成员与原选图意图，防止过期拒绝覆盖新选图；无成功入场 run、不扣材料。组队按同一选图 cohort 恢复，不能只恢复请求者。复用现有 TownAreaProjectionDelivery 与选图投影，位置取当前位置，不取登录出生点。

推荐验证顺序：先只验证真实取消回城，确认缓存清零、原位置及队伍成员状态一致；再单独加入自动重开，确认地图/难度可切、重新启用深渊可请求、0084可退出，且没有重复开启。第二段不稳定时保留明确的回城行为，不堆叠猜测通知。

独立报告：[A21拒绝外层](../../artifacts/manual-hell-refusal-20261007/a21-refusal-agent/report.md)、[原生恢复路径](../../artifacts/manual-hell-refusal-20261007/a21-restore-agent/result.md)、[70与上游](../../artifacts/manual-hell-refusal-20261007/refusal-server-agent/findings.md)。三个报告均为静态/源码调查，不代表候选回包已 ClientVerified。

## 地图和难度输入链复核

证据类别：ClientStatic + 既有 ClientRuntimeReadonly。只读检查，未修改客户端、服务端或部署。所有地址均为上述客户端的 RVA。

### 输入与选择提交是两个不同层次

- 地图控件处理 `0222CD50`：先检查控件输入检测函数 `023DCC30`，再检查选图模块 `02312440`（`module+0x500/+0x501`）和 `02312330`（`module+0x1A8`）。满足条件后，`0222CECD` 或 `0222CFA0` 调用公共选择提交 `02320AD0`。`023DCC30` 检查控件 `+0x31` 并比较输入标识 `+0xD4`；不能把它简单命名为 Enable 或鼠标按下回调。
- 难度按钮输入链：`0233AD38` 检查 `module+0x248` 控件，允许后在 `0233AD71` 将当前难度减一；`0233AD9D` 检查 `module+0x250` 控件，允许后在 `0233ADD6` 将难度加一。分别在 `0233AD76/0233ADDB` 调用 `02312D90`；其满足候选和难度范围等条件时，通过 `02312E08` 调用 `02320AD0`。
- 键盘难度函数 `02326F80` 有原生字符串 `CNSelectDungeonModule::procKeyboardSelectDifficulty`，根据可用难度计算新值后，在 `02327488` 调用 `02320AD0`。
- 键盘地图函数 `02335360` 有原生字符串 `CNSelectDungeonModule::procKeyboardSelect`，`0233627D/02336617` 同样调用 `02320AD0`。这些地址属于键盘处理，不能当成已确认的鼠标点击入口。

公共函数的真实起点为 `02320AD0`。在 `02320B03..02320B2E`，若深渊缓存启用且缓存地图、难度不是 -1，就将传入的新地图索引、新难度替换成缓存值，随后按旧值应用选择。伪代码：

```text
ApplySelection(requestedMapIndex, requestedDifficulty):
    if hellSelectionCache.active and both cached values are valid:
        requestedMapIndex = hellSelectionCache.mapIndex
        requestedDifficulty = hellSelectionCache.difficulty
    updateSelection(requestedMapIndex, requestedDifficulty)
```

### 正常与故障现场的对照

必须比较 `normal-ui.json` 和 `failure-held-ui.json`，不能使用采样前已回城的 `failure-ui.json`。

| 字段 | 正常选图 | 缺票后停留选图 |
| --- | --- | --- |
| 已解码的入场等待标志 | 0 | 0 |
| module+0x1A8 | 0 | 0 |
| module+0x500 / +0x501 | 0 / 0 | 0 / 0 |
| module+0x6BC | 0 | 0 |
| 深渊缓存 active | 0 | 1 |
| 缓存地图索引 / 难度 | -1 / -1 | 7 / 0 |

已采样的控件和容器不足以覆盖所有地图图标与难度按钮；容器内存一致不能证明其所有子控件均启用。本轮没有进行实际鼠标点击的动态断点跟踪，尚不能证明故障现场每次点击都通过了控件检测。但已经确认：即使输入通过检测，新选择仍会被残留深渊缓存覆盖；故障现场上述模块级输入门均为 0。因而该问题具有明确的选择提交层锁定机制，不能笼统归因为没有收到 CMD 拒绝响应或所有按钮被 Disable。

用户实测切普通模式解锁，与 `023397C2..023397DB` 清除缓存的静态路径吻合。下一步应继续定位原生协议如何终止这份深渊选择缓存生命周期；本结论不代表已经证明必须 patch，也不代表已找到新的单包解锁协议。
