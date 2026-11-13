---
kind: rks-review
target: git diff 17eb734d4..eac3d585abb158c91dae3e89660f741caa566ab6
scope: complete mixed result (code + protocol docs), automated acceptance
verdict: approved
---

# 完整改动审查：冒险团按账号派生、通关经验加成与升级推送

## 审查对象与依据

- 目标：`17eb734d4..eac3d585a`。包括 `4d761b252` planning、`ea7c76c82` implementation、`eac3d585a` 换行修复，共 78 个文件。
- 管辖文件：`TaskContract.md`（revision 5）、`spec.md`、`plan.md`。另读了仓库 `AGENTS.md`、`Document/Architecture.md` 中规则服务与领域服务一节、`Document/DatabaseSchemaMigration.md`、`protocol/evidence.md` 和工作区 `AGENTS.md`。
- 套用的审查标准：reviewing-work 的 `references/code.md` 与 `references/deliverable.md`，本次是代码加协议资料的混合交付。
- 范围：RQ-001..RQ-011 的自动化部分。AC-003、AC-005、AC-006 的实机部分和 AC-009（T-008 用户测试）尚未进行，不在本次范围。

## 决定性证据

**构建与测试（本次新跑）**
- `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
- 设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 后，按过滤条件运行以下测试，全部通过，0 跳过：AdventureGroup*、PvfCharacterManageContentSource、ExperienceBonus/Calculation、DungeonClearCodec、SchemaVersionTests、SelectCharacterAcceptedTranscript、SelectCharacterPayloadCodec、ContentWarmup、MonsterDeathCodec、OtherCharacterDetailPayload、PartyTower 特征测试。各程序集结果：DbMigrate 7、Infrastructure 5（含真实 PVF 用例，未跳过）、Host 7、Protocol 101、Application 61、Networking 5。
- 没有跑全量套件。全量结果沿用调用方的说明。

**70 派生规则**（`Server/src/Dfo.Server.Application/Characters/AdventureGroupServices.cs`）
- 角色点数：按 band 累加到角色等级，`top = min(band.Max, L)`。
- 账号点数：先求和，再封顶到 `LevelThresholds[MaxLevel-1]`，A21 为 175350（第 17 行）。
- 冒险团等级：满足阈值的最高序号，最大为 `MaxLevel`。
- 经验加成率：`G==0 || L_c >= Lmax` 时为 0，同级也算最高（第 37 行）。
- 测试 `AdventureGroupTests` 覆盖 AC-001 的全部边界值：P(39/40/50/70/86)、0/1374/1375/10849/10850/21570/21699/21700、175349/175350、封顶为 10、E[1..10]=10%。
- 真实 PVF 测试断言 61 个逐级 band、阈值、max 和 exp bonus，金币表为空。合成档逐 token 复现了两处格式缺陷。

**单一 owner，不持久化**
- `IAdventureGroupRuleService` 只被 `AdventureGroupDomainService` 注入；`ICharacterManageContentSource` 只被规则服务注入。
- 发包层、经验管线、`0x01BA` 命令都只调用领域服务。
- 代码中没有任何冒险团值的写入。领域服务只读 `ICharacterTable.ReadByAccountAsync` 和 `FindOwnerAsync`。
- 符合 `Architecture.md` 的要求：内容源只被规则服务注入，规则服务不查世界状态。

**发包偏移**
- USERINFO1 尾部：写 u8 G 和 i32 S（`StateSynchronizationPayloadCodec.Entry.cs:829-830`）。
- subtype3 `+3`：写 u16 G，取目标会话账号的值。链路为 `OtherCharacterDetailQueryDomainService`（target 的 current view）→ `LoadAsync(target.AccountId)`。
- `0x01BA`：两条路径共用 `MercenaryInfoResponseEncoder.Roster`，测试断言 `01 03 42 54 00 00 00`。
- subtype2：`CharacterStateProjection.BuildRosterEntryAsync` 固定写 0。
- 特征 golden 共 10 个文件，逐字符比对后，差异只在 USERINFO1 尾部的 level 和 point 5 字节，以及 `0x01BA` body 的 level 和 point 5 字节（`015F0500` 即 G=1、S=1375；`014E0C00` 即 G=1、S=3150）。没有其他字节变化。

**通关经验**
- 通关类加成只在 `ExperienceEvent.DungeonClear` 时收集，冒险团项排在最后（`ExperienceBonusDomainService.cs`）。
- 普通通关只有一个入口 `DungeonClearSettlementDomainService`，它用 `ClearExperienceContext` 计算，金额为 floor(Base × rate)，不含评价加成。
- 领主之塔以 `Event = DungeonClear` 收集加成，沿用 110 万剩余额度逐项扣减，所以冒险团项最先被截短。测试覆盖未触顶、截短、归零三种情况。
- 迷妄之塔/死亡之塔和击杀不收集冒险团加成，有测试覆盖。
- `0x0023` 第 7 格（body+45）写入 `experience.AdventureGroup`，测试同时断言其余字节不变。

**推送**
- 订阅为 LiveOnly，不设合并键，有注册测试断言。
- `DomainEventConsumptionCommands.LiveOnlyAsync` 先提交消费再调用，并跳过跨进程和超过 30 秒的事件，"每次等级变化最多推一次"成立。
- 订阅者要求 b > a，且 `Find(account)` 找到的会话处于世界中。通知处理器再次核对接收者和 IsInWorld，然后发出 b−a 帧空 body。
- `LevelRiseAsync` 在等级下降、角色已删除或 G 不变时返回 null，测试覆盖跨一级、跨多级、不跨级、下降和已删除。

**schema v5**
- `Version = 5`，DDL 去掉该列，读取链整条删除。
- DbMigrate 新增第 4 步，先检查列是否存在再执行 DROP COLUMN。新测试覆盖数据保留，以及迁移后与新建库的结构一致。
- 在 `Server/src` 和 `Server/tests` 中大小写敏感搜索 `manage_level`：0 命中。命中只在 `Tool/DbMigrate`（迁移步骤、`LegacyUpgrade`）和 `Tool/DbMigrate.Tests` 的 v4 夹具，都在契约允许的范围内。
- 迁移文档已有 v4→v5 段落。

**测试改动是否削弱了断言**
- 转录测试对 `0x01BA` 由比较整帧哈希改为断言 body 字节。帧类型由判定条件锁定，长度由 7 字节 body 锁定，具体值另由 golden 独立钉住，没有实质削弱。
- GM host 测试给种子数据补了互不相同的 `slot_index`。原因是 `ReadByAccountAsync` 本来就拒绝重复槽位，而真实入场路径（`AccountEntryAssembly`）早已经过这个查询，所以这是修正夹具，不是放宽断言。
- `ShopFixtureContent` 补上内容源，`ContentWarmupTests` 名单增加 `character-manage`，都属于必要的接线。
- 其余删改都对应被删除的列，没有放宽断言含义。
- 换行修复提交在 `--ignore-cr-at-eol` 下没有任何差异；相对基线也没有整文件换行变动。

**协议资料**
- 四个协议页和 `catalog.md` 的新增段落使用 CurrentImplementation、70Reference、ClientStatic、上游转录、Hypothesis/High 等标签，都符合 `evidence.md` 的分类。
- 没有标 ClientVerified 的内容。
- 空 body 和升级券双加级两项都明确写为待实机。

## 发现

无阻断性发现。

## 非阻断的残余风险与建议

1. **DP-4 的影响面写窄了，属文档缺口。**
   - **现状**：Plan DP-4 写「唯一在同一命令内发出带新 G 的 USERINFO1 的是升级券」（`plan.md:47`），NOTI-0x0002、NOTI-0x01A0 和 catalog 也只提升级券。实际上，`GameFinishQuestCommand` 在任务改变成长类型（转职/觉醒）或打开 649/650 槽位时，同一命令里也会：
     - 先结算任务经验（`GameFinishQuestCommand.cs:206`，帧中带 `NewLevel`）；
     - 再重读当前视图（第 128 行）；
     - 然后发出带新 G 的 USERINFO1（第 232、288 行）。
     
     这类任务的经验如果恰好让账号越过冒险团阈值，就和升级券是同一个"先 USERINFO1、后 `0x01A0`"的组合。
   - **后果**：不影响本次自动化验收，也不会让计划中的用户测试失效，因为测试的是同一个客户端机制。但如果实机证实升级券会多加一级，按现有文档去修，可能漏掉任务路径。
   - **建议**：在两份协议页的「未证实」和用户测试的未覆盖项里补上这条路径，留给届时的裁定一并处理。DP-4 本身已有充分记载：Plan、NOTI-0x0002、NOTI-0x01A0 和 catalog 四处都写明了升级券风险待实机，也说明了出现时交回用户裁定。
2. **并发时可能多推。**
   - **原因**：`LevelRiseAsync` 计算时，本角色取事件的 before/after，其他角色取当前等级（`AdventureGroupServices.cs:90-91`）。同一账号的两个不同角色（例如 GM 改离线角色，同时在线角色升级）的事件如果都在两次写入提交后才处理，两边各算一次，就可能多推（例如两边各贡献半个阈值时推 2 帧，实际只升 1 级）。
   - **为什么只记为残余风险**：每个事件作业都紧跟在发布它的命令之后执行，正常顺序下不会出现，触发窗口极小。
   - **建议**：接口注释「consecutive changes add up」应理解为"在依次处理时成立"。
3. **subtype2 入场路径的测试断言的是模型字段，不是字节。**
   - `AdventureGroupWireTests.Entry_roster_header_leaves_the_adventure_group_out` 断言 `ManageLevel/AdventurePoint == 0`，没有直接断言 +5、+6 两个字节。
   - 字段到字节的编码未改动，选人界面路径也本来就写 0，实际风险很低。
4. **待用户测试的部分。** 以下属于 T-008，不是缺陷：
   - `Document/tasks/adventure-group/` 尚不存在，协议页已提前引用该路径；
   - `0x01A0` 空 body、客户端是否按绝对值写入 USERINFO1，仍是 Hypothesis 或 70Reference，由用户测试证实或推翻。
   
   本次审查没有发现会让计划中的用户测试无法进行或产生误导的问题。例如服务端帧日志能记录零长度帧（`evidence.md`），所以 SC-5 的"帧日志显示 b−a 帧"可以取证。
5. **真实 PVF 用例在没有 PVF 时会跳过。** 届时只剩合成档覆盖。本次已在有 PVF 的环境下实跑，0 跳过。

## 复核 18d4f09c5

- **改动范围**：`git show 18d4f09c5` 只改了 `protocol/catalog.md`、`NOTI-0x0002.md`、`NOTI-0x01A0.md` 各一行，均为 3+/3−，没有换行变动，也没有改代码或测试。
- **内容核对**：三处都把完成任务路径写进了"是否多加一级待实机"的说明。NOTI-0x0002 写明了路径的具体内容：`GameFinishQuestCommand` 在改变成长类型或开槽时，任务经验先入账，然后在同一命令里先发带新等级的子类型 1，再发 `0x01A0`；并写明结论对升级券和完成任务两条路径同样适用。仍标为待实机，没有新增 ClientVerified，证据标签不变。非阻断建议 1 已处理。
- **小瑕疵**：catalog 写的是"转职类完成任务"，没有覆盖开槽（649/650）这一情形。两份协议页写得完整，所以不影响结论，后续顺手改成"改变成长类型或开槽的完成任务"即可。
- **结论**：本次修正不改变上文结论，目标更新为 `17eb734d4..18d4f09c5`，verdict 仍为 **approved**。

## 复核：revision 7 增量

### 新目标与标准

**审查目标：**
- 服务端仓：`17eb734d4..5c6e66651`。增量部分为 `18d4f09c5..5c6e66651`，即以下四个提交：
  - `6de6ad892`：契约第 6 版；
  - `34f7a5b6a`：`0x01BA` 改按已登录账号回答；
  - `95adbf9d4`：契约第 7 版；
  - `5c6e66651`：客户端静态结论、实机记录、`re-login-banner/` 反汇编摘录。
  
  审查开始时文档还是未提交的改动，复核期间已提交为 `5c6e66651`。工作区除本审查文件外是干净的。
- ClientHack 仓：`E:\Project\DNF\DNF-JP-86-ClientHack-adventure` 分支 `task/adventure-login-banner` 的提交 `b41b33d`。

**依据：**
- 契约 revision 7，及其独立评审 `task-contract-review-r6.md`、`-r7.md`；
- Spec amendment 中的 RQ-006 修订、RQ-012、AC-005 修订和 AC-011；
- Plan amendment 中的 DP-8 和 T-009。

**审查标准**：同上文，code 与 deliverable 两份 reference。ClientHack 提交按 code 标准审。

### 新鲜证据

**服务端**
- 当前 HEAD 构建：0 错误。
- `LightweightProtocolCommandFlow` 与 AdventureGroup 相关的 Application 测试共 82 项，全部通过。
- Protocol 的 AdventureGroup 测试 10 项，全部通过。
- 没有重跑全量套件，带/不带 PVF 的全量结果沿用调用方的说明。

**ClientHack**
- 在已有构建目录 `out/win32-release` 中运行 `ctest -C Release -R adventure_levelup`：1/1 通过。全量 17/17 沿用调用方的说明。
- 部署检查：`DNFClient-A21-protocol` 与 `DNFClient-A21-protocol2` 的 `plugins/0210_AdventureLevelUpFix.dll`，SHA-256 都是 `98cd30e7…4e36`，与 `stage-a21` 构建产物一致。`DNFClient`、`DNFClient-OneKey`、`DNFClient-A21-debug` 都没有这个文件，部署范围符合授权。

### 判断

**服务端 `34f7a5b6a`**
- 对 `IOnlineUserQueryDomainService.TryGetByConnection` 返回的账号调用领域服务 `ReadAsync`，未登录的连接写 0。
- 单一 owner 和分层都不变：命令处理器依赖领域查询服务，这是允许的；回答的始终是本连接自己的账号，不会泄露别人的数据。
- 入场批次的 `0x01BA` 仍取已选角色 Extras 里的值，两条路径编码相同。
- 测试改为断言字节：已登录时为 `01 03 42 54 00 00 00`，未登录时为 0；调用痕迹改为走 `online.TryGetByConnection`。断言没有削弱。
- 满足 RQ-006 修订与 AC-005 修订的自动化部分。

**ClientHack `b41b33d`（RQ-012 / AC-011 的自动化部分）**
- 钩子与原函数的调用约定一致：thiscall、无栈参数，用 fastcall 模拟。
- 首次绘制时，只有 manager+8 的实体存在且实体 +0x60 的动画存在，才给 `[anim+0x44C]` 置 bit3，并且只置一次；然后照常调用原函数。之后不再干预，0x01A0 的重播路径不受影响。
- 安装前校验四段原字节，任一不符就拒绝安装。插件 FLAGS 为 0，即非必需插件，安装失败也不会拖垮客户端。
- 直接置位与 SetEnd（`02248CF0`）的效果等价，校验的签名正好证明了这一点。
- 测试覆盖：
  - 镜像不符时拒绝安装；
  - 缺实体或缺动画时不动作；
  - 首次置位且保留其他位；
  - 原函数照常调用；
  - 清位重播之后不再干预。
- 地址来源写进了 `address-provenance.md`，含 EXE 的 SHA-256、各段 RVA 和校验字节。
- `EXCLUDE_FROM_ALL` 加手动部署的做法有 0150 先例。

**实机记录**（`Document/tasks/adventure-group/用户测试流程.md`）
- 用户明确确认了三项：选人界面右侧和 buff 都显示、首次进城不再有动画、升级券路径升级时出现升级提示。最后一项的帧日志显示恰好发出 1 帧 `0x01A0`，其后 `0x01BA` 应答为 6 级，没有多加一级。
- 步骤 1–4、6、6a、7 记为「用户没有逐项报告」，记录如实写明。

**结论：无阻断性发现，verdict 仍为 approved。**

### 非阻断问题

1. **NOTI-0x01A0 页的实现状态写过时了。**
   - 位置：`protocol/game/server-to-client/NOTI-0x01A0.md:102`，原文是「这超出服务端仓，需另立 ClientHack 任务」。
   - 这句与契约第 7 版不符（用户已选「本任务里顺带改 ClientHack」），而且全页没有提到已经实现的 `0210_AdventureLevelUpFix`。
   - 建议改写为：已由 ClientHack `0210_AdventureLevelUpFix` 在首次绘制时置结束位；用户实测首次进城不再有动画；未装补丁的客户端仍会播放一次。
   - 同类小问题：ClientHack 仓 `address-provenance.md` 中的「实机效果待确认」，现在可以引用用户实测的结论。
2. **以下验收项缺用户逐项反馈，verification 阶段不应写成实机通过。**
   - AC-003：结算面板「冒险团奖励」一行；
   - AC-005：四维没有计入两次、第二客户端查看他人；
   - AC-011：连续两次正常退出重登都没有升级提示。
   
   用户的整体结论「没问题了」能否覆盖这些项，属于 verification 或用户裁定的问题，不是本审查能判断的。实机记录已如实写明这一点。
3. **同一页早先「未证实」段落的状态。** NOTI-0x01A0 与 0x01BA 页里，早先写的「升级券是否多加一级」「Hypothesis，待实机」由同日新增的一节取代，按协议页按日期追加的惯例可以接受。如果想减少误读，可以在旧段落末尾加一句"见 2026-11-13 新节"。
