# A21 称号簿 UI 兼容补丁：可执行 hook 研究

日期：2026-10-08。对应同目录 `TaskContract.md`。本文件是 ClientStatic 研究与 ProposedImplementation；尚未构建或加载补丁，不是 ClientVerified。

## 1. 目标与边界

保留书中 canonical 原件，以服务器明确的佩戴来源 `(category,index)` 标记唯一的装备中行；成就槽与自由槽统一得到 state3/frame6。不得按 itemID 推测来源，也不修改 PVF candidate。保留原生图片、动画、引用计数、外观选择及非书物品操作。网络发送、ACK 与 pending 的完整方案由同任务其他研究合并；UI 方案不能单独解决物理交换。

基准 dump：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，VA base `00400000`，本次实算长度 `122085376`，SHA-1 `12924af255ef6b22282ab37e3d2198b594968b83`。最初上下文的 `F8244D3...` 未证实为本 dump 哈希，不能混用。以下 RVA=VA-00400000。实际插件安装必须验证运行镜像身份与解壳后目标字节。

## 2. 已证明的 UI 状态与副作用

`02578240` 重建分类：entry candidate 空时跳过；state1保留动画；物理书槽有实体写2、空写0；仅本地书视图、空槽、candidate严格匹配 actor 槽13时写3。`0257B19F..0257B1F4` 只由 state3选择frame6“装備中”。

`025767F0` 是原生状态 setter：先查entry所属group；有效group时写entry+1C，并维护cache及引用。特别注意 `025769C2..025769D3`：新state2且旧state0会调用 `02576560` 初始化条目静态图片及可选ANI。完整body未见消息toast/chat调用，不能将这个函数称为获取提示发送者。因此只在rebuild返回后改成3，可能已启动条目ANI；反过来直接跳过此调用也可能缺静态图像。安全静默方案见§11。

真实字段：entry stride `0x4C`；+0为PVF SlotType（=-1为无有效定义）；candidate proxy+4、begin+8、end+C、capacity+10；state+1C；QuestId+40；cache pointer+44；cache+20为展示定义ID。candidate不要改写。cache是图像/展示引用对象，不是item实例。

`02574000`只按QuestId=-1允许取到背包。`0257CB60..0257CBAC`使用此门；固定成就槽失败走BC32提示。`02579F65..02579F7C`为QuestId非-1选择槽背景3/4，=-1选择7/8。这些原生差异仍保留；统一装备标记不意味着统一成就条件、背景或非佩戴时的取出规则。

## 3. 服务器绑定与本地模式

建议缓存服务器快照：`session/character identity, generation/sequence, active, category, index, displayDefinition`。active=false是明确的“无书来源佩戴”，与“尚未收到快照”区分。definition只用于指定行的展示，不用于找行。重复ID两个slot也只能命中一对category/index。

启用UI覆盖须同时满足：插件协商已完成、绑定快照有效、当前角色一致、`DWORD[03A5C64C] == 03A5C8DC`。这正是原生 `0257848C..02578496` 的本地视图门；其他人的书使用 `03A5C8C8`。绑定尚未到达时不能把unknown当none，也不能在已协商镜像模式下回落到物理书交换；书镜像按钮应暂时拒绝/等待快照。

绑定只在成功投影时提交，失败保留上一份；角色切换、退出、连接变化清空。UI改动在游戏/UI线程执行，网络线程只缓存不可变快照和dirty标记。不能从网络线程直接写entry/cache。请求单飞、去重和失败恢复由插件自身状态负责，不借原生容器pending模拟。

## 4. Hook 签名与安装字节

下列声明是x86 ABI说明，非生产代码。thiscall trampoline使用ECX为this、callee清理显式实参；C detour可按工程惯例用 `__fastcall(self, unusedEdx, ...)` 接入口。cdecl trampoline/detour都由caller清栈。C编译器对thiscall函数指针语法须由实际ClientHack工程验证，不能偷换为stdcall或cdecl。

