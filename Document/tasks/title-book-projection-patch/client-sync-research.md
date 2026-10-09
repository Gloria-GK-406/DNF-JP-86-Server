# 称号簿静默快照与明确佩戴绑定：客户端同步入口研究

日期：2026-10-08。范围：授权实施前的只读研究；本次只新增本文，没有修改生产源码、客户端、数据库或部署。本报告中的 wire 与插件流程是拟议方案，不是已实现或已实机验证的行为。

## 结论与选择

建议采用 **NOTI 0x0166 标准分类列表之后追加固定 24B trailer，并 hook 原生 0166 handler，在原 handler 返回后提交绑定、重建 UI**。trailer 必须在整个标准载荷末尾，不能占用每条 entry 的未知 4B 扩展。服务端从一次 GetAsync 读取五个分类及同一佩戴绑定，明确当前佩戴原件的 `sourceCategory/sourceIndex/itemId`，以源位置区分重复 itemId；客户端不再用空书槽或 candidate `[-1]` 推导该绑定。

该入口的 handler 注册、cdecl 签名、正常明文入口、快照物化和刷新函数均有 A21 ClientStatic 证据。未 patch 客户端忽略尾部的结论为 **High**：handler 本身不读多余尾部，接收层逐帧重置读流；保护代码中的 cursor setter 以及通用 postcallback 尚未完整展开，仍需“带 trailer 的 0166 后连续收到正常帧”实机验证。不能写成 ClientVerified 或承诺所有客户端镜像兼容。

独立自定义 NOTI 暂不推荐。0100 没有可订阅网络服务，而 native 接收路径在查 NOTI 注册表之前用 type 直接索引固定数组；任意挑高位 unused opcode 不能保证未 patch 客户端安全。

## 证据身份

