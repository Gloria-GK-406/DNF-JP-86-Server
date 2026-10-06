# A21 马战：原生初始化与管理器生命周期复核

证据级别：本文均为 **ClientStatic**。Confirmed 表示固定 dump 中可复核的指令与控制流，不代表实机已验收。2026-10-06完成管理器及通用006C初始化路径的有界复核。

## 当前结论

1. **Confirmed：04B8 是带事件副作用的状态通知，不足以代表客户端完整初始化。** 它同时更新马战 manager 的 state、通知马战主 UI，并可能公告。初始化还存在独立的通用活动可用性门：EventManager 事件表必须存在 `0x093D` 才能从通用入口进入马战 UI。
2. **Confirmed：通用活动表的原生初始化消息是 NOTI `0x006C`。** 注册地址 VA `0141A0BF..0141A0CD`，handler VA `01419E30`。它调用 `014196B0` 解析活动表，再沿 `01416BD0 → 0141A1E0` 应用事件启用/取消。不能把 `0141A1E0` 自身误称网络 reader，它是事件变化应用函数。
3. **Confirmed：马战 manager 有显式清零入口；构造未显式初始化 state。** reset 是 vtable+8 的 VA `005B64C0`，指令 `mov dword ptr [ecx+4],0`。构造 `004D29A0` 只设 vtable 并注册 handlers，不能据此断言新建时 `state=0`。亦未证明 reset 必在登录调用。
4. **High，范围受限：目前已定位的马战特定路径中，只有04B8写非零state；没有找到单独静默同步该state的已确认消息。** 这是在完整管理器构造/注册/vtable、13处派生RTTI代码引用及通用service-key45候选检查后的结论，不能由“setter只有一个caller”升级成全程序排除证明。
5. **Confirmed，针对已解析路径：006C的093D事件不会经generic event object给马战manager初始化state。** 创建工厂仅处理0843..08AE，093D超界返回空；通用事件应用函数的特殊分支上界08EF，093D走默认。006C建立的入口可用性与04B8赋的轮次状态是不同对象的数据。

## 固定输入身份