| 用途 | VA / RVA | 原型与返回 | 前24B（按实际可读长度核验） |
|---|---|---|---|
| 分类重建 hook | 02578240 / 02178240 | `void __cdecl rebuild(int category, uint8_t refreshGroup)`；第二参数占4B栈槽，仅低字节使用 | `55 8B EC 81 EC 40 02 00 00 A1 E0 CE 99 03 33 C5 89 45 FC A1 1C CA A5 03` |
| 状态 setter hook | 025767F0 / 021767F0 | `void __cdecl set_row(void *row,int state,int displayId)` | `55 8B EC 6A FF 68 80 DE E7 02 64 A1 00 00 00 00 50 83 EC 1C 53 56 57 A1` |
| 取出资格 guard hook | 02574000 / 02174000 | `uint8_t __thiscall can_take(void *ui,int category,int index)`；ret8，ECX未参与原逻辑 | `55 8B EC 8B 45 08 8B 0D 1C CA A5 03 56 50 51 E8 2C 4E 62 00 8B 70 04 8B` |
| 点击包装，可选 | 025738A0 / 021738A0 | `void __thiscall book_op(void *ui,int category,int index,int op)`；retC | `55 8B EC 83 B9 E8 02 00 00 00 75 1D 8B 45 10 8B 4D 0C 8B 55 08 6A 00 50` |
| UI交互，观测/额外guard | 0257CA60 / 0217CA60 | `uint8_t __thiscall interact(void *ui)`；AL返回，ret | `55 8B EC 6A FF 68 58 E9 CF 02 64 A1 00 00 00 00 50 51 53 56 57 A1 E0 CE` |
| 背包拖入书，可选guard | 0257D130 / 0217D130 | `void __thiscall put_from_main(void *ui,int category,int sourceSlot,int targetIndex)`；retC | `55 8B EC 6A FF 68 C8 E4 E7 02 64 A1 00 00 00 00 50 53 56 57 A1 E0 CE 99` |
| 拖动交互，可选 | 0257D410 / 0217D410 | `void __thiscall drag(void *ui)`；ret，无显式实参 | `53 8B D9 8B 0D B8 C9 A5 03 E8 A2 2F D8 FF 83 F8 02 0F 85 9A 00 00 00 8B` |
| 统一装备sender，需要与网络方案合并 | 016FA5F0 / 012FA5F0 | `uint8_t __thiscall equip(void *container,int bookOrMainSlot,int equipSlot,int maxPending,int op,uint8_t bypass)`；ret14 | `55 8B EC 6A FF 68 B4 AB DC 02 64 A1 00 00 00 00 50 83 EC 58 53 56 57 A1` |
| 底层物理移动guard | 017012D0 / 013012D0 | `uint8_t __thiscall move(void *source,int sourceSlot,int opaqueArg2,void *destination,int destinationSlot,uint8_t flag)`；ret14；arg2/flag完整语义未命名 | `55 8B EC 6A FF 68 E1 B1 DC 02 64 A1 00 00 00 00 50 83 EC 44 53 56 57 A1` |
| native模型getter，调用而非必hook | 02B98E40 / 02798E40 | `void *__cdecl get_rows(void *model,int category)` | `55 8B EC 83 EC 18 56 8B 75 08 8D 45 0C 50 8D 4D F4 83 C6 28 51 8B CE E8` |

这些是正常函数入口，前方没有已见保护dispatcher，前5B可由完整指令覆盖；实际MinHook relocation结果仍须构建验证。不能直接把 `0257A840`、`0257CB60` 等函数内部位置当MinHook入口，也不要覆盖不完整指令。所有必需hook验证成功后一次性启用；任一失败回滚本插件已安装hook，不留下半套镜像模式。

## 5. 按category/index读取模型，不缓存row指针

`model = DWORD[03A5CA1C]`。native `02B98E40(model,category)`先查map是否有该key，成功返回entry vector，未找到返回null。返回vector+4/+8/+C分别为row begin/end/cap。`row = begin + index*0x4C`，须检查category0..4、index>=0、end>=begin、差值整除0x4C、index<count、内存可读。

该getter使用map包装器和迭代器校验（异常路径 `02B98E9F`）；不要把它当任意损坏指针的安全探针。插件可先读有效model及现有分类，游戏线程内调用。替代是只读树遍历：model+30是head，head哨兵+21=1；node left/parent/right=+0/+4/+8，key+C，value vector从+10开始；value begin/end/cap=node+14/+18/+1C。禁止误用model+2C为head。手写遍历需有节点数量上限、循环检测和可读检查。