- ClientStatic：`E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，flat dump base `00400000`。下述地址均为该镜像 VA，RVA=VA−00400000。
- CurrentImplementation：`E:/Project/DNF/DNF-JP-86-ClientHack`，本次检查 HEAD `2aaddbf0a68ba7d586509342b54e273dfb3f127d`，检查时工作区无未提交修改；真实 ClientHack 源码在该独立仓库，非 Server 仓库中的旧目录。
- CurrentImplementation：Server `E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`。相关仓库没有 `.codegraph/`，本次用 rg、源文件与 dump 反汇编核对。
- 既有实包/现场观测仅用于说明需要解决的状态分裂；上游参考实现不能代替 A21 客户端裁判。

## 现有网络插件能力与限制

源码路径均相对 `E:/Project/DNF/DNF-JP-86-ClientHack/ClientHack/`：

| 位置 | 已确认内容 | 对方案的影响 |
|---|---|---|
| `include/clienthack/plugin_api.h:45`，`:61` | `ch_hook_request` 支持 RVA、expected bytes、detour、original；host 提供带字节验证的 `hook_create` | 新插件可独立 hook 0166 与 UI 刷新函数，使用宿主管理回滚 |
| `include/clienthack/plugin_api.h:62`、`:63` | generic `service_publish/service_query` | 只是服务注册能力；不代表存在 network listener |
| `plugins/0100_network_hook/network_hook.c:600`–`:618` | Decrypt passthrough 签名为 fastcall(self, edx, type, input, inputSize, output, outputSize)，先记录/抓包，再 memcpy | 回调在原生 handler 前；只有 type/body，不含 CMD/NOTI selector，不能只凭同编号识别协议族 |
| 同文件 `:46`–`:50`、`:525` | receive input 前方不是 15B frame header | 不应负偏移读取 header 来猜 selector、长度或序号 |
| 同文件 `:654`–`:657`、`:695` | ReadU8/U16/U32、ReadBytes、SetRemaining 已有诊断 hook | 读流 trace 不是可靠 handler-after 服务，不应依赖诊断开关实现产品功能 |
| 同文件 `:780`–`:785` | 0100 已 hook CipherDecrypt | 新插件重复 hook 同地址会产生宿主/MinHook 冲突；若选此入口须先扩展 0100 service，而不是另装同地址 hook |

全仓生产插件检索未找到网络 service 的发布/查询调用。因此“新插件直接订阅现有 NetworkHook 服务”当前不可用。选 0166 handler 入口不需要改变 0100。

## 0166 接收入口、签名与消费边界

### 正常入口与 hook 校验

NOTI 注册：`01188EED` push context=0，`01188EEF` push handler=`01181F70`，`01188EF4` push type=`0166`，`01188EFB` call Register=`0163C4A0`。

通用 NOTI 分发：`01637861` 取 context，`01637864` 取 handler；`01637866` push context，`01637867` push type，`01637868` call handler，`0163786A` add esp,8。原 handler `011822C8` 为裸 `ret`。故可以声明：

```c
typedef void (__cdecl *title_book_166_handler_fn)(int type, void *context);
static void __cdecl title_book_166_detour(int type, void *context);
```

`01181F70` 是普通明文 SEH 入口，不是保护跳板；RVA `00D81F70`，本 dump 前 16B：

```text
55 8B EC 6A FF 68 8B 57 D7 02 64 A1 00 00 00 00
```

宿主应按 A21 镜像/前缀校验安装 hook；不匹配则插件不安装。以上是静态签名证据，尚未做 MinHook 实装测试，不能仅凭前缀承诺 trampoline 已验证。

### 标准载荷消费与 owner 隔离

| VA | 消费/行为 |
|---|---|
| `01181FA7` | u8 InfoType |
| `01181FB3` | u16 owner；owner=0 选择本地分类容器数组 `03A5C8DC`，非零选择他人数组 `03A5C8C8` |
| `01181FD6` | i32 category |
| `0118206F` | 清空该分类实体容器（016F68D0） |
| `0118207B` | u32 count |
| `01182098`–`0118210C` | 每条标准物品字段；存在 `01182126 → 0274FBE0` 的保护扩展消费 |
| `011821AF`、`011821E1` | 创建物品实体、调用 016F2FB0 插入对应分类 |
| `011821F3`–`011822AD` | 仅 InfoType=1、owner!=0、category=4 的他人查看窗口分支；本地正常分支直接收尾 |
| `011822C8` | 返回；没有 02578240、没有获得提示队列 |

服务端现有 entry 编码为 26B，不能将其中最后 4B 擅自认定为“完全可复用 padding”。保护扩展的完整语义仍 Unknown。固定 trailer 从**帧 body 的最后 24B**定位，原 handler 消费标准前缀后再核对游标，而不让插件复制一套未知的原生 entry parser。

### 帧边界与未 patch 客户端

全局入站 cursor：`03BAAE38`，remaining：`03BAAE3C`。

- `0163BCF9` 读 totalLength，`0163BCFF` 减 15 得 bodyLength。
- `0163BD0C` 调保护函数 `05778441` 设置本帧流起点，`0163BD15 → 027C4350` 设置 bodyLength。
- 解密后 `0163BEAA` 再设流起点，`0163BEB2 → 027C4350` 再设本帧 remaining。
- `027C4356` 直接把参数写入 `03BAAE3C`；`027C5233` 读 cursor，`027C523E` 减 remaining，`027C5246` 写推进后的 cursor。
- `0163786F` 在 handler 后调用通用 `01437F10`；其完整剩余字节处理仍 Unknown。

0166 本身按 count 完成后返回，没有要求 remaining=0 的可见分支。下一帧重新设置 cursor/remaining 的调用结构支持末尾扩展可被未 patch 客户端跳过，但保护 setter 与 postcallback 尚未完全展开。**静态 High；没有带新 trailer 的 WireCaptured/ClientVerified。**

“线格式能容忍 trailer”与“原生 UI 语义不变”必须分开：原生 02578240 会把存在的书实体视为 state2；若服务端对未 patch 客户端也回填当前 worn source 原件，原生不会因 trailer 自动改为 state3。严格保持未 patch 行为需要 patched-client capability 分支：legacy 投影继续排除 worn source，patched 投影包含全部 canonical 原件与明确绑定。若当前部署所有客户端都强制装新插件，可作为部署前提，但不能把该前提写成 wire 本身保障。

## 已选定的 24B 扩展接口与原 handler 包装流程

父任务已选定以下六个 little-endian i32，按顺序追加在标准分类列表之后。本研究未实施该编码。

| 偏移 | 字段 | 值/校验 |
|---|---|---|
| +0 | magic | `0x314D4254`，字节 `54 42 4D 31`（TBM1） |
| +4 | version | 1；其他版本不激活 |
| +8 | sourceCategory | 佩戴来源分类 0..4；无 book 绑定用显式 -1 |
| +12 | sourceIndex | 来源 index；无绑定用 -1，不把 index0 视为无操作 |
| +16 | itemId | 明确绑定物品 ID；无绑定的约定值需与服务端一致，建议 -1 |
| +20 | trailerLength | 24 |

三项无绑定字段必须采用一致的 sentinel 组合，禁止 category=-1 但 index/itemId 仍有效的半绑定。对 book binding，按已有 PVF 行和书实体再次验证 cat/index/itemId；实际 worn 数组仍由原生装备消息维护，不能把 trailer 当作穿戴实体写入命令。appearance 不包含在该24B结构中，继续走实际外观协议。

这24B没有 characterId、revision 或批次末标记，因此不能声称支持跨切角重放检查、原子五帧提交或乱序新旧版本判定。当前合同必须由服务端一次 GetAsync 构造相同绑定的五类快照，并在有序连接上作为连续批次发送；客户端切角/断连主动清缓存。若将来允许五类快照交错或异步跨版本投影，需扩展版本，不能仅靠数字大小猜批次。

拟议 handler 包装顺序：

1. 进入 detour，从 `dnf_base + 0x037AAE38` 取本帧 bodyStart、从 `dnf_base + 0x037AAE3C` 取 signed remaining。context 只是注册时的 user data（此 handler 注册为0），不是 packet/context 对象，不能从 context 取包。至少 remaining>=11+24，且受正常 frame 上限约束，检查地址范围/加法溢出后，复制开头 InfoType/u16owner/i32category 和最后24B到本地结构。只接受末尾 length=24、开头 magic/version 匹配。**不修改全局 cursor、不提前改变模型。**
2. 调 original(type, context)，让 native 清容器并完整物化标准列表。
3. original 正常返回后，要求 `cursor == bodyStart + savedRemaining - 24` 且 `remaining == 24`，并保持预先复制的分类0..4、合法三字段绑定及相同本地生命周期；若不一致，仅记录失配并拒绝绑定提交。不要猜消费边界或继续读越界。global stream 是同步分发状态，重入时必须拒绝外层失配，而不是把内层帧当作同一帧。
4. 仅 `InfoType==0 && owner==0` 才提交绑定；InfoType1 自己或他人查询均不激活。本轮服务端 data binding 随 GetAsync 的登录自身五分类发送；query InfoType1 不编码扩展。**没有扩展不激活，也不能表示解除或覆盖已有有效绑定。**
5. 原 handler 已消耗标准前缀，插件可通过 bounded 本地副本完成语义消费。若需要把 native remaining 归零，可在完全校验之后以既有 ReadBytes 原语读恰好 trailerLength 到有界缓冲区（VA027C5380、cdecl(dest,length)）；不应直接无条件写全局 cursor。是否必须归零取决于 postcallback 后续验证，未证实前不把必要性写死。
6. 有效帧物化完成后，缓存已校验的三字段绑定，调用原生 02578240 重建基础状态，再对唯一 source 行施加明确佩戴覆盖。因 wire 无批次 commit 标志，此阶段是逐帧提交，五帧最后完成时才拥有完整最终实体快照；不能声称中间状态完全原子。可在服务端保证 cat0..4 连续顺序的前提下增加客户端到齐 mask 延迟最终整表刷新，但 mask 本身不是 revision，不能抵御不同批次交错。缓存不保存 native item 指针或 UI 行指针，只保存值，避免下一分类 clear 后悬空。

非法/缺失 trailer 时 native 标准处理仍执行，插件不沿用该帧伪造的字段。切角/退出必须清空绑定和可选分类 mask。插件失效时应回到 legacy 投影策略，不能用静默忽略 hook 失败但仍由服务端全量回填原件来声称 UI 兼容。

## 刷新时机与 UI 覆盖

刷新入口 `02578240`，RVA `02178240`，cdecl(category, flag)，前 16B：

```text
55 8B EC 81 EC 40 02 00 00 A1 E0 CE 99 03 33 C5
```

Native 019C ACK 中 `0111CF8B` push 1、`0111CF8D` push category、`0111CF8E` call 02578240、`0111CF93` add esp8，证明两栈参 cdecl；flag 的完整命名仍未确定，应先沿用该调用的 1 而不擅称外观开关。0166 本地列表物化返回后，实体容器更新已完成，故 detour-after 是“读新实体后重建”的明确边界。原生 ACK 在同接收链中调用该 UI 函数，支持在 handler 返回后使用同线程调用；该分发线程是否唯一 UI 主线程、是否允许任何登录阶段立即调用仍为实机验证项。

安全调用前至少检查：hook/signature 初始化成功、本地生命周期仍有效、category0..4、全局 title-book model `03A5CA1C` 非空、该类 PVF 行向量存在、sourceIndex 位于已有 vector 内、绑定 itemId 与对应 canonical 实体一致，以及重入 guard 未占用。02578240 自身未被证明可在任意生命周期阶段调用；缺 UI/model 时仅缓存，等待后续原生 rebuild 后补 overlay。调用使用 `void (__cdecl *)(int category, int flag)`，不得传 this/fastcall ECX。主动调用必须绕过自身 trampoline或使用统一 guard，避免无限递归。

状态覆盖可借用 `02576A70`，RVA `02176A70`，`void * (__cdecl *)(int category,int index,int state,int itemId)`，返回存在的 row 指针或 0；它有分类/PVF 向量存在与 index 上界检查。前16B：

```text
55 8B EC 51 8B 45 08 8B 0D 1C CA A5 03 53 57 50
```

该函数调用 `025767F0` 原生行状态 setter；`0257684E` 写 row+1C 状态，`02576857` 取 cache，`0257685F` 写 cache+20 itemId。因此可用明确 binding 设置该源行 state3、cache itemId，而不改 PVF candidate vector。需核对返回后实际 state/cache，不能把返回非空等同成功：setter 前面 `02576820 → 02573E30` 获取 UI asset，缺失时可跳过写状态。未创建 UI 时只缓存绑定，待原生 refresh/窗口创建后重试，不伪造 row。

为保证之后 ACK、普通刷新或开关窗口不再次把 state3 改成 state2，建议同时 hook 02578240：original 完成后应用该分类最新已提交绑定；0166 detour 中主动请求重建也经过同一覆盖规则，带重入保护。旧绑定行需要恢复本分类重建出的基础状态；重复 itemId 的非源行不得保留 native 推导出的 state3。固定成就 state1 的保留/恢复必须遵循原生状态规则，不能简单把所有非目标行写 0。此处是需实现及验证的 overlay 算法，现证据只证明可调用的 setter，不证明 duplicate-ID/click 完整行为已解决。

## 完整状态机与服务端合同

| 阶段 | 原生/服务端行为 | 插件行为 |
|---|---|---|
| 登录自身 | 一次 GetAsync 读取五类 canonical 原件及当前 worn 镜像 source；每个 InfoType0自身帧追加相同24B绑定 | 无扩展不激活；物化后验证边界再提交 |
| PUT/GET 请求 | native 可先做本地实体交换并 push pending | 不拦截请求、不自行再搬物品 |
| 成功 ACK | ACK 必须先完成 native pending pop/物理交换；empty pending 已确认不搬物品，旧“空 pending 二次反转”结论撤回 | 原生 ACK 正常执行；仅 UI overlay 不补造 pending |
| 装备状态同步 | 发实际装备全量状态/原生合法刷新组合 | 不用书快照替代 actor 穿戴数组或头顶外观同步 |
| 五分类 0166 | ACK 后静默物化全部原件（含 worn canonical source），连续五帧使用同一 GetAsync 绑定 | preparse最后24B→original→核对cursor/remaining→提交绑定 |
| rebuild | 明确 none 或唯一 source；五帧最终完成时实体完整 | native rebuild→apply binding，无 000E gain queue，无 0168 成就假事件；没有 wire 原子commit保证 |
| 后续 native UI refresh | ACK/开窗/其他原生路径调用 02578240 | original 后重复应用当前已提交绑定 |
| InfoType1查询 | 编码不带24B扩展；owner非0使用独立他人实体数组 | 即使碰巧末尾匹配magic也不激活，不改变本地绑定 |

Server 当前 `Server/src/Dfo.Server.Application/Achievements/TitleBookDomainService.cs:42`–`:48` 的 GetAsync 过滤 worn MirrorSourceUid；`:72`–`:77` 从 worn 镜像取得源 UID。拟议 GetAsync 应在同一读取上下文中返回**原件五类+明确绑定**，从 UID 定位 source 的真实 cat/index，不由 itemId 搜索首个同名称号。真实原件取到背包后 binding=none，book 中该源自然消失；mirror 删除/解除后 binding=none，canonical 原件继续存在。若读取与后续消息构造跨事务，须防止绑定和五分类来自不同版本。

不能再借助 000E 补整表：A21 gain queue 路径已经确认；也不能用 0168 假装刷新，0168 是成就状态处理，并不定义自由 candidate 或明确佩戴映射。

## 自定义 NOTI 备选的排除依据

Native 有 NOTI 注册 forwarder `01637720`，registry getter `0163C550`、lookup `0163C510`、register `0163C4A0`。handler 为上述 cdecl(type,context)。但是 receive 在 `0163C11B` 取 u16 type，`0163C11F` 无可见上界检查地读 `[type*4+03B51F20]`，随后 `0163C13D` 才进入 NOTI 分发。注册表本身可放某数字不代表接收层数组可安全索引。因此独立 opcode 必须另行证明数组合法范围、候选无占用以及无 patch 行为，或在原生分发前完整拦截消费。该方向目前比 0166 trailer 增加额外保护代码与兼容面，不作为本轮首选。

## 实施后必须验证的边界

1. 带 trailer 的五个 0166 与紧邻正常帧：patched 和 unpatched 各自不串帧、不越界，original 后消费边界匹配。
2. 登录、打开/关闭窗口、切角、查询自己、查询他人：本地 binding 不被他人快照覆盖，UI 未物化能在创建后补上覆盖。
3. 两个同 itemId 不同 cat/index 的原件：只有 source 勾选，换源正确；cache 正确不代表所有 click 请求选择已被证明，须检查实发请求。
4. 自由/固定称号佩戴、解除、连续换位、取到背包：ACK 不被重复处理，实体保留/移除与 binding 一致，无获得 spam；固定 state1 不被覆盖逻辑错误清零。
5. hook 安装失败、旧 trailer 版本、失配长度、切角后旧绑定：不得提交错误绑定；服务端 patched/legacy 投影政策与客户端能力一致。当前24B无revision，不可声称能识别同一角色不同旧批次重放。

本报告足以给出可实施入口与合同，但尚未证明新插件的完整状态机正确；尤其未 patch 尾部兼容、UI 生命周期、固定成就恢复与 duplicate-ID 点击消费，仍须实现后实机验证。
