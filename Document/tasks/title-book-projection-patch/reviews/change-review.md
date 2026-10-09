---
kind: rks-review
verdict: approved
---

# 称号簿镜像联动完整结果审查

日期：2026-10-08。独立审查者读取并采用 reviewing-work 的 Code 与 Deliverable 判据；审查 TaskContract revision 1、Spec、当前 Plan、三份客户端研究、双仓完整本任务改动及相关既有物品移动逻辑。未修改生产文件、客户端或数据库。未调度其他审查者。

## 审查目标与边界

- Server：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`，基线 HEAD `9da9b83ee2ad3f42502d575254d8a176e6bbfeac`，本任务工作区改动。
- ClientHack：`E:/Project/DNF/DNF-JP-86-ClientHack/.worktrees/title-book-projection`，基线 HEAD `2aaddbf0a68ba7d586509342b54e273dfb3f127d`，0200 插件、策略测试、CMake、对应 Document 文档。
- 初轮读取的 `title_book_mirror.c` SHA256：`E1B838168AEF0C57C5E4102B18292BEB80709D2CC7DBC23D145B0FED12B5BAFB`；最终批准的目标哈希见下方第二轮。
- `protocol/game/client-to-server/0x0010.md` 及深渊取证文档属于既有不相关修改，审查已识别并排除，不要求回滚。

以下确定缺陷来自 CurrentImplementation 与权威 A21 dump 的明文分支。构建和服务端测试不证明新插件已 ClientVerified。

## 第三轮增量复审：approved

本轮复审两个 test-only 清单补充、真实限时称号资格修正及部署脚本；没有修改生产文件或执行部署。

- `World/CommonExperienceEffectTests.cs` 仅增加新命令到 expected；readOnly 名单不变，反射得到的每个非只读 selected-session command 仍必须注册为 Transactional。SHA256=`9D863914E2975FC33CA24BFD6F817E17DED4AF02CFAD1C2AAA42A063C6DC5F65`。
- `Sessions/EffectSettlementRegistrationTests.cs` 仅在有序 protectedNonMain 名单加入新命令，没有把它改成 IRequestSettlementCommand，也没有弱化其它断言。SHA256=`9FD746E7064A1A3D01C9C74826D734CEA3B06E3409498CC6FF010F7AC358CD22`。Exclusive 注册由已审查的 ProtocolModule 生产接线保证，此处不把 Transactional 测试误称为另有 Exclusive 断言。
- 实现线程自查提出的限时称号问题确认为 RQ-004 缺陷：原先新替换路径继承 UsableAsync 的 `ExpireTime<=0`，会拒绝尚未到期的普通真实称号。当前新增 `requirePermanent=true` 默认，仅新原子真实替换入口传 false；原书原件入/出规则不变，非 pile、锁、title placement 与服务端穿戴许可仍检查。已追加 mode6，把真实称号设置为未来 UnixExpire 并断言原 UID / 完整 Snapshot 迁至装备13。该项源代码复审关闭。
- 第三轮批准的 `TitleBookDomainService.cs` SHA256 更新为 `C9191C5E83C28EE205B64AA59F5283CF2644577D0E0253091D2341DB01B74901`，替代第二轮该文件旧批准哈希。ClientHack 及 TitleBookSlice 批准哈希不变。

独立读取 Application TRX 结果为3294/3294、0失败、0跳过；finish=22:32:03，仅有六个替换 mode，属于限时 mode6 添加前的全量证据。限时新例的最终定向重跑和之后的 Protocol 重跑仍由控制器汇入完成验证，不能拿旧 TRX 当新例已通过。

部署脚本位于主仓 gitignored `E:/Project/DNF/DNF-JP-86-Server/artifacts/title-book-mirror-patch-20261008/`。只读检查 prepare、deploy、backup-audit 后，未发现确定阻塞安全缺陷：沿用已配置的绝对 JP86 DB/PVF 路径；SQLite backup API 以只读源生成新的完整备份；修改客户端仅0200 DLL；停止上游前核对 PID 与 exe，启动独立隐藏窗口的新 runtime，校验监听归属和 bootstrap/GM ready；失败路径停止自身新进程、恢复/移走0200并重启上游。没有 DB restore，也没有直接写上游 source/DB。脚本哈希如下：

- prepare.ps1：`1D50659AC3CC232904FF6B9E31D02B6931A28A1FC9C1D1DA16C447BF00261AAC`。
- deploy.ps1：`29A3B4098DA9ED26C0C2276B441F23BA7F5E5656A59EDC75A14885B0B3E9224C`。
- backup-audit.py：`B94F416ACE17C3F215E3ABAF80449475CD0E1840F28414E237206934CBDC3858`。

本审查者的 Get-CimInstance / Get-NetTCPConnection 现场只读调用被当前沙箱拒绝访问，所以此处批准脚本逻辑，不独立宣称现场 PID/监听已确认；部署前仍由控制器自己的新鲜现场证据执行身份门。尚未 publish/deploy 的声明来自控制器，不视为本审查已执行操作。

## 第二轮修正复审：approved

最终代码与当前 Plan、ClientHack 使用文档、协议 `0x019D` / `NOTI-0x0166` 扩展说明一致，未发现未解决的确定阻塞缺陷。此结论允许进入完成验证与已授权的测试部署，不表示已经部署、可正式上线或 ClientVerified。

批准源码 SHA256：

- ClientHack `title_book_mirror.c`：`B202EC0A3A04A3EA479AAA6DB768348CC93AC5460C1EFF1C4575CD635D3E83D6`。
- Server `TitleBookDomainService.cs`：`503FE6775D9E0DDA08A691CE383CA0E1528F4155B5F651135713012D8205095B`。
- Server `TitleBookSlice.cs`：`07FB131427574F6BFCBC8C817ECC5E08126D232A40CC7A11C1291B4A1EB2D389`。

两阶段取消续发已完全删除。Main 真实称号替换镜像改为本项目自定义 019D Space=TBM1 magic，Slot=Main 原生 game slot，ItemId=新真实称号，Category/Index=旧书来源。领域方法验证旧 MirrorSourceUid、Main 来源、实例是否真实、容量、锁和穿戴资格，然后在已注册的 Transactional/Exclusive 命令中完成镜像删除、真实实例迁移与事件；所有拒绝均在首次写入之前。成功/失败 ACK 都用原生 space3 和旧分类，成功 slot13/index旧来源，沿已审查的局部 consume 清除对应 book pending；不会把 magic 当成客户端原生容器。

客户端保留一个 projected 在途请求；Main 原子替换同时记录 real_slot，并在 move_item 拒绝该 Main 槽作为源或目的的物理移动，直到 ACK 完成。该变化只作用于本请求涉及的源槽，没有全局冻结正常背包移动。ACK 后直接释放 own busy，后续快照失配不会永久保留插件请求锁。

新增六模式领域回归覆盖成功、错误 itemId、错误书来源、装备锁、穿戴资格拒绝、事务回滚，并在 before 状态前设置独立外观 26628，使拒绝/回滚断言包含旧镜像外观。协议 handler 新用例覆盖 magic 分流、Main game9→business0，以及失败 ACK 的原生 space3。本审查独立读取 `verification/title-mirror-protocol.trx`：1397 executed / 1397 passed / 0 failed / 0 notExecuted。Application 最后外观补充后的最终重跑与 C 构建/CTest，由控制器继续汇入完成验证，不在本审查中虚构已执行结果。

下方保留初轮缺陷及补救证据，均由本节最终复审关闭；保留历史不意味着仍在使用两阶段代码。

## 第一轮阻塞：已在第二轮解决

### P1：两阶段真实称号替换先删除旧镜像，后续拒绝会改变失败前的装备和外观

影响 Spec 的状态不变量“失败不改变实体、绑定或外观”、RQ-004 普通真实称号生命周期，以及 TaskContract SC-4/SC-5。

当前 `title_book_mirror.c:206..218` 在 Main 非空真实称号替换书镜像时，仅确认物品 ID 为正，就先调用 `send_projected(...,cancel=1)`。服务端会提交镜像删除和外观清空。待 none 0166、worn 空和来源实例一致后，`:280..294` 才调用原生 sender 装备新真实称号；未对返回值 false 提供恢复，也没有第二阶段 0013 拒绝恢复路径。等待超时、来源改变而放弃续发，同样留下已取消状态。

原生 sender 在 `VA016FA8C1..016FA8D0` 调用 actor 虚表 `+744(newItem)` 做穿戴许可判定；后续 `016FAB06..016FAB19` 等路径可返回 AL0。服务端 `MoveItemSpaceSlice.cs` 的 `PutOnAsync` 在 `CanWearAsync`/位置资格拒绝之后才删除 displaced mirror；其现有单事务替换本来会保留失败前的旧镜像。插件的提前取消绕过了这一失败边界。一个新称号穿戴资格不满足，或者取消后来源槽被移走，即可造成“一次替换失败但原镜像已卸下”；这个结论不依赖实机是否出现背包闪现。

最低充分修正：真实新称号替换应以一个服务端事务作资格检查、删除旧镜像和装备新实例；客户端针对这个受限 0013 操作跳过本地镜像搬入背包，并仅消费自己的等待项。可沿 `016FB56D` 先行 helper CALL 及 CMD0013 ACK 局部 helper 继续最小取证，不需要改共享保护 helper 本体。若采用补偿方案，则必须覆盖原生 sender false、服务器 0013 拒绝、超时、来源失配，并恢复原书来源与独立外观，不能只复穿一个默认外观新镜像。仅补前置客户端资格门不足以覆盖服务器第二阶段拒绝。

初轮该项解决前没有批准实现审查门；第二轮已以单事务 TBM1 请求解决，见上方复审。

## 本轮发现后已复审关闭的问题

1. actor global 的 RVA 最初误写 `0333DFB8`。权威 dump 的 `0111CE67`、`016FA65F` 等明文访问 VA `03B3DFB8`，正确 RVA 为 `0373DFB8`；当前代码已经修正。
2. Main 真实称号完整委托原 sender 会走旧称号空书槽搜索和递归卸下，然后提前返回。`016FADFB→0257CD90` 配合 `0257CE56..71` 排除已物化原件，`016FAF70..99` 递归 op3 并返回；当前加入续发避免“只卸下、没有装备新称号”的直接遗漏，但两阶段失败问题仍见上方。
3. projected ACK 原先直到有效 0166 才释放 own busy；消费边界失配会永久阻止下一次称号操作。当前 ACK 原生处理返回后释放本请求，续发具有 5 秒期限，不再用未知投影长期占用 own busy。
4. 续发等待期间新书请求原先可抢入，旧 none 快照可能续发 Main 请求覆盖较新选择。当前称号入口统一检查 replacement 单飞，续发也要求无 projected request。
5. 缓存真实称号来源原先只比较 itemId，另一同 ID 实例可能被穿戴。当前保存原 item 指针仅作 identity 比较，不跨包解引用，并再次比对当前物品 ID；来源变更会放弃续发。

## 已确认的实现边界

- 服务端从 MirrorSourceUid 找到仍在称号簿中的 canonical 原件，输出 category/index/itemId；五分类来自一次 GetAsync 返回。相同 ID 不用于搜索首个来源。读取路径无写入。
- 所有书实体都保留投影；佩戴中原件取出、覆盖受领域拒绝。取消必须匹配实际原件 UID；同源重复佩戴保持镜像 UID 与外观。
- 专用书操作的无背包变化镜像删除事件不再重复投影；装备状态先于静默 0166。没有用全量 000E 书回填或假 0168 获得事件。真实解锁仍走真实 0168。
- 24B TBM1 使用六个 i32，服务端和策略头一致，无绑定为 -1/-1/0；InfoType1 不追加扩展，插件只接受 InfoType0/owner0。没有将原生每项未知 4B 当作自定义字段。
- x86 fastcall 空 EDX 桥的 ECX/原始栈参数数目与已研究 thiscall ret 清栈一致；rebuild/setter、0166、ACK 保留 cdecl。独立对照 dump 的 19 个函数签名均匹配；两处 ACK CALL 的地址与研究吻合。此项只证明静态目标签名，不证明运行中 MinHook 和保护 flush 正确。
- native setter 保留 cache 与引用操作，补静态资源后通过 native shared_ptr move 清除同步生成 ANI；未裸 memset shared_ptr，也未改 candidate vector。
- 书拖动 guard 依据来源位置；普通未绑定移动继续原函数。Shift 外观的 0239 路径保留。

## 尚待实机验证，不能写作确定缺陷或通过

- 26B 原生 entry 的保护尾部真实消费边界；TBM1 后紧邻其他帧不串帧；原 handler 返回时 cursor/remaining 恰好停在24B尾部。
- native ACK 在 consume 返回 true 后仍执行 actor `+74C/+750` 等尾部，这些虚方法的完整写入语义尚未证实；必须记录装备投影前后本地状态。
- 登录阶段主动重建与 UI 模型/资源生命周期、开关窗口、五分类逐帧到齐中间态、图片/动画和重复 ID 点击入口。
- 会话通过登录/退出/选角 StartPacket 重置，并检查 actor 指针；24B 没有 revision/characterId，不能据此声称同角色旧批次重放检测或五帧原子提交。
- 没有扩展的服务端保留原生动作；配套服务端对所有自身客户端默认返回全 canonical 原件，因此移走插件必须同时切回匹配的旧发布版本。仅关闭插件不是本任务的合格降级。
- 当前 protocol2 的 ReadTrace Enabled=0。若启用0100 ReadTrace，其 ReadBytes hook 会改变0200被调用原语的签名字节；应将该诊断配置兼容面记录到部署前提，不能认为所有配置均已测试。
- C 策略测试只覆盖 trailer 校验和精确来源 UI 策略，不覆盖实际 writer、pending、原子替换客户端分流和 native ACK 尾部。服务端新增原子失败/回滚回归，客户端状态仍须以实包/现场状态完成用户验收。

部署哈希、SQLite 备份、实际加载身份、监听/ready 及用户验收不在本轮已有证据内；由控制器继续完成。审查批准即使后续取得，也不代替完成验证。

## 启动故障修复增量审查

```yaml
kind: rks-review
verdict: approved
```

独立审查者`/root/title_mirror_result_review`，范围仅0200初始化诊断、Flush精确签名和验证工具。目标C SHA256=`8FA45EA161272B00F2C3F767D7ECA695F51E70A2B371F42C5717E9167E634AF8`，构建与部署DLL=`025BF6FEC0AEAAD3995C8EFD50A16F7EF24AEDAB5B466AF14F6182EE70A0ADB2`。

独立验证20个dump+1个live目标通过；缺少live JSON会明确失败。live JSON的19个函数位点中只有Flush旧签名失配；反汇编实际64B确认原生stack-probe/cookie及原writer访问。修订未跳过校验、改变callee调用或业务语义。corrected-startup.log证明0200、全部插件初始化和线程放行；C策略测试通过。无需服务端改动，无阻塞问题。

非阻塞文档发现：不能声称所有init失败都有RVA日志，两处ACK CALL签名失配仍直接拒绝。已缩窄ClientHack说明文档措辞。该批准只覆盖启动修复，游戏内称号交互依旧pending。