setter收到row时，可对五类现有vector范围反查，要求 `(row-begin)%0x4C==0` 且row在范围内，得到唯一cat/index；只比较地址所属范围，不读取itemID找来源。保留重建调用栈上的category加速，但嵌套调用和独立0168调用仍要范围核实。

角色切换、PVF重载、vector重新分配或UI缓存重建会换row/cache地址。只持有服务器绑定key；每次操作重新解析。cache+44由native负责引用计数和生成，插件不要释放或写其shared-pointer内部字段。

## 6. 推荐状态覆盖算法

最小必须组合为setter hook + rebuild hook + sender镜像分流 +物理移动guard；仅改显示不满足契约。

1. `rebuild` detour始终先调用native trampoline。setter detour在该过程内将当前绑定行的requested state0/2/3变为3；必要时以服务器displayDefinition作为该行显示参数。这样不经过old0→new2条目ANI分支，但必须补齐原生cache静态资源，见§11。active来源的state1是否立即改3，应按已提交佩戴事实处理；其他新成就的state1完全保留原生动画。
2. 权威绑定模式下，其他行的requested state3不能保留原生itemID匹配结果；使用该行实际本地书实体存在与否归一化为2或0。这包含相同ID的固定候选行，以及actor13为普通背包称号而active=false的情况。普通装备仍穿戴，只是不冒认书来源。
3. 调用原生setter执行最终参数，保留它的group判断、cache引用和资源管理。若有效绑定行由于group缺失而native未写state，不应静默假装成功；记录不兼容并停用书镜像动作，交实现阶段明确处理，不能在未知group/candidate里造结构。
4. `rebuild`返回后重新解析该category并做一次绑定核验；补齐被state1/候选空跳过的合法绑定行，以及清理残留错误state3。用native setter trampoline及§11资源构建更新，不写candidate，不重复播放ANI。invalid/disabled row应拒绝绑定，不强造UI条目。
5. 新快照被UI线程消费时重建五分类。`refreshGroup`使用原生语义；state2和3都计为已有条目，镜像不会改变成就数量。不要每帧无差别重复重建。原生尾部 `0257857C→02577780(actor)` 保留，不hook掉成就效果计算。
6. 镜像取消时上一active行应回state2，因为canonical实体仍在。替换时旧行2、新行3；不经过空书槽0，不重新获物。失败不换binding；快照和physical未齐时等待，不猜物品ID。

`025767F0`并非一个纯字段setter，因此实现必须验证这些分支：旧0→覆盖3、旧3→2、原生wrong row3→实际0/2，以及原生新成就state1→2。不能全局屏蔽条目ANI或0168达成通知；新的成就达成仍应显示原生动画。

## 7. 点击与取消：保持UI，截住物理sender

renderer `0257A840..0257A8A9`：state2→op0；state3→op2；Shift/键检查true的state2分支 `0257A87D→02574680` 为独立外观0239，必须保留。正常sender是 `016FA5F0(this=titleTables[cat],slot=index,equipSlot=13,maxPending=1,op=0/2,bypass=0)`。

`025738A0`只覆盖一部分点击入口；renderer和 `0257D4E8`直接调用016FA5F0。因此建议统一在016FA5F0按this精确等于本地五类书容器指针、index范围、装备13、已协商模式分流。书镜像操作走定制请求/等待状态，不能进入native函数体，否则仍有旧称号归还、pending及本地交换。非镜像/非书请求调用原trampoline。

state3取消入口仍传bypass0，原生在 `016FADFB→0257CD90` 找到旧固定空槽时，会先递归op2/bypass1再继续外层op2，可能产生双019C。因此统一分流必须发生在原函数入口，不能仅改ACK或者仅改变bypass。定制动作必须由明确cat/index指定来源，服务器验证当前绑定，重复事件合并为一个在途请求。

真实背包称号不由书绑定认领。角色装备栏对镜像的卸下/替换可能进入普通0013路径，UI书hook不覆盖该入口；网络方案必须把当前mirror装备的取消转换为绑定动作，并保留非mirror普通生命周期。不能仅用actor13.itemID决定是镜像。

## 8. 拖出限制与拖入保留

第一层：hook02574000。当有效绑定的category/index等于请求行时返回false，否则调用原函数，保留固定QuestId门与自由槽正常取出。此hook覆盖 `0257CB60` 的书实体拖出资格判断；state3的CA60路径本身走op2，不拿书原件进入普通拖出。

