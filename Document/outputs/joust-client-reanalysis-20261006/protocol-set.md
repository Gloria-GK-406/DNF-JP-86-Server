# A21 马战协议集合与查询、投注、历史原生调用链

本报告只读复核原始 A21 内存镜像；服务端实现仅用于列差异。没有启动客户端、运行数据库、部署或修改产品代码。

## 身份、坐标、证据等级

- Dump：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`。
- 本次实算 SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。
- 调度给定 EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；本子任务未重新读取 EXE 验 hash。
- Image base `00400000`；全文机器地址为 **VA**，RVA = VA - `00400000`。例如状态 getter **VA `02846D50` / RVA `02446D50`**，实际字节 `8B 41 04 C3`（读 `this+4` 并返回）。
- Confirmed = 该 dump 中可机械核对的指令/读长/分支；High = 按访问模式推导的对象作用；Unknown = 未排除，不扩写成游戏规则。没有新增 WireCaptured 或 ClientVerified 结论。

## 集合结论

**马战局部初始化 VA `004D2870`（RVA `000D2870`）确实只有七条接收注册，但这不等于整个功能只依赖七条协议。** 通用 EventManager 的活动存在表、manager 建立/重置、UI 生命周期均在模块外。结果界面的外部入口显式查询活动 key `0x093D`，见下文。

| 类别 | Opcode | 客户端 writer / reader VA | 本地读写长度 | 可证明作用 |
|---|---:|---|---:|---|
| C2S CMD | 04E6 | writer `004D21A0` | 14B | 查询；首14B在本函数没有字段赋值 |
| S2C CMD ACK | 04E6 | reader `004D2030` | callback success 门后读 u32 | result=0确保UI 1FB对象存在；错误显示指定string |
| C2S CMD | 04E7 | writer `004F2B10` | 23B | 确认回调发送；14B未知前缀+knight u8+slot i32+amount i32 |
| S2C CMD ACK | 04E7 | reader `004D20B0` | callback success 门后读 u32 | result=0调用UI 1FB的 `004F4300` 更新函数 |
| C2S CMD | 04E8 | writer所在UI函数 `004F4B10`，构包 `004F4CD3` | 14B | UI初始化发历史查询；本地首14B未赋值 |
| S2C CMD ACK | 04E8 | reader `004D25B0` | success门后固定3504B | u32 result+500×7B；result=0清历史、装记录、整理并更新滚动控件 |
| S2C NOTI | 04B8 | reader `004D2690` | 3B | u16+状态i8；写manager+4并更新UI/公告 |
| S2C NOTI | 04B9 | reader `004D2330` | 90B | u16+8×11B；更新1FB及1FC中的选手投影 |
| S2C NOTI | 04BA | reader `004D23E0` | 46B | u16+i32+8×5B；更新1FB投注投影 |
| S2C NOTI | 04BB | reader `004D24A0` | 31B | 1FC结果场景投影 |

ACK矩阵中的 success 是 reader 第二参数 `[ebp+0xC]`；完整帧中该参数怎样从公共CMD字节产生，本子任务未单独重建dispatcher，不能用它推断任意失败帧长度。当前服务端以1B success+4B result编码，与reader的两个门相容。

完整局部注册：CMD注册函数 `01637660`，NOTI注册函数 `01637720`；七项分别在 `004D28F2/290A/2922/293A/2952/296A/2982`。`004D21BE`只是 `004D21A0` 内部的公共 BeginCMD 调用指令，**不是第二个query writer**。

全dump抽取了501个符合固定注册形状的记录（兼容opcode的 `68 imm32` 与 `6A imm8`），保存 `protocol-set-registers-all.tsv`；扫描不是通用反汇编CFG，未命中的其他注册形状不能视作不存在。邻近opcode不构成马战归属证据：

| 类别 | 邻近opcode | reader VA | 结论 |
|---|---|---|---|
| CMD | 04EC/04ED/04EE/04EF/04F0 | 00444480/00444010/004446B0/00442820/00442380 | 同一另一模块004448xx注册，无马战manager直接RTTI引用，暂不纳入 |
| NOTI | 04BC | 004425E0 | 同上 |
| CMD | 04DF/04E0 | 00501560/00504DD0 | 另一模块，无当前马战依赖证据 |
| CMD | 04E9 | 02231720 | 02EEBCC3全局注册，无当前马战依赖证据 |
| NOTI | 04B0/04B1/04BF | 004FDA30/004CF8F0/0051DF80 | 附近模块或全局注册，未证明马战依赖 |
| NOTI | **006C** | **01419E30** | **已确认的通用活动完整列表入口**，短push `6A6C` 注册于0141A0CD；reader→014196B0字段解析→01417650尾表→01416BD0活动同步→0141A1E0类型派发，包含093D入口存在表依赖 |
| NOTI | 02B0 | 01415320 | EventManager本体注册；读u8 count，随后count×8B写global+20；不是已证明的活动存在map初始化 |
| CMD | 044A | 014151B0 | EventManager本体注册；读u32并处理0x12/0xCC错误提示，未证明马战开关作用 |
| NOTI / CMD | 0197 / 01D5 | 0141C2D0 / 0141D6A0 | 全局活动邻接候选；初始化代理另追，不能与马战七条混称完整集合 |

## ACK的实际用途、查询与真正打开UI

### 通用UI方法必须区分

`023060F0`（RVA `01F060F0`）是 **get-or-create/ensure对象**：按ID查已存在列表，次查cache，没有则调用factory `023BD910`并缓存；整函数没有显示窗口调用。`02304C70`是另一个 UI 打开入口。因此 `004D2030` 的result=0仅调用 `023060F0(0x1FB)`后弃返回值，不能把它称作“成功ACK打开投注窗”。04B9、04BA读者同样调用get-or-create，因而当前服务端遗漏04E6成功ACK虽有原生流程差异，**单凭这点不能归因UI打不开**。

factory的跳表 `023CA480[0x1FB]` → `023CA26C`，分配0x244B并调用投注UI constructor `004F4930`；`[0x1FC]` → `023CA2A6`，分配0x264B并调用结果UI constructor `004F9D40`。投注UI虚表 `0306B94C+0x18` = `004F4B10`。另一个打开入口的保留主体在 `02304F9F..02304FAE` 调UI虚表+0x18，随后判断返回bool；**ensure路径没有这项调用**。`02304C70`本身入口及部分分支受保护跳转，不能由保留主体证明所有实时执行路径，但两种调用的静态能力差别可直接核对。

查询原生链：

1. `004F6050` UI初始化读取网络client状态（`0163D150`）；允许时取得EventManager中ID `0x45` 的马战manager，调用query writer `004D21A0`。
2. query发送后立即调用状态getter `02846D50`。值0时弹string `0x12BC0`并走返回false；非0继续UI初始化 `004F5CC0`。这里等待ACK之前已有本地manager状态门，因此只有B9/BA数据、没有本地state，不能代替这道门。
3. `004F5CC0` 后段 `004F5DE3` 调 `02304C70(0x1FB,0,0)` 打开投注UI；另有 `004F6310` 控件事件 `(kind=0xD)` 可打开该窗，ID `0x1F40` 分支会再次发04E6。
4. 04E6 success result0仅ensure 1FB；04B9/BA填投影；原始server回包的严格相对顺序无法由这些客户端函数证明，仍须实帧。
5. 结果UI的场景处理 `004FA518` 从参数值派发；其中 `004FA528` 分支只要结果UI1FC已显示且manager存在，会再发04E6；不是独立新opcode。

### 通用活动存在门

外部UI入口 **VA `025ABDC9` / RVA `021ABDC9`** 保留参数 `0x093D`，获取global EventManager `0141A170`，再调用 `014172D0(0x093D)`。`014172D0` 从EventManager `this+0x18` 的map执行find并返回find!=end。true时确认UI1FC未显示再 `02304C70(0x1FC,0,0)`；false显示string `0x12BC0`。

这证明原生结果入口依赖通用活动存在数据；也不证明任意93D配置等同完整启用。**原生入口为NOTI006C**，其reader先解析临时活动列表、读尾表、再同步活动对象（01416BD0）；`0141A1E0` 的直接调用点是 `01416E46` 和 `0141B256`。类型对象是否初始化state与重置详情由并行初始化调查继续定位。任务中的 `0190_JoustStateSync` 是补丁插件编号，不能当作原生NOTI0190。

006C本次重读字段与现有 `protocol/game/server-to-client/NOTI-0x006C.md` 人工A21段交叉一致：`u16 entryCount`；各项 `u16 eventId + u32 unknown + dstring startNotice + dstring endNotice + u8 hasDetail`；有detail时 `u8 flagA + u8 flagB + dstring title + dstring shortName + dstring thirdText + u32 startUnix + u32 endUnix + dstring linkKey + dstring description + u8 enabled`。含detail的entry固定部分46B加七个字符串字节，不含detail固定部分15B加两字符串字节；dstring前缀u32字节长，读取后转宽字符串的实现为0274FCB0，本子任务不对补丁后编码做新结论。

尾表01417650查现有manager map中是否含event9，若有先读 `u8 n + n×u16`，随后均读 `u8 extraCount + extraCount×(u16 eventId + 12×u32)`。已重读完整tail `01417650..01418130`，其中没有直接093D/ID45/马战RTTI处理分支，故不能把尾表任意u32命名为马战状态。006C reader还刷新calendar UI和其他系统，不是已证明的纯静默快照；event9存在时的尾段时序与通用重置/关闭必须整体核查。

## 投注依赖与请求形状

投注UI依赖至少三类原生状态：

- manager ID45及其state `this+4`；`004F3D2A` 调getter，state!=1将enable flag置0，并调用UI `this+208` 与 `this+21C` 的虚表+0xC方法禁用。**这不是该函数立即退出**。
- 04B9填入的8条记录/选中index `UI+224`；`004F5130` 从 `UI+1B8` 容器找所选record，再读record+1作为byte并用于详情UI，字段含义由场景调查负责，不能仅凭记录位置命名。
- manager活动配置对象（`01A182F0`），可投注材料列表数量 `028D71E0`、按序item ID `028D71B0`、数值上限 `028D6EF0`，以及本地库存索引 `016EF580/016EE6C0`。`004F4B10`初始化这些材料投影，随后 `004F4300` 更新输入及材料状态，然后发送04E8。

点击投注控件路径在 `004F40xx` 构造带虚表 `0306B73C` 的确认callback：amount→callback+24、slot→+28、knight index→+2C；用string `0x12BC1` 打开确认UI `0x263`。callback `004F2B10` 仅当传入结果参数==0且amount!=0才组04E7。payload拷贝起点 `[ebp-0x1C]`，+14=u8 knight、+15=i32 slot、+19=i32 amount。这是本地序列化语义，server的BusinessSlot映射/库存扣除仍属CurrentImplementation。

04E7 success ACK调用 `004F4300`，该函数重新查看材料库存、可投注数量/输入控件及manager配置；不会写manager state，也不会装载04B9/BA所有数据。成功后的库存通知/04BA/04B9先后是否必需，从静态dump只能证明观察依赖，无法证明server顺序。

## 历史不是只改普通data，但查询依然可只读服务端

`004D25B0` 在callback success非0时固定读取3504B，result0才投影UI1FB：调用 `004F42F0`清 `UI+230` 容器；循环最多500条，每条7B，首u16为0即终止；非0记录调用 `004F4D20`装载；最后 `004F4D40`整理容器并 `004F1A00`设置滚动范围/位置。这不等于打开history窗口的动作，更不意味着服务器查询需要副作用。ACK无payload-error显示分支，result非0或success0直接返回。

记录形状在本reader只机械证明 `u16 + u8 + f32(raw4B)`；胜者/赔率命名应结合UI后续访问和资料，不能仅靠server DTO。

## 错误映射

仅列客户端string ID，未从PVF解出文字，不能把ID反向猜成服务端业务理由。

| ACK | Result | 客户端动作 |
|---|---|---|
| 04E6 | 0 | ensure UI1FB，无show/state setter |
| 04E6 | 1/3/4 | string12BC0，经UI209显示 |
| 04E6 | 2/5及范围外 | 静默返回 |
| 04E6 | 6 | string12ACA，经UI209显示 |
| 04E7 | 0 | ensure UI1FB后调用004F4300 |
| 04E7 | 4 | string12BC0 |
| 04E7 | 5及范围外 | 静默返回 |
| 04E7 | 6 | string12BC2 |
| 04E7 | 7 | string12BC4 |
| 04E7 | 8 | string12ACA |
| 04E8 | 非0或success0 | 无历史投影，也无错误弹窗 |

## 14B未知前缀的证据边界

三条C2S request的14B公共前段在各自局部writer中未见赋值；query/history整段14B局部栈地址传给 `0274CED0`，bet仅在后9B赋值。公共BeginCMD `0274D1E0` **另行**构造14B frame-header并调用低层写入，因此不能把这14B未知前缀直接认作同一frame-header。

`0274CED0`累加payload长度，再把指针/长度交给dump内 VA `05790FE9` 的转接函数。实际dump长度122085376B，此地址有可读取字节；其序言后进入保留flags/寄存器的混淆跳转（05790FFE跳E4830E6B），**目标在本镜像之外**，不能用线性后续杂乱反汇编当作有效写路径。本子任务未追尽该转接函数的真实写路径，故 **最终wire是否保留、改写、加密或填充首14B为Unknown**；不能称其随机栈垃圾、加密token或反作弊认证结构。只有本地writer的23/14B请求尺寸和后三字段可确认。

## 当前服务端差异与仍未排除项

读取 `JoustSlice.cs` 只是差异检查：active04E6仅发送04BA/04B9，没有CMD04E6 success；inactive统一result6；04E7所有失败统一result6。前者不匹配原生成功ACK调用路径，但两条NOTI也ensure同一对象，尚无足够证据认定它是主要故障原因；后者让所有失败都触发string12BC2而不是各种原生分支。

state高位0x80方案改变原生状态字节，既不是native silent-sync opcode，也不是原生能力发现。通用活动初始化、manager reset、入口gate、状态set必须作为整体追踪，不能仅把7条reader补齐就宣布协议集合完整。

仍未排除：其他形状的全局handler注册、通用活动数据分阶段创建/删除/重载、场景UI虚表回调顺序、首14B低层真实写路径、实际server ACK/NOTI顺序、实机UI/库存时序。

## 可复现证据

- `protocol-set-scan.py`：dump hash、定点反汇编、rel32及绝对xref、push常量扫描；xref周围输出可能起点不对齐，结论只使用从已确认函数起点/分支起点重新反汇编的片段。
- `protocol-set-initial.txt`：ACK、state reader和直接xref。
- `protocol-set-followup.txt`：局部七项注册、ensureUI、history投影、相邻值扫描。
- `protocol-set-manager-refs.txt`：manager RTTI全dump绝对xref、state getter精确字节、ACK成功更新。
- `protocol-set-writers.txt`、`protocol-set-bet-submit.txt`：请求writer、UI展开、确认callback、enable gate。
- `protocol-set-generic-gate.txt`、`protocol-set-config-refs.txt`：generic93D存在门及通用EventManager相关方法。
- `protocol-set-registers.py`、`protocol-set-registers.txt`、`protocol-set-registers-all.tsv`：全dump严格形状注册集合及外围候选。
- `protocol-set-send-internals.txt`：公共frame header独立构造及payload低层append界限。
- `protocol-set-event006c.txt`、`protocol-set-event006c-tail.txt`：独立重读006C完整活动列表reader和主表/尾表字段。
- `protocol-set-wrapper-tail-full.txt`：完整006C尾表，以及dump内混淆wrapper到镜像外目标的准确界限。
- `protocol-set-ui-create-show.txt`、`protocol-set-ui-factory-detail.txt`、`protocol-set-ui-bind.txt`：ensure/factory/create与调用UI虚表初始化的边界，UI1FB构造及控件绑定。