- dump：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`
- base：`0x00400000`；`RVA = VA - base`。
- dump SHA-256（本次Get-FileHash复核一致）：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。
- EXE预期 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 不启动/修改原生客户端，不将0190插件或0x80扩展当作原生证据。

## 管理器构造、注册和重置

| 项目 | VA | RVA | 可复核结果 |
|---|---|---|---|
| 分配/构造/注册服务 | `0140E039..0140E06F` | `0100E039..0100E06F` | 分配12B，调用马战构造，以键0x45登记service |
| 马战构造 | `004D29A0` | `000D29A0` | `004D29D0`写vtable，`004D29D6`调注册；无+4赋值 |
| manager vtable | `030624F8` | `02C624F8` | 4 slots如下 |
| slot0析构 | `004D2530` | `000D2530` | 销毁manager+8对象，回收manager |
| slot4注册 | `004D2870` | `000D2870` | 建manager+8内容对象，注册7个消息处理器 |
| slot8 reset | `005B64C0` | `001B64C0` | `[this+4]=0`，无UI/网络副作用 |
| slotC默认检查 | `01834060` | `01434060` | 返回false |
| setter | `004D2150` | `000D2150` | `004D2158`将argument写`[this+4]`，还更新下注UI |
| getter | `02846D50` | `02446D50` | 读`[this+4]`，这是共享小函数，不能仅靠getter xref认定类型 |

注册集合（Confirmed）：ACK04E6→004D2030；ACK04E7→004D20B0；ACK04E8→004D25B0；NOTI04B8→004D2690；NOTI04B9→004D2330；NOTI04BA→004D23E0；NOTI04BB→004D24A0。注册函数不解析网络，也不设非零state。

通用service reset路径 `014151A0 → 0140D030 → 0140CEC0 → object.vtable+8` 已定位；其中 `0140D030` 从字符串解析service key，并不是已确认的登录全量reset。故“登录必清零”尚未排除或证实。

## 04B8状态与窗口门

`004D2690`读取3B后，经派生RTTI `0396012C` 找 service45，`004D26F9`以`movsx`读取state byte，`004D2700`调用setter；随后`004D2728`调用主UI `004FCBD0`。继而 packet 原始state再次用于公告分支，未见旧值去重。

窗口open VA `004F6050`：`004F60C8`先发送04E6，`004F60CF`读取manager state，返回0即`004F60D9`使用字符串12BC0弹窗/拒绝。因此“已经发送查询”不说明窗口会等查询响应之后重新读取状态；本函数顺序就是先发送、再同步检查已有state。

## 覆盖范围与可复核工件

- `initialization-scan.py`：只读dump工具，输出VA、RVA和指令字节；`call`扫描全dump E8/E9相对分支，`ref`扫描u32绝对引用，`inventory`生成派生RTTI完整函数与service45候选。
- `initialization-derived-inventory.txt`：派生RTTI0396012C的 **13处代码引用** 与padding界定的完整周边函数；另收集getter前120B含`push45`的候选。这是筛选+人工检查范围，padding界定与raw分支扫描均不能证明全部机器码边界。
- `initialization-generic-reader.txt`：事件应用函数0141A1E0的完整指令区间；093D大于高区switch上界08EF，走默认收尾；前置generic event对象vt+4/+8链在下节复核。
- `initialization-core-evidence.txt`：构造、vtable、重置、注册、04B8、006C调用链、generic factory与窗口门的原始指令；上述factory疑点已在下节排除。

13处派生RTTI code references：004D26D3、004F2BF3、004F2D88、004F3092、004F3B9B、004F432E、004F4B69、004F5164、004F609E、004F63DA、004F7AA0、004FA4EB、004FD3EC。两处额外非代码引用是RTTI结构。

## 006C / 093D 的独立初始化门与间接写入排除

入口门 `025ABDC9 push093D → 0141A170 → 014172D0` 查 EventManager+18 event map是否含093D；这个gate和service45+4 state是两个存储位置。

006C注册见 `0141A0BF push01419E30 / 0141A0C4 push6C / 0141A0CD call01637720`。注意opcode使用短`push6C`，只扫描`68 imm32`形状会漏掉此原生消息。

handler `01419E30` 调 `014196B0`读活动表，后调 `01416BD0` 对新旧event map做比较；`01416E46`调用 `0141A1E0(eventID, data, enableFlag)` 应用变化。后者前置链：

```text
0141A353 → 01643A20：从主游戏对象+b019c的generic event对象map找eventID
若已有对象：0141A372(vt+4)启用 / 0141A379(vt+8)取消
若没有且enable=true：0141A390 → 01647EA0 → 00C5E180创建
```

创建工厂 `00C5E1AA add eax,-0843 / 00C5E1B2 cmp eax,006B / 00C5E1B5 ja00C5E25F`。093D与0843之差00FA，大于006B，故 `00C5E25F..00C5E265`将返回shared_ptr的两dword清零；不存在有效对象的vt+4启用调用。全dump扫描01647EA0仅找到此处一处E8 direct call，也没有u32地址引用。该结论排除这个已确认factory所生成的093D typed对象，并不等价于穷尽所有不经此factory写主游戏对象+b019c map的可能方式。

0141A1E0后置特殊event switch对高编号仅处理0833..08EF（`0141AD6C..0141AD84`）；093D超界，直接走0141AF3A收尾，不调用马战setter或service45 reset。取消093D在这条已解析变化链中同样不直接重置service45；入口gate随后会因eventID不存在而拒绝打开。无法由此保证已打开窗口如何关闭，006C其余专用刷新函数需与UI证据交叉判断。

因此，006C是完整马战闭环的一部分，但本路径没有证明原生提供“静默地将state初始化为当前合法phase”的能力。只同步event093D不能替代04B8状态。现有服务端有没有发送006C属于CurrentImplementation，应由主代理独立核对。

协议子代理独立复核了006C变长entry、event9条件tail和完整 `01417650..01418130`：该tail没有093D、service45或马战派生RTTI直接项。这支持上述有界排除，但006C本身还会刷新calendar/UI及其他系统，不能从不走04B8公告推出“006C所有投影均静默”。交叉工件：同目录 `protocol-set-event006c.txt`、`protocol-set-event006c-tail.txt`、`protocol-set-wrapper-tail-full.txt`。

该子代理另指出006C handler的顺序为 `014196B0(tempMap) → 01417650(this) → 01416BD0(tempMap)`；条件tail查看manager中event9的时点早于新表同步。除非另行排除parser对旧manager map的间接修改，不应将event9尾字段条件简单解释成新包活动表，也不能据此建议安全重发006C。本报告不将这一未闭合时序用于马战state结论。

## 尚未排除与证据边界

1. 已排除NOTI006C→0141A1E0→01647EA0这条已确认093D工厂链写state；其他不经该factory写generic event map的路径未做全程序别名证明。
2. 经基类指针、保存的裸指针、bulk copy/memset、通用虚调用或保护区代码对manager+4的写入，不能靠派生RTTI或setter caller扫描完全排除。
3. allocator返回内容/运行时reset时序没有动态证据；构造state未写不等于保证state0或任何其他值。
4. 即便确认006C设置093D gate，也不自动证明它初始化state或静默打开投注UI。两道门须分别验证。
