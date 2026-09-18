# 固定上游行为基线

证据基准：`ServerS4A21` commit
`c58cd2b479b0357cb6ca78887c9d638b080f4638`。以下只说明兼容行为，不把上游命名或实现方式升级为当前
A21 客户端事实。

## `0x000C` 与设置投影

- `SetPartyInfoRequest.cs:25..55` 接受 12-byte direct 和 `16 + titleLength` 的 inline-title 两种形状，
  并把它们归一化成 12 字节 `Raw`；只直接解释 `TitleIndex=Raw[1]` 与 `UserMax=Raw[2]`。
- `PartyHandler.cs:532..633` 以“当前是否已有队伍”决定创建或编辑，随后把 `req.Raw` 原样交给
  `PartyManager.UpdateSettings`。创建成功发送 `NOTI 0x0009 type=0`、`0x0099`；编辑广播
  `NOTI 0x0009 type=1`；随后刷新公共列表。
- `PartyInfoNotiBuilder.cs:60..108` 对 type 0/1 写同一个 12-byte 信息块，对 type 0/2 写八槽名册，
  type `<=2` 末尾写 `hasExtra=0`。这与当前 A21 parser 的机械读取一致。

## 上游没有 Story 规则

固定 commit 的 parser、handler、`Party`、`PartyManager` 和通知 builder 中没有
`PartyCategory`/Story 分支，也没有根据 `[10..11]` 查询任务资料。它们只把未知字节保存在
`PartyInfoBlock`，容量和队长权限走共用路径。因此：

- 上游证明“类别 0 与任意 target 可以被透明转发”的兼容面；
- 上游不能证明任意 `u16` 都是合法 Story target，也不能证明 target 是 dungeon/area/quest 中哪一类 ID；
- 上游先创建再更新的非原子流程不属于当前实现要复制的行为；
- 公共列表刷新是 handler 的后置行为，不证明当前 A21 Story 创建依赖 `0x023C`；快速匹配 `0x01BE`
  也未出现在这条 `0x000C` 路径中。

裁决原则：上游与当前 A21 冲突时以当前 A21/实帧为准；本页只保留上游 opaque pass-through 作为兼容
对照。