第二层：hook017012D0物理移动。在本地协商模式且绑定有效时，source指针等于 `DWORD[03A5C8DC + category*4]` 且sourceSlot等于绑定index，拒绝物理搬移；destination为该绑定书槽时也拒绝覆盖原件。不能用物品ID关联。其他移动完整调用native trampoline。这层防止快捷键、旧拖动上下文、非当前UI状态绕过第一层。未知移物路径仍由服务器同样禁止移走active canonical原件作最终边界。

`0257D130(ui,category,sourceSlot,targetIndex)`为背包→书：先要求目标物理空，再要求目标state!=3，然后调用017012D0。保留此路径，不整体替换；绑定中目标应即使状态暂时未刷新也被guard拒绝。非active空自由槽照常接受PUT，候选保持PVF定义。

`0257D410`读取拖动context：context type2（角色装备）直接发op2到当前选中书目标；type3/4（物品来源）经0257D130。因此不要把“拖到书”全部禁用。mirror装备拖到其绑定书行应走定制取消；拖到不同书行不能改canonical位置或重新绑定，应拒绝/保持原件。普通真实背包/装备称号仍可依原生PUT规则进入合法空槽。

## 9. 必须保留与不得依赖的 native 部分

- 原始分类重建、setter的图片/引用管理、非active state1达成动画、QuestId非佩戴取出规则、非镜像物品移动。
- Shift外观 `02574680`、0239处理、`02574320`/frame39选择保持独立；外观clone不能触发frame6来源绑定。
- 不改候选vector，不造item对象，不写item+3C4作为锁，不调用受保护052583D2模拟确认。空pending的该helper已证实直接false；旧“空队列继续交换”结论作废。
- 不缓存row/item/cache裸指针跨回包、换角色或重建；不从itemID选行；不让ACK旧来源/低序列覆盖新binding。

## 10. 未知边界及实现验证点

首次PUT所有保护/间接metadata写者尚未穷尽；本方案不依赖此机制，直接使用明确绑定。017012D0的arg2与flag完整语义未命名，不允许改动传递值。所有物理移动入口尚未穷尽，服务端拒绝active原件移出仍必需。MinHook实际建trampoline、C ABI桥、网络handler/UI线程一致性需实现验证。

最小逻辑验证：相同ID两个书槽仅指定行3；active=false不误标固定空候选；绑定快照先于/晚于实体投影均最终收敛；取消/替换不走获得分支；查看他人书不受本人binding影响；active原件拖出被两层guard拒绝；解绑后自由槽允许取出、固定槽仍遵守QuestId规则；Shift外观保持0239；失败/旧回包不改变binding；重登录换row地址仍正确。最终窗口和输入行为需用户实机验收，静态研究不等于已完成修复。

## 11. 同步快照的 setter/cache 历史方案（2026-10-09已否定清展示ANI）

**后续实机反证：** 用户确认切换称号簿后整页称号动画变空。以下清cache+8的建议虽按原生引用计数释放，却误删称号自身展示ANI，不是解锁效果；真正state1解锁效果为cache+10。本节保留历史研究，不能继续作为安全推荐。当前修订保留展示资源、缩小分类刷新，并处理悬浮提示借用旧装备对象的问题，见 `render-recovery-20261009.md`。

父任务提出以syncDepth下预写state规避old0分支。静态判定：**不存在same-state early return**。有效group路径 `0257683F`读取old，`0257684E`写new，`02576851..0257685F`在displayId!=-1时仍调用cache getter并写cache+20，即使old==new。早退条件是group为空 `0257682B..2D`，不是state相等。

但是old预写2/3后不再进入 `025769CB→02576560`；新cache的静态图像可能为空。renderer `0257A9EE..0257AA05`直接消费cache+18，不能只靠cache+20 ID正确就宣称图标正确。

`02576560`签名 `void __cdecl init_row_resources(void *cache,void *row)`：

- `0257659C`以cache+20定义ID取得PVF物品，`025765C4..D2`更新row+20展示文字。
- `025765D7..02576612`取得物品静态frame并写cache+18 shared_ptr。
- 满足物品资源条件时，`0257669F..02576761`生成或重启cache+8 ANI。
- 本函数未见全局toast/chat调用。原生0168投递03A8D2CC通知是另一条链，不能被这里的静默保证替代。

