# A21 Story 设置字段图

## 结论摘要

Story 类别固定为 `0`。`ModeTargetRaw` 不是普通地下城 ID：它来自客户端当前剧情进度管理器，之后被剧情
条目查找、两目标比较和进度比例函数消费。现有符号不足以给出精确资料表名，因此本单元采用
`StoryQuestTargetId` 作为 **High / semantic alias**，wire 权威名称仍保持 `ModeTargetRaw`。

原生 Story 没有一个可安全硬编码的固定 target default。创建/编辑 UI 会现场查询当前目标；负值路径在
发送前报错，`>0` 路径还会设置两个关联 opaque 字段。`0` 是否是合法当前目标、`0xFFFF` 是否只表示
无目标，必须由当前 A21 原生创建帧裁决。

## 12-byte canonical block

| offset | 宽度 | Story 构包来源 | S2C 消费/约束 | 裁决与置信度 |
|---:|---:|---|---|---|
| 0 | 1 | 专用 builder `0x019E6F08` 写 `1`；通用提交从 UI `+0x40` 写入 | parser 保存，但完整业务消费者未闭合 | Story 专用路径值 `1` 为 Confirmed static；枚举含义 unknown，必须无损保留 |
| 1 | 1 | 在六个预设标题中匹配所得索引；0 时紧随 raw-dstr 标题 | 同一 title/raw-dstr gate | 标题索引/gate 为 Confirmed static；服务端既有 canonical 化政策不等于客户端默认 |
| 2 | 1 | Story UI 对象 `+0x50` / 通用 UI `+0x34` | 共用容量 1..4，且不得低于成员数 | Confirmed shared constraint；未发现 Story 专属上限 |
| 3..6 | 4 | 专用 builder 从对象 `+0x5C` 写 `u32`；通用 Story 在 target `>0` 时由 `0x006A1BB0 → 0x02846D50` 取得并保存 | 具体消费者未闭合 | Confirmed width/source；meaning unknown，禁止归零或据此拒绝 |
| 7 | 1 | 专用 builder 从对象 `+0x60` 写 `u8`；通用 Story 的 target `>0` 路径写 `1` | 具体消费者未闭合 | Confirmed width/source；`1` 仅是通用路径观察，完整枚举 unknown |
| 8 | 1 | 专用与通用提交均写 `0` | 具体消费者未闭合 | Confirmed static value on observed builders；meaning unknown |
| 9 | 1 | 专用 builder 明确写 `0`；通用 UI `[edi+0x44]==0` 选择 Story | parser 存到 party object `+0x60`，Story UI 分支比较 0 | `PartyCategory.Story=0`，Confirmed static |
| 10..11 | 2 LE | `0x03A5C9EC` 上的 `0x00E5A1E0` 当前目标查询结果，经 `WriteU16` 写入 | 保存到 party object `+0x64`；Story UI 交给 `0x006A17D0`、`0x00E5A1E0`、`0x006A1080`、`0x006A0CD0` | wire/source/consumption Confirmed static；`StoryQuestTargetId` 为 High，精确资料表 identity unknown |

## target/default 证据链

1. `0x009C616B`：通用提交的 `[edi+0x44]==0` 分支选择 Story，调用
   `[0x03A5C9EC]::0x00E5A1E0`，结果保存为 outgoing target。
2. `0x00E5A1E0`：没有可用项时返回 `-1`；否则返回当前条目 ID。函数内部还检查活动/有效状态，但精确
   类型名无法从当前符号确认。
3. `0x009C6181..0x009C61AF`：仅当 target `>0` 时取得关联 `u32` 写入 raw `[3..6]`，并把 raw `[7]`
   设为 `1`。`0x009C6407` 对最终 target `<0` 显示错误并跳过 `0x000C`；这并不能推出 target `0`
   一定非法。
4. `0x019E6E7A`：Story 专用自动 builder 仅在现有对象 target `+0x64==0` 时运行，然后重新查询当前
   target 并写入；所以 `0` 同时承担“需要刷新”的内部状态，不能从静态代码擅自映射为 wire sentinel。
5. `0x0240E0E1..0x0240E17E`：收到的 category 0/target 被剧情显示路径消费。它把 target 和当前目标
   同时交给 `0x006A1080` 比较，并把 target 交给 `0x006A0CD0` 计算进度。
6. `0x006A0CD0`、`0x006A1080` 都通过 `0x01083B90` 查找 ID 对应记录；前者读取记录 `+0x34/+0x38`
   形成进度比例，后者比较两条记录的关键字段。结合 dstr 49275 的“共享队长任务经验值”，将其解释为
   剧情任务/场景目标 ID 是 High，而不是仅凭 UI 文本的猜测。

## 标题与容量

Story 专用 builder 在六个内置标题间匹配；匹配失败/索引 0 时写 raw-dstr。容量仍使用共用 1..4，且
通用提交路径会与当前成员数、已有设置逐项比较。没有观察到 Story 专属标题长度或容量规则；因此继续使用
既有严格 UTF-8/title 边界与容量规则，不新增模式特例。

## S2C 与自动刷新

已接受的 `NOTI 0x0009` parser 对 type 0/1 读取同一块：raw `[9]` 保存到对象 `+0x60`，raw
`[10..11]` 保存到 `+0x64`。本单元新增两类 refresh 证据：

- `0x011724C7..0x011724E5` 在通知解析后的状态变化门成立时调用 Story builder；精确门含义保持 unknown。
- `0x00E76790..0x00E767A0` 在 quest-like mutation 结束时，只有当前队伍 category 0 才调用 Story builder。

这些证明 Story target 会随剧情状态刷新，但不证明客户端在每次任务变化都必发，也不证明服务端应该主动
推导 target。
