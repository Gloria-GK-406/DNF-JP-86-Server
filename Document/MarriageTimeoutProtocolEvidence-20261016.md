# A21 婚礼自动超时与普通拒绝协议补证

日期：2026-10-16。证据范围：ClientStatic / Confirmed与ObservedMetadata / Confirmed；不代替当前补丁的双端文本实机验收。

## 绑定与复核入口

- 客户端EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 只读内存镜像SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；image base `00400000`；指令行首与正文定位地址为RVA，VA=RVA+image base。反汇编中的相对分支按RVA显示，内存操作数中的绝对地址保留原始VA。
- 镜像：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`。
- 服务端源码基准：`c9acc87326448c900020f15f8a7d2c3b9e497a9e`。
- 客户端补丁基准：相邻DNF-JP-86-ClientHack仓 `f717197eaadc20e5179b351b2b80f31f76fd4c08`，模块 `ClientHack/plugins/0200_gameplay_compatibility/wedding_timeout_reply_fix.c`。
- 只读复核：同仓 `python Document/outputs/wedding-timeout-20261016/probe_timeout.py --simulate-fix`；从生产数组读替换值并在内存副本核对，不修改镜像。

## 结论与修改边界

原生dispatcher是DEC/SUB3/SUB3，匹配1/4/7，action2未进入婚礼超时sender。补丁将RVA0088A369的03改01、RVA0088A36E的03改05，恢复1/2/7；将RVA0088A3B2的02改01，使婚礼自动超时的构包前缀与手动拒绝21B逐字节一致。正文仍22B，业务action2/outcome1位于正文18/21，target0走当前邀请上下文。原生后续构包、Flush与本地超时关窗保留，action1/7的超时sender未改。

NOTI03CE在status判断之前关闭等待窗；成功status0且action1/2时，outcome1选择dstr0x11C8A，outcome2选择0x11C37。字符串内容另见[原始本地化补证](outputs/marriage-proposal-20261016/client-evidence.txt)。因此邀请方普通拒绝须发送outcome1，不能只依赖outcome2来关闭窗口。

原始sender21B及dispatcher23B均做完整验签；只接受原始与正确替换两种状态。该取证不推断UI倒计时的规范秒数，也不代表patch后文案已经实机通过。

## 定向反汇编

### 原生手动拒绝构包前缀

```text
RVA 0088A2E1  53                               push ebx
RVA 0088A2E2  68 F1 03 00 00                   push 0x3f1
RVA 0088A2E7  66 89 5D F7                      mov word ptr [ebp - 9], bx
RVA 0088A2EB  C6 45 F6 02                      mov byte ptr [ebp - 0xa], 2
RVA 0088A2EF  89 5D F2                         mov dword ptr [ebp - 0xe], ebx
RVA 0088A2F2  C6 45 F9 01                      mov byte ptr [ebp - 7], 1
```

### 原生超时dispatcher

```text
RVA 0088A35E  8B 86 AC 01 00 00                mov eax, dword ptr [esi + 0x1ac]
RVA 0088A364  48                               dec eax
RVA 0088A365  74 60                            je 0x88a3c7
RVA 0088A367  83 E8 03                         sub eax, 3
RVA 0088A36A  74 32                            je 0x88a39e
RVA 0088A36C  83 E8 03                         sub eax, 3
RVA 0088A36F  0F 85 8A 00 00 00                jne 0x88a3ff
```

### 原生婚礼超时构包前缀

```text
RVA 0088A39E  53                               push ebx
RVA 0088A39F  68 F1 03 00 00                   push 0x3f1
RVA 0088A3A4  66 89 5D F7                      mov word ptr [ebp - 9], bx
RVA 0088A3A8  C6 45 F6 02                      mov byte ptr [ebp - 0xa], 2
RVA 0088A3AC  89 5D F2                         mov dword ptr [ebp - 0xe], ebx
RVA 0088A3AF  C6 45 F9 02                      mov byte ptr [ebp - 7], 2
```

### NOTI03CE关闭等待窗（先于status判断）

```text
RVA 00891978  E8 03 3A B3 01                   call 0x23c5380
RVA 0089197D  8B 0D B8 C9 A5 03                mov ecx, dword ptr [0x3a5c9b8]
RVA 00891983  83 C4 08                         add esp, 8
RVA 00891986  53                               push ebx
RVA 00891987  6A FF                            push -1
RVA 00891989  68 BF 01 00 00                   push 0x1bf
RVA 0089198E  E8 FD 2D 67 01                   call 0x1f04790
RVA 00891993  8B 0D B8 C9 A5 03                mov ecx, dword ptr [0x3a5c9b8]
RVA 00891999  53                               push ebx
RVA 0089199A  6A FF                            push -1
RVA 0089199C  68 C2 01 00 00                   push 0x1c2
RVA 008919A1  E8 EA 2D 67 01                   call 0x1f04790
RVA 008919A6  8B 45 F4                         mov eax, dword ptr [ebp - 0xc]
RVA 008919A9  3B C3                            cmp eax, ebx
RVA 008919AB  0F 85 F8 00 00 00                jne 0x891aa9
```

### NOTI03CE action1/2 普通拒绝与超时提示

```text
RVA 00891A16  3A C3                            cmp al, bl
RVA 00891A18  0F 8E 5D 02 00 00                jle 0x891c7b
RVA 00891A1E  3C 02                            cmp al, 2
RVA 00891A20  7E 31                            jle 0x891a53
RVA 00891A22  3C 07                            cmp al, 7
RVA 00891A24  0F 85 51 02 00 00                jne 0x891c7b
RVA 00891A2A  53                               push ebx
RVA 00891A2B  80 F9 01                         cmp cl, 1
RVA 00891A2E  75 56                            jne 0x891a86
RVA 00891A30  68 DE 1C 01 00                   push 0x11cde
RVA 00891A35  E8 86 FA C2 01                   call 0x24c14c0
RVA 00891A3A  8B 0D B8 C9 A5 03                mov ecx, dword ptr [0x3a5c9b8]
RVA 00891A40  83 C4 04                         add esp, 4
RVA 00891A43  50                               push eax
RVA 00891A44  68 09 02 00 00                   push 0x209
RVA 00891A49  E8 22 32 67 01                   call 0x1f04c70
RVA 00891A4E  5B                               pop ebx
RVA 00891A4F  8B E5                            mov esp, ebp
RVA 00891A51  5D                               pop ebp
RVA 00891A52  C3                               ret
RVA 00891A53  80 F9 01                         cmp cl, 1
RVA 00891A56  75 24                            jne 0x891a7c
RVA 00891A58  53                               push ebx
RVA 00891A59  68 8A 1C 01 00                   push 0x11c8a
RVA 00891A5E  E8 5D FA C2 01                   call 0x24c14c0
RVA 00891A63  8B 0D B8 C9 A5 03                mov ecx, dword ptr [0x3a5c9b8]
RVA 00891A69  83 C4 04                         add esp, 4
RVA 00891A6C  50                               push eax
RVA 00891A6D  68 09 02 00 00                   push 0x209
RVA 00891A72  E8 F9 31 67 01                   call 0x1f04c70
RVA 00891A77  5B                               pop ebx
RVA 00891A78  8B E5                            mov esp, ebp
RVA 00891A7A  5D                               pop ebp
RVA 00891A7B  C3                               ret
RVA 00891A7C  80 F9 02                         cmp cl, 2
RVA 00891A7F  0F 85 F6 01 00 00                jne 0x891c7b
RVA 00891A85  53                               push ebx
RVA 00891A86  68 37 1C 01 00                   push 0x11c37
```

## 已观察的服务端metadata（不是完整实帧正文）

源日志：`artifacts/marriage-timeout-retest-20261016-153732/runtime.stdout.log`。读取时快照字节数 `1560451`，SHA256 `EDD7A2F9102A4249D1210AF6FB85637E27043072CD1D2480D09B24F8F6D894C0`；源日志仍会追加，下列摘录独立保留。角色/目标/预约标识以占位符脱敏，target0协议值保留。自动outcome2来自上一版仅修分支的补丁，手动拒绝为outcome1；不是当前自动普通拒绝补丁的成功证明。

```text
2026-10-16 16:16:58.832 +08:00 info: Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalHandler[0]
      Marriage ceremony request actor=<role> target=<peer> tier=0 waitIndex=0 result=None id=<auto-invitation>
