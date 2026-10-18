# A21 设置 blob 与键位文本静态复核

日期：2026-10-13；服务端基线：314bd463。证据为 ClientStatic，不等于 ClientVerified。
只读反汇编文件：`E:/Project/DNF/DNFClient-A21-protocol2/DNF.unpacked-b1-debug.exe`，SHA-256：
`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
以下均为 VA（image base 00400000）；与机器区使用的 RVA 不混用。

## 外壳（Confirmed / ClientStatic）

| CMD | body | 调用点 |
|---|---|---|
| C5 | u32(150) +150B | 0137EF61 push150，0137EF74 call0274D3A0 |
| C6 | u32(byteLength) +原始多字节文本，无尾NUL | 013851F9 Begin(C6)，01385216 call0274D4A0 |
| 170 | u8 bank +u32(300) +300B，bank0／1 | 0137F3BA／0137F3F8写bank，0137F3D2／0137F410写blob |
| 1C0 | u32(512) +512B | 0137F36D push512，0137F380 call0274D3A0 |

0274D3A0先写四字节长度再写bytes。C6的0274D4A0→027BADD0→WideCharToMultiByte，
0274D410使用strlen，0274D439计入length+4。发送主体遍历106个热键构造KEY/VALUE文本。
不能套旧198B／99个u16，不能截前2B。文本内容本期保持不透明。

## 1C7文本回放（Confirmed / ClientStatic）

01160D70入口→01389E50：01389E5D读取u8类型，01389E66写this+250，01389E71加载默认。
01160D83→01389E80→01389680读取覆盖文本；013896E8→0274FCB0→0274FC10执行ReadU32+ReadBytes，
经MultiByteToWideChar后01389C56 convertDBKeyToEnum、01389C73 _wtoi、01389C83 applyHotkey。

013885D0检查keyType==1，选择`ClientOnly/HotKeySystemForCreator_ver2.co`，其余选择
`ClientOnly/HotKeySystem_ver2.co`。01388648检查资源；成功后013886A1..C5重建106项映射，
013886CF→0164B580将106个u16填0xFFFF，再逐条应用默认。

长度0经0274FC40→0274FC6E返回0；0274FCB0生成fallback `recvHotkeyOption2 - Fail`，
它只有一个token，不进入01389B61的双token覆盖、不走NONE重上传；最后01389B22→0164BCC0应用默认。
所以无存档可发送 `u8 profile + u32(0)`；依赖默认.co资源加载成功，缺资源不保证复位。

客户端这里只使用下发类型，不读取职业。职业10→profile1属于70／上游参考下的服务端策略，
账号＋profile为产品持久化策略，均不冒充当前A21作用域实测。C6上行没有profile，
连接上下文只能防已知切换和排队串写，无法识别无关联字段的迟到旧profile上传；实机切换仍待验收。

## AD与187

AD的旧“body被丢弃”静态否证已被[A12读取跟踪](../../../game/server-to-client/NOTI-0x00AD.md)推翻：
实际读取主blob及两个bank长度。当前A21入口01160D20调用01389310与0137EDE0；共用0274FBE0仍在VMP，
本次未以纯静态封闭该helper。150B主设置有历史A21 WireCaptured；完整恢复仍待当前实机。

187：011478F7→0137FB40；0137FB52 ReadU32、0137FB5A检查512、0137FB77读取512B至this+268。
当前数据库保留含长度的516B，直接回放，不再加前缀。

C5／C6／170／1C0没有同type CMD响应handler。1D9有注册响应回调，未纳入本期。