使用原生shared_ptr move-assignment **028894B0 / RVA024894B0**清除cache+8动画，保留cache+18静态图像。原型 `void *__thiscall move_shared(void *destination,void *sourcePair)`，ret4，返回destination；来源是两个DWORD，函数消耗来源并按native引用计数释放旧destination。传栈上 `{0,0}` 源pair有效，不需要调用会额外分配control block的04288C0。不能直接memset cache+8/+C，避免泄漏/双释放。

推荐同步过程（仅客户端已协商的完整0166原件快照/绑定投影，游戏线程内）：

1. 有效本地row、group、物理canonical和正数展示ID齐全后，保存旧cache指针及旧cache+20。期望state由唯一cat/index绑定决定：active行3，其他已物化行2。
2. 在syncDepth内先写已知普通字段row+1C=desired，再调用原生setter trampoline(row,desired,id)。不预写candidate、cache指针或共享引用。
3. 重新取 `02575EC0(row)` 的cache；如果新cache/旧ID不同/cache+18为空，调用原生 `02576560(cache,row)` 补静态图像和文字。随后即使helper没有调用，也可用028894B0将cache+8移动赋值为栈上空pair，去掉同步产生/残留的条目ANI。
4. 检查row.state、cache+20及静态图像。ID变化却保留旧静态图像是失败，不允许只检查state。资源加载失败要记录，不能自行造fake资源对象。
5. desired0的正常移出清理仍使用原生old→0，不用上述预写掩掉原生2→0清图片逻辑。非sync的0168/new-achievement state1路径调用原生，不静默、不清cache+10动画。

这直接回答“预写后cache还能重建”：数值state和cache+20能更新，静态图片必须显式借原生资源构建补齐；使用native引用释放去ANI，才是完整方案。此路径包含真正的插件内存改动，当前研究未执行，须实现测试及实机观察验证资源生命周期。

补充校验字节：

| 函数 | 前24B |
|---|---|
| 02576560 init_row_resources | `55 8B EC 6A FF 68 38 DE E7 02 64 A1 00 00 00 00 50 83 EC 3C A1 E0 CE 99` |
| 02575EC0 cache getter | `55 8B EC 6A FF 68 CB 17 DC 02 64 A1 00 00 00 00 50 56 A1 E0 CE 99 03 33` |
| 028894B0 shared_ptr move-assignment | `55 8B EC 8B 45 08 83 EC 08 56 57 8B F9 8D 75 F8 33 D2 33 C9 3B F0 74 0E` |
| 02573E30 group getter | `55 8B EC 83 EC 14 8B 0D 1C CA A5 03 53 56 57 33 FF 89 7D F8 57 51 E8 F5` |

实际ClientHack实现工作区由父线程指定为 `E:/Project/DNF/DNF-JP-86-ClientHack/.worktrees/title-book-projection`；本文件只记录研究，不在该目录修改生产代码。

## 12. 装备称号13拖向背包装备页：精确入口与最小分流

2026-10-08 追加。只读检查实现 `ClientHack/plugins/0200_title_book_mirror/title_book_mirror.c`：现有send_item拦Main空槽没有限定op；现有move_item仅guard明确的book source/target。以下是静态确认，不是新实机抓取。

### 12.1 目标拖动确定经过016FA5F0 op6

背包格的drop处理入口 **015B0360 / RVA011B0360**（thiscall，无显式实参，ret；prologue `55 8B EC 83 EC 20 56 8B F1 83 BE D8 00 00 00 FF`）。已选中格检查通过后：

1. **015B039C..015B03DA**读取全局UI交互mode和拖动context；要求mode2。UI+24保存目标物品容器，UI+28+UI+D8是选定的目标物理slot。
2. **015B03F6..015B03FB**检查context[0]==2（装备来源），跳 **015B0AAA**。context6/C/17也跳此分支，未扩展命名其所有来源。
3. **015B0AC2→016F00A0**取得拖动物品。016F00A0同样要求mode2；context2经jumptable进入 **016F00E0..016F00EC**，调用 `actor.1843710(context+8,0)`，即从角色装备位置取实例，而非从Main物理格取实例。
4. **015B0AE3→0166F6D0**取得拖动物品的装备位置。0166F6D0只有 `mov eax,[ecx+1310]; ret`；称号返回13。
5. **015B0AF1..015B0AFB**按以下参数调用：

   `016FA5F0(this=UI+24容器, index=UI+28+UI+D8, wornSlot=13, maxPending=1, op=6, bypass=0)`。

   调用前push顺序是0、6、1、equipmentSlot、selectedSlot。目标格可空也可已有实体；这条分支本身没有仅空格限制。
