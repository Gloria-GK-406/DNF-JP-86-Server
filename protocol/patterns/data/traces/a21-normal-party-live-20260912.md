# A21 Normal 队伍创建、标题与离队实机闭环（2026-09-12）

## 固定环境

- 客户端 `DNF.exe` SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 调试 EXE SHA-256：`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
- PVF SHA-256：`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。
- 定向原始帧：spec-002 `evidence/task04/live-final-targeted-frames.jsonl`；只把与结论直接相关的最小
  body 另存到 `samples/`。

## 无队 Normal 创建与标题

当前 A21 原生创建 UI 发送 12 字节 direct `0x000C`：

```text
00 03 04 00 00 00 00 05 00 02 00 00
```

wire `[0]=0` 是 special/return flag，`[1]=3` 是预设标题索引，`[2]=4` 是容量，`[9]=2` 是
Normal，`[10..11]=0` 是 Normal target。服务端将前两字节投影为 canonical S2C 顺序后创建
`partyId=3`，回送 type-0：

```text
01 00 03 00 00 03 00 04 00 00 00 00 05 00 02 00 00 ...
```

客户端继续存活并正确消费 `titleIndex=3`；这与此前未交换字段时显示“无队伍名”的失败形成对照。
因此 Normal target `0`、C2S 前两字节交换和预设标题回送均为 `ClientVerified`。inline raw-dstr 标题
仍只有静态与自动化证据。

## 队长离队

两人 Normal 队伍中，队长在 `game-10010-0004` 发送空 body `0x000D`。当时的服务端实验实现向两端先发
旧 `partyId=1` clear，再向幸存者额外发送新 `partyId=2` type-0：

```text
10:45:01.833Z  member/leader  01 00 01 00 03
10:45:01.835Z  survivor       01 00 02 00 00 ...
```

Host 记录请求接受，并在后续查询中把幸存者报告为 `inParty=True`；但当前 A21 UI 明确显示“队伍已解散”。
用户据此确认现版本规则是队长离队直接解散，而非旧版上游的自动转移。最终服务端实现删除 replacement，
只向全部旧成员发送旧 party type-3 clear。

该 UI 结果与“服务端确实发送了 replacement”同时成立，构成对自动转移兼容方案的直接反证。当前 A21
队长离队整队解散为 `ClientVerified`；旧版上游行为仅作冲突来源保留。

## 最后一人离队

实验 replacement 中唯一成员随后发送空 body `0x000D`，收到：

```text
01 00 02 00 03
```

UI 回到无队状态。最终实现中的单人队长离队走同一个“队长解散全队”路径。

## 仍未验证

- inline raw-dstr 自定义标题的当前 A21 UI 回显；
- 非队长提交 `0x000C` 的客户端失败表现；
- 公开队伍目录的创建/更新/移除广播及跨频道隔离（另行规格处理）。
