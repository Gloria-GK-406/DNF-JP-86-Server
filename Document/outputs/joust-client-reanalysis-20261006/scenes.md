# A21 马战原生 state / scene / result stage 重新取证

日期：2026-10-06。范围：ClientStatic，只读分析原始 dump；不修改客户端、服务端、数据或部署。此前实现与文字释义均不作为证据。状态、动画、公告及B9/BB字段复核已完成；原生初始化另由并行研究负责。

## 工件和地址

- dump：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`
- 本次脚本验证 SHA-256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。
- image base：`0x00400000`；以下主地址均为 **RVA**；括号为对应 **VA**。反汇编指令的 operand 保留原始 VA。
- 复核脚本：`scenes-disassemble.py`；原始证据：`scenes-core-asm.txt`、`scenes-tick-asm.txt`、`scenes-fields-clock-asm.txt`、`scenes-animation-clock-asm.txt`。
- `Confirmed` 仅表示所列静态分支/字段存在；不等于 WireCaptured / ClientVerified。字符串 ID 对中文的映射未读取正文，仍为 Unknown。

## 04B8 消费和公告（Confirmed）

`D2690 (4D2690)` 读取 3B（u16 round + signed i8 rawState）。manager factory/type-cast 后，`D2700 (4D2700)` 调 `D2150 (4D2150)`，后者在 `D2158 (4D2158)` 保存 `[manager+4]=rawState`。随后 `D2710 (4D2710)` 经 `1F060F0 (23060F0)` 获取/按需创建窗口 `0x1FC`，非空则调用 `FCBD0 (4FCBD0)`。这里不能推成玩家已经打开页面；窗口对象存在与已显示检查是两回事。

`FCBD0` 总会先保存 `[UI+21C]=round`；按 rawState 作无符号范围 0..5 跳转（表 RVA `FCC7C` / VA `4FCC7C`）：

| rawState | scene 消费 | manager 保存 | 04B8 直接公告 ID |
|---|---|---|---|
| 0 | `FCBF4→FA1B0(0)` | 0 | 无 |
| 1 | `FCC00→FA1B0(1)` | 1 | `12BCB` |
| 2 | `FCC0C→FA1B0(2)` | 2 | `12BCC` |
| 3 | `FCC75` 返回，不改变 scene | 3 | `12BBD` |
| 4 | `FCC18→FA1B0(13)` | 4 | `12BBE` |
| 5 | counter `[UI+220]` 为0/1/2时分别转5/8/11，再写1/2/0 | 5 | 无 |
| 范围外 | 不改变 scene | 原始 signed 值 | 无 |

公告从 `D272D (4D272D)` 再读 **rawState**，减1，unsigned 比较4。表 `D2850 (4D2850)`：1..4分别到 `D2742/49/50/57`，5到结束 `D2834`。未检查旧 state/round，不去重。

- 窗口已显示谓词：`1F01820 (2301820)`，调用点 `D2767 (4D2767)`。
- 已显示：跳过浮动提示，仍走 `D2818→D282F→1662DF0 (1A62DF0)` 聊天公告。
- 未显示且 mode getter `123E1E0 (163E1E0)` 返回1：构造5000ms浮动提示，再聊天公告。未显示且非mode1：仅聊天公告。
- 因而“UI未打开就不会公告”错误；只要 get-or-create 返回对象，聊天链仍执行。
- 原生 rawState 高位没有 silent 标志：signed读取后超范围，不作正常 scene 映射；不能把客户端插件的 high-bit mask 当原生协议。

## scene setter 与本地推进（Confirmed）

`FA1B0 (4FA1B0)` 在 `FA1ED (4FA1ED)` 无条件覆盖 `[UI+218]=scene`，未比较旧 scene。表 `FB7BC (4FB7BC)` 只覆盖0..12；scene13保存后走公共结束，不启动本表动画。

| scene | setter 入口 RVA (VA) | tick 行为 / 外部含义边界 |
|---|---|---|
| 0、1 | `FA528 (4FA528)` | 同入口；窗口已显示且manager有效时 `FA542→D21A0 (4D21A0)` 自动查询04E6；然后公共基础显示 |
| 2 | `FA547 (4FA547)` | 公共基础显示，绕过上述自动查询 |
| 3 | `FA8A2 (4FA8A2)` | 创建/启动动画；tick动画完成→4 |
| 4 | `FAA48 (4FAA48)` | 第一组轨迹动画；本地轨迹耗尽→5 |
| 5 | `FAA94 (4FAA94)` | 第一组结束显示；tick空分支，不自动进入6 |
| 6 | `FAB53 (4FAB53)` | 第二组前置动画；tick完成→7 |
| 7 | `FACC5 (4FACC5)` | 第二组轨迹动画；耗尽→8 |
| 8 | `FAE66 (4FAE66)` | 第二组结束显示；tick空分支，不自动进入9 |
| 9 | `FAF4C (4FAF4C)` | 第三组前置动画；tick完成→10 |
| 10 | `FB0ED (4FB0ED)` | 第三组轨迹动画；耗尽→11 |
| 11 | `FB297 (4FB297)` | `FB35B/FB35F` 无条件递归设置scene12 |
| 12 | `FB369 (4FB369)` | 对第三组中winner flag=1者形成名称+ID12BBF，并发聊天/可选浮动提示 |
| 13 | 无专门表项 | 保存为13；BB stage将其作为特殊恢复/起播条件。业务中文名称Unknown |

Tick 入口 `FB7F0 (4FB7F0)` 表 `FCBB0 (4FCBB0)`：scene3→`FB858`，4→`FB881`，5→结束，6→`FBEB5`，7→`FBEDE`，8→结束，9→`FC501`，10→`FC52A`；其它 scene不处理。

三条链：

```text
scene3 --[+25C动画完成]--> scene4 --[PVF轨迹耗尽]--> scene5 (等待外部)
scene6 --[+25C动画完成]--> scene7 --[PVF轨迹耗尽]--> scene8 (等待外部)
scene9 --[+25C动画完成]--> scene10 --[PVF轨迹耗尽]--> scene11 --> scene12
```

动画完成调用 `2222540 (2622540)`，分别在 `FB866/FBEC3/FC50F` 检测AL。进入4/7/10时，`[UI+240]`计时对象先调 `23C3410 (27C3410)` 清运行位，再以 `(9100,0)` 调 `23C3B60 (27C3B60)` 启动。scene4/10共用`FB275..FB28D`，scene7为`FACCC..FACE4`。旧笔记把对象写成+208不正确，本次原始指令为 **+240**。

Tick以 elapsed (`23C3660 / 27C3660`) 与 duration (`23C36F0 / 27C36F0`) 比较，再调用线性插值 `23C4660 (27C4660)` 求 `floor(7*elapsed/9100)`，达到/超过duration时固定7。索引达到任一当前选手轨迹vector长度时切组结束scene（`FBAC0/FBAC3→FBE80`、`FC129/FC12C→FC4D1`、`FC773/FC776→FCB58`）。这不是等待服务端终局标记，也未使用服务器日历时间。具体是否9.1秒结束还取决于有效PVF轨迹长度、前置动画时长与UI tick是否被调度，不能直接断言实机固定9.1秒。

## 04BB stage / scene 矩阵（Confirmed）

`D24A0 (4D24A0)` 读取31B；UI get-or-create非空时 `D2512 (4D2512)` 调 `FCCA0 (4FCCA0)`。先保存round `[UI+21C]`，清理/重建对应参赛者与轨迹集合，然后按 signed stage 经unsigned0..3表 `FD438 (4FD438)` 分派。stage是结果载荷第2字节，非04B8 state。

| BB stage | 解析组 | counter `[UI+220]` | 收到前scene条件 | 进入scene | 关键 RVA (VA) |
|---|---|---|---|---|---|
| 0 | 第一组4条；亦消费第二组2条 | 0 | scene13 | 3 | `FCDE6→FCDF9 (4FCDE6→4FCDF9)` |
| 0 | 同上 | 0 | 其它scene | 5 | `FCE17/FCE19 (4FCE17/4FCE19)` |
| 1 | 第一组4条、第二组2条、第三组1条 | 1 | scene13或6 | 6 | `FCFE2→FD019/FD01D (4FCFE2→4FD019/4FD01D)` |
| 1 | 同上 | 1 | 其它scene | 8 | `FCFFC/FD000 (4FCFFC/4FD000)` |
| 2 | 全3组 | 2 | scene13或9 | 9 | `FD1E2→FD219/FD21D (4FD1E2→4FD219/4FD21D)` |
| 2 | 全3组 | 2 | 其它scene | 12 | `FD1FC/FD200 (4FD1FC/4FD200)` |
| 3 | 全3组 | 2 | 无条件 | 12 | `FD40E/FD412 (4FD40E/4FD412)` |
| 范围外 | 已保存round/清集合 | 不经上述赋值 | 无 | scene保持 | `FCCF0`范围检查 |

因此 stage0/1/2 能分别开始第一/二/三组动画，也能在非起播scene直接展示已完成结果。stage3无条件最终结果显示。停发stage2、只发stage3会跳过第三组原生起播路径；不能据此重新定义原生流程。

`state5` 的counter由BB stage重设。连续三次 state5（没有BB覆盖counter）分别强制结束第一/二/三组，第三次立即进入最终显示。stage2在scene13/9开启第三组后，客户端自身tick也能最终进入12；因此事先下发完整第三组胜负，再拖延服务器最终标记，不能阻止本地冠军展示。是否“提前”须对比真实服务端调度/实帧时序，当前仅确证消费机制。

## 最终公告与中途恢复边界

scene12入口没有去重，遍历第三组记录 `[UI+1D4..1D8]` 中 `[entry+4]==1` 的winner。`FB70C (4FB70C)` 使用字符串ID `12BBF`，`FB711`取字符串，`FB721`格式化名称，`FB742→1662DF0 (4FB742→1A62DF0)` 发聊天。窗口已显示仅抑制浮动提示，不能抑制聊天。重复stage3或其它再次进入12的输入存在重复聊天的静态路径。

本次确认：04B8 state1..4是带公告的事件消费；04BB既是数据消费也是按当前scene决定动画/最终公告的操作。它们不是通用无副作用快照。中途恢复若简单重放state4+BBstage2，会重启第三组并最终本地冠军；BBstage3恢复会触发最终聊天；state5没有直接公告但第三次/ counter2会进入冠军公告。不能通过“不发04B8公告state”就保证完全静默恢复。

原生无公告初始化的独立入口（含093D）由另一路研究负责，此报告不以high-bit插件替代原生证据。待该链明确后才能给中途恢复的完整推荐顺序；当前静态证据不足以宣称已存在任意阶段的无公告精确时间恢复。无服务器时间输入的本地动画也不能从31B BB单独还原动画已播放毫秒数。

## Unknown / 待补充

- 12BCB/12BCC/12BBD/12BBE/12BBF正文未读取，不以状态号或既有中文截图直接定业务语义。
- tick虚表调度是否在UI隐藏时持续执行，需要继续静态追踪或实机验证；“窗口对象存在”不能证明tick运行。tick的唯一完整VA地址字面量命中在RVA`2C6BBEC`（VA`306BBEC`），没有直接E8调用。
- 093D等初始化入口能否只初始化manager/state/scene而不公告，交叉研究后补结论。
- 9100的计时参数/插值与轨迹索引是Confirmed；毫秒单位及约9.1秒描述为High，尚未独立识别IAT计时函数名和全局tick标度。`2453B20 (2853B20)`实际读取全局计时索引并解密返回，不直接调用系统时钟。

## scene6/9入口穷举与当前实现差异

本次扫描整个dump所有`E8 rel32`对`FA1B0 (4FA1B0)`的引用，共21处，且对完整函数VA字面量扫描无命中；结果保存`scenes-xrefs.txt`。逐处反汇编后，没有发现scene5→6或scene8→9的另一自动入口：

| 调用 RVA | 来源 | 传入 scene |
|---|---|---|
| `FB35F` | setter scene11 | 12 |
| `FB877/FBE84/FBED4/FC4D5/FC520/FCB5C` | tick | 4/5/7/8/10/11 |
| `FCBF6/FCC02/FCC0E/FCC1A` | B8 raw0/1/2/4 | 0/1/2/13 |
| `FCC30/FCC4B/FCC66` | B8 raw5，counter0/1/2 | 5/8/11 |
| `FCDF9/FCE19` | BB0 | 3/5 |
| `FD000/FD01D` | BB1 | 8/6 |
| `FD200/FD21D` | BB2 | 12/9 |
| `FD412` | BB3 | 12 |

在马战代码RVA`F0000..FE000`对`+218`字段的指令扫描，比赛窗口scene仅发现构造器`F9F09 (4F9F09)`写初值0，以及setter`FA1ED`。另一些`+218`访问属于投注窗口/其它类，不能混成同对象。构造器还在`F9F16 (4F9F16)`将counter初始化为-1（**未收到BB设置counter之前，state5不匹配0/1/2，也不会正确轮转**）。`scenes-scene-field-xrefs.txt`和`scenes-ui-constructor-asm.txt`为复核证据。

因此：

- BB1唯一已找到的scene6起播是“原scene13或6”；BB2唯一已找到的scene9起播是“原scene13或9”。scene5/8不会被tick自行改成6/9。
- 父代理提供的当前服务端序列（CurrentImplementation，非原生协议证明）：Quarter `state4→BB0`；Semi仅BB1；Final无包；End `state2→BB3`。按本次静态链，Quarter能`13→3→4→5`；后续在scene5发BB1只进入8，跳过第二组起播；在scene8简单补发BB2会直接进入12，仍跳过第三组起播。禁stage2是绕过路径，恢复stage2本身也不足以修正顺序。
- 一种**可达性示例而非原生服务端推荐**：每组先使scene13，再下发BB0/1/2，分别可进入3/6/9；但原生使13的已知B8 state4同时有直接公告。是否应在每组广播state4、是否有另一原生无公告初始化分支，必须与初始化研究或实帧交叉核对；当前不能给完整“正确原生serversequence”。
- 全dump E8扫描不排除动态生成/计算函数地址或窗口外代码直接修改字段；但对已识别马战函数、跳表、tick、setter路径的上述结论为Confirmed。

## 04BB四字节记录与PVF Win/Loss交叉读取（Confirmed）

每条记录`[a:i8, aAux:u8, b:i8, bAux:u8]`由`F7A70 (4F7A70)`消费。a匹配当前参赛者index时设`entry+4=1`，b匹配时设`entry+4=0`；即winner/loser由**字段位置**决定，不由aux决定。未匹配当前集合的index不产生对应轨迹更新。

资料getter `16182F0 (1A182F0)`只是返回`[manager+8]`，其后实际查询函数`24FDC00 (28FDC00)`接收：隐藏返回对象、selector、knightIndex、aux。selector非0取资料对象`+78`，selector0取`+8C`，进行knightIndex→aux两级lookup后复制vector。

本次解密loader字面量并核对写入地址（`scenes-pvf-loader-asm.txt`、`scenes-literals.txt`）：

| PVF标签 | 字面量 VA（RVA） | loader RVA (VA) | 资料对象字段 |
|---|---|---|---|
| `[win]` | `3627690 (3227690)` | `24FD925..24FD9D5 (28FD925..28FD9D5)` | `+78` |
| `[loss]` | `3627660 (3227660)` | `24FDA02..24FDAB8 (28FDA02..28FDAB8)` | `+8C` |

loader外层容量按5组循环，内层最多7整数；实际PVF有几组不应从代码容量推成内容。aux0就是第0组索引，原生消费未禁止0；本次没有证据要求服务端改选其它aux值。

第一组winner路径的完整例子：

```text
F804D/F8054: 查 Loss(a, aAux)，返回临时vectorA
F8073/F807A: 查 Win(b, bAux)，返回临时vectorB
F80A1: 把vectorB复制进a对应entry的轨迹
F80E0..F815F: 再按vectorA中值100的项目进行处理
```

第二/三组同构。b路径例`F81E2/F81E9`查询`Loss(a,aAux)`再`F8213`赋给b轨迹。**不能将aux笼统命名为winnerWinResource/loserLossResource**：a/b字段与Win/Loss资料存在交叉消费。当前可以确证它们参与轨迹资料索引及动画，不能仅凭标签推成伤害公式、比分、服务端胜率或战斗能力。黄色attackType icon没有在这条BB轨迹选择中参与；动画从服务器已给定的a/b结果及PVF轨迹构造。

## 04B9骑士资料UI映射（Confirmed及边界）

`D2330 (4D2330)`读90B后，`D2376..D238C`从payload+2按stride11共8条调用`F5C10 (4F5C10)`；该函数将完整11B存到投注窗口`[UI+1B8]`按signed knightIndex查得的记录，再刷新当前选中骑士`F5130 (4F5130)`。同时比赛窗口通过`D23B3..D23C0→FA090 (4FA090)`消费index和第10字节（保存为参赛者entry+5）。

| 11B记录偏移 | 当前消费 | 证据 RVA (VA) |
|---|---|---|
| +0 i8 | 资料记录key、比赛参赛者index | `F5C24 (4F5C24)`、`D23B7 (4D23B7)` |
| +1 u8 | `conditionImg`图片index=该值+3；无0..3范围检查 | `F51AC (4F51AC)`、`F54EF..F54F6 (4F54EF..4F54F6)` |
| +2..5 | 完整保存；本次未追完其全部其它调用 | `F5C19/F5C43/F5C48` |
| +6 u16、+8 u16 | 格式串12BC7的两个计数；12BC8显示百分比按`+6/(+6 + +8)*100`，两者为0时百分比0 | `F567A/F568B`、`F56F3..F5756` |
| +10 u8 | 资料隐藏门，仅精确值1，且比赛scene<3；比赛参赛者entry+5亦保存该值 | `F555E/F5562`、`D23B3`、`FA0FC/FA110` |

`conditionImg`字面量VA`306BB74 (2C6BB74)`；`attackTypeImg`字面量VA`306BB50 (2C6BB50)`。攻击图标来自独立资料provider：`F51B9→16182F0 (1A182F0)→24E51E0 (28E51E0)`，按knightIndex在资料对象+64查attack value；`F5502..F5509`使用图片index=attack value+8。这两个字段并非同一来源，也不是BB的aux。

当比赛scene<3且+10==1，`F5571..F55EA`对投注/计数/百分比/名字四个文字控件使用字符串12AC9，`F55EC..F560E`隐藏两种icon；scene>=3时忽略这项隐藏门，正常显示资料及icon。因此+10只叫全局hidden会丢失阶段条件。其他值（如2）不触发这条精确值1的隐藏分支。

condition0→image3，attack0→image8是Confirmed；“condition0具体中文状态”“attack0是攻击型/物理型等”仍Unknown，本次没有tooltip/字符串正文支持这些业务释义。计数0、condition固定0或aux固定0不能单凭默认值被宣判布局错误：它们可按现有布局被消费。是否需更完整业务资料模型，是CurrentImplementation范围，不能冒充ClientStatic协议要求。

`F60CF (4F60CF)`保存状态gettercall目标为 **VA`02846D50` / RVA`02446D50`**；`2446D50`指令是`mov eax,[ecx+4]`。两种地址若明确标注VA/RVA没有矛盾。