6. 如果返回AL0，**015B0B04..015B0B51**扫描当前页空物理格；**015B0B68..015B0B70**用找到的空slot再调用相同 `maxPending1/op6/bypass0`。因此拒绝第一次occupied目标后，UI可能用空目标重试；插件的在途状态必须避免重复动作。

同一个drop函数确有 **015B09E7→017012D0**，但它来自 **015B06F9** 的context8/9/E等来源分支，经过各自源容器选择，不是上述context2装备拖动链。已证目标路径不需要新增另一个人物drop hook；已有016FA5F0入口hook可以截在构包及本地交换之前。不能将这一结论扩展为所有人物交互/快捷键/插件路径都已穷尽。

### 12.2 容器和装备身份辨认

- 本地Main容器指针是 **DWORD[03A5C9BC] / RVA0365C9BC**。native **01648AC0(0)** 在 **01648B8B**返回它；space29也返回同Main。UI+24在该背包页应等于此指针，以指针相等核实，不能仅“非book就是Main”。
- 容器kind getter **016F0590 / RVA012F0590**通过container+34/+38保护整数包装器返回kind。原型无显式参数、ECX=container、EAX=i32；使用工程fastcall桥即可。不要直接读+38当kind。识别当前Main并不需要此getter，精确全局指针更小、更确定。
- space3/17的装备不是一个可简单等同Main指针的物理容器。**01648BA0**明确把space3/17与普通容器分开，经actor的 **018364F0**取装备slot；普通space才经1648AC0/16EE6C0。特别是1648AC0本身对未特殊处理的space可能回Main，因此不能用 `1648AC0(3)` 推定装备容器。
- 对017012D0而言，`self==Main && index==13`指Main物理slot13，不代表角色装备13；按此判定转换cancel会误拦普通物品。当前针对明确book source/target的guard可保留，不要添加这类臆测。
- 是否mirror由同角色、已协商、服务器active category/index绑定决定；worn13.itemID等于binding.itemID最多是投影一致性检查，不能据此认领来源。普通真实称号即使与书原件同ID，服务器active=false时仍必须完整走native。

### 12.3 当前代码的最小修正建议（未修改生产代码）

send_item对Main分支应先限定 `wornSlot==13 && op==6`；再要求same actor的有效active binding、无投影请求在途。Main空目标可转为 `send_projected(binding.category,binding.index,cancel=1)`，直接返回，不进入native0013或本地搬移。不要将所有Main空槽的op0/op3等请求都转成cancel。

对于active mirror拖向occupied Main格，至少不得原样穿透让mirror进入背包。两种明确策略可选：只支持空格取消，occupied返回0让原生扫描空格重试；或全部op6视作镜像取消而不交换目标真实物品。后者改变occupied拖动交换预期，需任务产品行为明确。现有任务优先空格取消时，前者是更小的补丁。普通真实称号（绑定inactive）始终trampoline，不因op6而被拒绝。

此最小改动只证明“mirror人物栏拖至Main”路径。还存在另一个需要独立处理的替换边界：mirroractive时Main真实称号op0直接native，会在 **016FADFB→0257CD90**尝试归还旧title；canonical始终占槽使onlyEmpty查找不能命中，随后 **016FAF70..016FAF99**可递归Main op3/bypass1，将旧mirror交换到背包。不能仅修op6就宣称普通真实称号替换镜像也已安全。需服务器/插件明确取消mirror后普通equip的编排，或暂时拒绝该组合动作；不按ID猜来源。

新静态验证点：context2目标空/occupied及fallback空槽均得到op6；active绑定时不发0013、不显示Main mirror；inactive且同ID普通title保持原生0013；Main物理slot13普通移动不被转换；在途请求禁止重复取消；binding与0002/0166短暂未齐时不要将unknown当普通实例。实际拖动全路径仍需要本版本实机验收。
