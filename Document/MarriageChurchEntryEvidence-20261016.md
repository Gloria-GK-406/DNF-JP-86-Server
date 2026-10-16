# A21 普通婚礼进入教堂的定向补证

日期2026-10-16，服务端修改前基准f8d519a1。ClientStatic / Confirmed按具体指令结论标注；服务器对未知字段的0值策略不升级为原生业务定义。

客户端EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；内存镜像SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`；image base00400000。下列行首为RVA，相对分支按RVA解码，内存中的绝对地址仍为VA。镜像为相邻DNFClient-A21-protocol的 `dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`。

## 身份与原生分支

1. `RVA01267CC7`的CALL目标为`RVA00898740`；该构造器在`00898782`将this写入绝对地址039ADF68，`008988D4`返回this；调用者在`01267CD4`把返回值写入039ADF6C。因此两处全局指针指向同一个婚姻manager，不能把后者当成当前玩家对象。
2. NOTI03D0的第一个u32被写入manager+5C；NOTI03D4首先将正文offset0与这个值比较，匹配才初始化当前婚礼。现有03D0第一个字段是绑定对象CharacterId，因此为每位接收者编码03D4时，offset0必须是其对象持久角色编号，不能两端固定成同一个申请者编号。
3. **已纠正：**03D4 offset4写manager+0C，复制到当前记录offset8。RVA00F9CFC3/00F9D045将其直接与角色area getter返回值比较，因此这是教堂area，而非固定0的普通模式。先前只观察列表构包分支而把它写0，漏看了新人通知入口，结论被推翻。新人通知回调RVA00F92C80调用构包器`(1,0,0)`，第二参数0只控制选择当前婚礼；真正写入03F7 offset18的是manager+0C的area。数据库room token仍不进入正文。
4. 03D4固定79B中，offset17/47各是30B字符串；offset77还有单独u16，写manager+74。原先将第二段笼统记作32B会掩盖这条边界。RVA008918B0读取此值，RVA00885E7E..00885E8C将它送入dstr11CFA的额度显示。其为请柬剩余额度，字段语义已闭合；当前请柬派送未实现，仍写0，不代表普通婚礼的官方额度。
5. offset8写manager+54；`RVA00893B81..00893B8B`计算其减当前秒时钟的差。当前秒时钟`RVA0124F710`是初始秒值加GetTickCount差乘0.001；现有InitialConnection编码发送Unix秒时间。因此当前准备截止时间按UTC Unix秒编码。offset12仍作为未闭合字段写0，不冒称仪式开始时间。
6. 新人进入教堂的03F7正文为23B，前14B opaque保留；14/18/22为u32对象编号、u32教堂area、u8 flag。普通新人按当前上下文发对象编号/当前area/0。构包器的第一参数0用于离开婚礼/回到大司祭附近，实际发0024；不能因它位于同一个函数就把这条支路也当03F7。
7. `RVA0089460A..0089462A`只认可town2/area12..62。当前PVF的town2为Underfoot，具有这些Church/Church.map槽位；63虽也引用教堂图，却不在该原生门范围，本次不分配且禁止直接进入。

当前PVF SHA256 `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`。`etc/popupmenu.etc`现有条目：1014“举办婚礼”、1017“参加婚礼”、1018“离开婚礼”、1019“开始婚礼”；本次只接普通新人入场与既有离场换图，不开放开始仪式。

## 错误码的独立依据

跳表位于RVA00892568，按status+10索引：

```text
-10: RVA00892546 push 0; push 0x8d45
-9: RVA0089253D push 0; push 0x11cd5
-8: RVA00892566 pop ebp; ret ; inc esi
-7: RVA00892534 push 0; push 0x11cd6
-6: RVA0089252B push 0; push 0x11cd4
```

对应dstr：8D45“当前无法移动”、11CD5“请确保消耗品栏空出两格以后参加婚礼”、11CD6“已超过参加婚礼时间，您被限制进入教堂”、11CD4“宾客参加婚礼必须要赠送礼金”。当前普通新人无有效预约/不匹配/不支持模式用-10，准备窗口到期用-7。不会把-6宾客礼金错误当成通用入场失败。

## 定向指令

### 03D0对象编号写入manager+5C

```text
RVA008952CB mov eax, dword ptr [esi]
RVA008952CD mov dword ptr [edi + 0x5c], eax
```

### 03D4对象筛选与剩余秒门

```text
RVA00895C36 mov ecx, dword ptr [0x39adf6c]
RVA00895C3C mov eax, dword ptr [ecx + 0x5c]
RVA00895C3F cmp dword ptr [ebp - 0x180], eax
RVA00895C45 jne 0x895dca
RVA00895C4B call 0x124f710
RVA00895C50 mov edx, dword ptr [ebp - 0x178]
RVA00895C56 add edx, 0x3c
RVA00895C59 cmp edx, eax
RVA00895C5B jl 0x895dca
```

### 03D4上下文回填

```text
RVA008954CA test edi, edi
RVA008954CC je 0x895556
RVA008954D2 mov eax, dword ptr [edi + 4]
RVA008954D5 mov dword ptr [esi + 0xc], eax
RVA008954D8 mov ecx, dword ptr [edi + 8]
RVA008954DB lea edx, [edi + 0x2f]
RVA008954DE push ebx
RVA008954DF push edx
RVA008954E0 mov dword ptr [esi + 0x54], ecx
```

### 当前婚礼数据复制

```text
RVA00895584 push edi
RVA00895585 mov edi, dword ptr [ebp + 8]
RVA00895588 mov dword ptr [edi], eax
RVA0089558A mov eax, dword ptr [esi + 8]
RVA0089558D mov dword ptr [edi + 4], eax
RVA00895590 mov ecx, dword ptr [esi + 0xc]
RVA00895593 push -1
RVA00895595 mov dword ptr [edi + 8], ecx
RVA00895598 mov edx, dword ptr [esi + 0x10]
RVA0089559B push 0
RVA0089559D lea eax, [esi + 0x14]
RVA008955A0 push eax
RVA008955A1 lea ecx, [edi + 0x10]
RVA008955A4 mov dword ptr [edi + 0xc], edx
```

### 参加婚礼列表按钮调用

```text
RVA00887978 lea ecx, [ebp - 0xc0]
RVA0088797E call 0x886230
RVA00887983 mov ecx, dword ptr [0x39adf6c]
RVA00887989 lea eax, [ebp - 0xc0]
RVA0088798F push eax
RVA00887990 push esi
RVA00887991 mov byte ptr [ebp - 4], 8
RVA00887995 call 0x894510
RVA0088799A test al, al
RVA0088799C je 0x8879b3
RVA0088799E mov ecx, dword ptr [ebp - 0xb8]
RVA008879A4 push esi
RVA008879A5 push ecx
RVA008879A6 mov ecx, dword ptr [0x39adf6c]
RVA008879AC push 1
RVA008879AE call 0x8955d0
```

### 03F7新人和宾客分支

```text
RVA0089565C cmp byte ptr [ebp + 8], 0
RVA00895660 je 0x89570b
RVA00895666 mov bl, byte ptr [ebp + 0xc]
RVA00895669 test bl, bl
RVA0089566B jne 0x89567e
RVA0089566D mov eax, dword ptr [esi + 4]
RVA00895670 mov ecx, dword ptr [esi + 0xc]
RVA00895673 mov dword ptr [ebp - 0xe], eax
RVA00895676 mov dword ptr [ebp - 0xa], ecx
RVA00895679 mov byte ptr [ebp - 6], bl
RVA0089567C jmp 0x8956cd
RVA0089567E mov ecx, esi
RVA00895680 call 0x8945f0
RVA00895685 cmp al, 1
RVA00895687 jne 0x8956bd
RVA00895689 push 0
RVA0089568B push 0x11c5a
RVA00895690 call 0x24c14c0
RVA00895695 mov ecx, dword ptr [0x3a5c9b8]
RVA0089569B add esp, 4
RVA0089569E push eax
RVA0089569F push 0x209
RVA008956A4 call 0x1f04c70
RVA008956A9 pop esi
RVA008956AA xor al, al
RVA008956AC pop ebx
RVA008956AD mov ecx, dword ptr [ebp - 4]
RVA008956B0 xor ecx, ebp
RVA008956B2 call 0x28627f3
RVA008956B7 mov esp, ebp
RVA008956B9 pop ebp
RVA008956BA ret 0xc
RVA008956BD mov edx, dword ptr [ebp + 0x10]
RVA008956C0 movzx eax, bl
RVA008956C3 mov dword ptr [ebp - 0xe], edx
RVA008956C6 mov dword ptr [ebp - 0xa], eax
RVA008956C9 mov byte ptr [ebp - 6], 1
```

### 离开婚礼改发0024

```text
RVA0089570B push 0x4e2d
RVA00895710 call 0x1219fd0
RVA00895715 mov ecx, eax
RVA00895717 call 0x1216e50
RVA0089571C mov esi, eax
RVA0089571E call 0x122a1f0
RVA00895723 test esi, esi
RVA00895725 je 0x8956a9
RVA00895727 push edi
RVA00895728 mov ecx, esi
RVA0089572A call 0x10317c0
RVA0089572F mov dx, word ptr [esi + 0x1c]
RVA00895733 movzx edi, word ptr [esi + 0x18]
RVA00895737 add dx, 0x1e
RVA0089573B push 0
RVA0089573D push 0x24
RVA0089573F mov byte ptr [ebp - 0x20], al
RVA00895742 movzx esi, dx
RVA00895745 call 0x234ca50
RVA0089574A mov ecx, eax
RVA0089574C call 0x234d1e0
RVA00895751 mov eax, dword ptr [ebp - 0x20]
RVA00895754 push eax
RVA00895755 call 0x234ca50
RVA0089575A mov ecx, eax
RVA0089575C call 0x234d2e0
```

### 本人的剩余秒数

```text
RVA00893B70 push ebp
RVA00893B71 mov ebp, esp
RVA00893B73 mov eax, dword ptr [ebp + 8]
RVA00893B76 sub esp, 0x18
RVA00893B79 push esi
RVA00893B7A mov esi, ecx
RVA00893B7C cmp eax, dword ptr [esi + 0x5c]
RVA00893B7F jne 0x893b94
RVA00893B81 call 0x124f710
RVA00893B86 mov ecx, dword ptr [esi + 0x54]
RVA00893B89 sub ecx, eax
RVA00893B8B mov eax, ecx
RVA00893B8D pop esi
RVA00893B8E mov esp, ebp
RVA00893B90 pop ebp
RVA00893B91 ret 4
```

### 教堂地点门

```text
RVA00894608 mov ecx, esi
RVA0089460A call 0x2019650
RVA0089460F cmp eax, 2
RVA00894612 jne 0x894652
RVA00894614 mov ecx, esi
RVA00894616 call 0x2306870
RVA0089461B cmp eax, 0xc
RVA0089461E jl 0x894652
RVA00894620 mov ecx, esi
RVA00894622 call 0x2306870
RVA00894627 cmp eax, 0x3e
RVA0089462A jg 0x894652
```

### 03F7错误分派

```text
RVA0089251C add eax, 0xa
RVA0089251F cmp eax, 4
RVA00892522 ja 0x892566
RVA00892524 jmp dword ptr [eax*4 + 0xc92568]
RVA0089252B push 0
RVA0089252D push 0x11cd4
RVA00892532 jmp 0x89254d
RVA00892534 push 0
RVA00892536 push 0x11cd6
RVA0089253B jmp 0x89254d
RVA0089253D push 0
RVA0089253F push 0x11cd5
RVA00892544 jmp 0x89254d
RVA00892546 push 0
RVA00892548 push 0x8d45
RVA0089254D call 0x24c14c0
RVA00892552 mov ecx, dword ptr [0x3a5c9b8]
RVA00892558 add esp, 4
RVA0089255B push eax
RVA0089255C push 0x209
RVA00892561 call 0x1f04c70
```

## 验证边界

字段边界和分支来自上述镜像；持久房间、教堂area投影、未闭合字段置0及准备窗口续开是CurrentImplementation。原生两端实际按钮、换图、人物可见和离场仍须用户验收；没有新增WireCaptured。自动超时普通拒绝已由用户确认提示与关窗正常，属于前一阶段有限范围ClientVerified。

## 新人通知入口补证

ClientStatic / Confirmed：大司祭1014“举办婚礼”在RVA021AB558读取state，等于2即提示重复预约；此分支不是入场。RVA00F9B0BF..00F9B0CD注册通知0x41C到RVA00F92BD0；其创建dstr11CF1确认框，确认回调RVA00F92C80以`(1,0,0)`调用03F7构包器。RVA00F9CF40..00F9D0BE根据state2、有效当前婚礼上下文与角色是否在对应教堂显示/隐藏该通知。另一个0x41D通知仅随state2显示，不能把请柬派送窗口当作入场按钮。截图已确认派送窗口显示双方名字和额度0；未证明新人入场已成功。

同镜像RVA008918B0返回manager+74，RVA00885E7E读取该getter并格式化额度文案，闭合了03D4 offset77为请柬剩余额度的消费链。本阶段不接请柬发送、来宾入场或额度结算，0是功能占位，不能认定为普通婚礼应有数量。