2026-10-16 16:16:58.832 +08:00 dbug: Dfo.Server.Networking.Runtime.TouchSocketNetworkMessageSender[0]
      S->C Game:03-00-00-00 selector=0x00 type=0x03CD frame=26 WeddingProposalNotification
2026-10-16 16:17:04.833 +08:00 info: Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler[0]
      Marriage ceremony response actor=<role> target=0 outcome=2 result=None id=<auto-invitation>
2026-10-16 16:17:04.834 +08:00 dbug: Dfo.Server.Networking.Runtime.TouchSocketNetworkMessageSender[0]
      S->C Game:04-00-00-00 selector=0x00 type=0x03CE frame=27 WeddingResponseNotification
2026-10-16 16:17:16.188 +08:00 info: Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalHandler[0]
      Marriage ceremony request actor=<role> target=<peer> tier=0 waitIndex=0 result=None id=<manual-invitation>
2026-10-16 16:17:16.188 +08:00 dbug: Dfo.Server.Networking.Runtime.TouchSocketNetworkMessageSender[0]
      S->C Game:03-00-00-00 selector=0x00 type=0x03CD frame=26 WeddingProposalNotification
2026-10-16 16:17:18.302 +08:00 info: Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler[0]
      Marriage ceremony response actor=<role> target=0 outcome=1 result=None id=<manual-invitation>
2026-10-16 16:17:18.302 +08:00 dbug: Dfo.Server.Networking.Runtime.TouchSocketNetworkMessageSender[0]
      S->C Game:04-00-00-00 selector=0x00 type=0x03CE frame=27 WeddingResponseNotification
```

该次申请16:16:58.832、自动答复16:17:04.833相距约6秒；仅为观察，不定义规范超时时间。2026-10-16用户明确反馈自动超时后邀请方正常显示被拒绝、等待窗口关闭，该路径记有限范围ClientVerified；仍未归档完整新实帧。协议编排及验证矩阵见[流程](../protocol/patterns/flow-marriage-proposal.md)。
