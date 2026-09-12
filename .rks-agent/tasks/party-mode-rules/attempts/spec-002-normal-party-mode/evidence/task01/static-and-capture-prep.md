# TASK-01 Normal 静态证据与定向捕获准备

## 固定输入

| 输入 | SHA-256 |
|---|---|
| `DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe` | `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14` |
| `DNFClient-A21-protocol/dstr.dat` | `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10` |
| 两份当前客户端的 `DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` |
| `DNFClient-A21-protocol/Script.pvf` | `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A` |
| 两份当前客户端的 `ClientHack.ini` | `40B97549F44ABEAE5B960C29C148FA0FB342A3B3A5A9097847B12796F7145579` |

接受基线为 `d1351ce6047ea84db1c764efc3ad9466781f07af`。spec-001 固定探针重跑
`14/14 PASS`；本单元探针 `probe_normal_party.py` 的专项锚点为 `5/5 PASS`。

## 上游对照

固定使用可复现的历史完成提交
`211129e30fb03964659b100e1e57568c8638103b`（不是本地当前 checkout
`2db3a135ad2c51508426f3c1f5b2e030e0dce9d6`）：

- `PartyHandler.cs:540..633`：同一 `0x000C` 在无队时创建，在已有队时更新；上游按请求 raw 保存设置。
- `PartyManager.cs:312..378`：`0x000D` 共用 `LeaveLocked`；移除最后成员后 `party.IsEmpty`，删除队伍并标记
  `Disbanded=true`。
- 这些只证明上游兼容行为；它不能替代当前 A21 的 wire/default 或 UI 证据。尤其上游先创建再更新的
  非原子结构不是本规格要复制的实现。

## 当前 A21 Normal default

`VA 0x0240BFF2..0x0240C05A` 先判断是否存在可读队伍对象、类别是否位于 `[0,3)`；仅在合法已有对象上
保留对象 `+0x60` 的类别和 `+0x64` 的 target。其余路径在 `0x0240C043` 明确写类别 `2`，随后在
`0x0240C051` 明确写 `u16 0`。因此：

- `Normal=2`：Confirmed static；
- 无合法现有队伍时 `ModeTargetRaw=0`：Confirmed static；
- 服务端现有 `0xFFFF` 与当前客户端 fallback 不一致；是否还存在另一生命周期 sentinel，必须由无队
  创建实帧裁决，静态阶段不把两者宣称为等价。

## 最后一人“保留/移除”路径

同一 UI 类的 `VA 0x023F0270..0x023F0338` 先调用在队判定，再取当前成员数并与 `1` 比较。精确为一人时，
它检查两个已保存选择状态；只有第二个状态存在时，`0x023F02C2..0x023F02D2` 构造并发送空 body 的
`0x000D LEAVE_PARTY`。同类相邻函数 `0x023F0340..0x023F040E` 以剩余角色名格式化 dstr `1126`：
“队伍中只剩下玩家[%s]。要继续保留队伍吗？”。

这组共同定位证明：

- 单人确认框的一条选择不发离队帧，保留队伍；
- 另一条选择复用空 body `0x000D`，服务端应按最后成员离队删除队伍；
- 哪个可见按钮对应内部第一/第二状态，仍需 UI + 帧时间对齐后才标 `ClientVerified`。

专项探针固定了上述 prompt、count==1、`0x000D` 和 Normal fallback 字节锚点；旧机器文档中的离队地址
以原壳 RVA 表示，当前 b1 VA 比它高 `0x00400000`，本页统一使用 b1 VA。

## 定向捕获

`capture_normal_proxy.py` 复用已接受的 split/coalesced TCP 重组和敏感流量白名单，只新增：

- C2S/S2C `0x000D`；
- 继续保留 C2S/CMD `0x000C` 与 S2C `0x0099`、`0x000B`、`0x0009`。

自检结果：既有创建三阶段重组/顺序 `PASS`；新增空 body `0x000D` 在拆包、粘包和无关帧共存下
`PASS`。首次动态加载因模块未注册到 `sys.modules` 触发 `dataclass` 命名空间错误；插入标准模块注册后，
同一自检通过，未改变生产代码或捕获边界。

## 运行时待裁决

1. 无队客户端从原生 UI 创建 Normal，记录 C2S `0x000C` 的完整 12 字节并确认 target。
2. 在既有队编辑一次，确认服务端接受和 type-1 回显。
3. 使队伍剩最后一人，分别观察提示；选择“移除队伍”并确认 C2S 空 body `0x000D`、S2C type-3 clear、
   Host `disbanded=True` 与 UI 回到无队状态。

在上述当前 A21 样本出现前，`0`/`0xFFFF` 最终 verdict 和可见按钮映射保持未决。
