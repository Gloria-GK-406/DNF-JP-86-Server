# TASK-01 当前 A21 Normal 运行时裁决

## 环境与原始证据

- 接受基线：`d1351ce6047ea84db1c764efc3ad9466781f07af`。
- 当前客户端与 PVF 哈希见 `static-and-capture-prep.md`；本轮未改客户端目录。
- 定向原始帧：`live-targeted-frames.jsonl`，只包含 C2S/CMD `0x000C/0x000D` 及直接相关的
  S2C `NOTI 0x0009/0x000B/0x0099`；同号 S2C `NOTI 0x000D ITEM_LIST` 已由方向与 selector
  白名单排除。
- Host 使用该 worktree 的 Release 产物与 A21 `Script.pvf`，隔离后端端口
  `17001/20010/20011`；客户端通过定向中转连接标准端口。

## Normal target/default

用户从无队状态通过当前 A21 原生 UI 两次创建 Normal 队伍，分别出现以下 C2S `0x000C`：

```text
2026-09-12T10:07:12.579Z  00 03 04 00 00 00 00 05 00 02 00 00
2026-09-12T10:07:36.xxxZ  00 06 04 00 00 00 00 05 00 02 00 00
```

两帧均为 12 字节 direct 形状，容量 4、类别 `Normal=2`、`ModeTargetRaw=0`。Host 均接受并创建
队伍；当前 A21 继续存活并显示队伍 UI。静态 fallback、两份真实创建帧和既有编辑样本均一致，因此：

- Normal canonical target/default 是 `0`：ClientVerified；
- 服务端 `0xFFFF` 不是同义 sentinel，而是旧实现 placeholder，应修正：Confirmed implementation error。

可推翻条件：当前同一 A21 构建存在一个可归因的合法 Normal 创建流程，明确写 `0xFFFF` 且客户端把
它与 target `0` 解释为不同合法生命周期状态。当前两次原生创建及所有静态构包路径均未出现该反例。

## C2S 与 S2C 前导字段不是同序

当前 A21 C2S generic builder 先写对象 `+0x40`，再写标题选择值；第二值为 0 时立即附带 raw-dstr。
固定上游 parser 也把 direct `body[1]` 命名为 `TitleIndex`。相反，S2C `PARTY_INFO` parser 先读
`titleIndex`，当其为 0 时读取 raw-dstr，随后才读取会触发“赛丽亚 Bonus”的标志。

本轮用户选择预设队伍名后，两次 create 只有 C2S `[1]` 从 `3` 变为 `6`；当前实现却把 C2S 12 字节
原样当作 S2C canonical 设置，再把 canonical `[1]` 按产品策略归零。于是回送变成：

```text
titleIndex=0, raw-dstr length=0, return/special flag=0
```

用户可见结果正是“无队伍名”。因此后继实现必须使用以下投影，而不是原样复制：

```text
canonical S2C [0] = C2S [1]  // titleIndex
canonical S2C [1] = C2S [0]  // return/special-party flag
canonical S2C [2..11] = C2S tail
```

对 inline-title 形状同样交换前两字段，并把 raw-dstr 内容单独保存到 `PartyTitle`；S2C 在
`canonical[0] == 0` 时必须回送该标题，而不是无条件写空串。产品规则只把 canonical S2C `[1]`
归一化为 0，不得清除 canonical `[0]` 的预设标题索引。

字段顺序与失败原因：Confirmed（静态读写路径 + 两次当前实帧 + UI 结果）。具体 `3`/`6` 对应哪两条
可见预设文案等待用户补充，只影响索引到文案的展示记录，不影响 wire 投影与修复边界。

## 队长离队与最后成员

两人队成立后，队长客户端发送空 body C2S `0x000D`：

```text
2026-09-12T10:05:44.296Z  C2S 0x000D  bodyLength=0
2026-09-12T10:05:44.299Z  S2C NOTI 0x0009 type=3  同时发给两名成员
```

Host 记录 `LEAVE_PARTY character=1 accepted=True`；用户确认两人队被直接解散。当前
`PartyDomainService.LeaveExactLocked` 对 `slot==0` 无条件清除全体 membership 并删除 party，测试
`MemberLeaveRefreshesSurvivorsWhileLeaderLeaveDisbandsEveryone` 又把该行为固化。

固定上游 `211129e30fb03964659b100e1e57568c8638103b` 的旧版规则是：

- 队长离开且仍有成员：选择最低占用槽成员为新队长，队伍继续存在；
- 最后一名成员离开：队伍为空后才解散；
- 普通成员离开：只清除离队者并刷新幸存成员。

当前 A21 对队长/成员离队都发送同一空 body `0x000D`，角色身份决定服务端领域分支。TASK-04 曾按旧版
规则实现“退休旧 generation/party id，再向幸存者投影新队”，但最终实机中客户端收到 old clear 与
replacement full-state 后，UI 仍明确显示“队伍已解散”。用户据此确认现版本队长离队就是整队解散。

最终裁决：当前 A21 队长离队清除全部旧成员并删除 party；普通成员离队仅清本人并刷新幸存者；单人离队
自然解散。旧版自动转移只保留为冲突来源，不进入当前实现。详见 `../task04/runtime-correction.md`。

## 公开队伍列表（本 Spec 外诊断）

当前服务端只有成员定向的 `PartyRosterChanged` 投影，没有向同城镇/同区域无队玩家发布多块
`NOTI 0x0009` 的目录服务。固定上游已有 `PublishTownPartyListsAsync`：收集同监听端口、同 town/area
可见队伍，向无队玩家发送 `PartyInfoNotiBuilder.BuildList`，并以 type-3 block 删除已不可见队伍。
因此“其他人在队伍信息列表看不到新队”是当前新服务端尚未移植公开目录能力，不是 Normal 设置规则。
它继续保持在本 Spec 的 Out，需独立授权与规格处理。
