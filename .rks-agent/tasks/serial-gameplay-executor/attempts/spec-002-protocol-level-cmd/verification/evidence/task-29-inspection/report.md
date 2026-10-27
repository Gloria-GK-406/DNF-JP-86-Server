# TASK-29 r2 有界实现验证报告

本轮完成 Cera 唯一整协议 CMD；8 个精确源码路径＋2 canonical 内实施，旧 fine／注册／caller 删除，消息／codec／通知／core／Support 全程原 bytes。Task Result 写在同一精确 r2 prefix 后；required independent review 由主控另派。没有 stage、commit、远端、发布、清理或 TASK-30。

实现以一次 Ordinary WriteResultAsync／NonRequest／一个精确 buyer physical actor 和显式 metadata 直接驱动原 domain。正常 Refused 仍 Committed，普通 clock/domain cause 要求回滚；仅 runner 确认的 KnownStorage commit 结果恢复原拒绝。C1／C2／真实 whole scope C3 默认失败保护，C3 保已登记成功帧后实际关闭来源。取消／typed／已有 sticky 独立故障均保持失败，不吞作 StorageFailure。

- 原 App focused110、codec7、Host真竞争1先跑；迁后App125／Host1通过，原319个 Assert语句与全部原测试方法／参数保留。Buy/Command是领域强测试接缝；Handle／新Flow和probe才证明真实decoder／dispatcher／whole／footprint。NameTag setup单独同provider job。
- after02十二模式原生探针通过，source及加载assembly身份见loaded-assemblies.json。真实action19/1811和nativeCommit19/787、KnownStorage FallibleCommit同cause、完整所有列回滚和实际原ACK、去trigger新购买均成立。C1/C2/C3真实资产/typed cause/单actor保护/physical sourceclose/CloseCauses与SourceChain精确匹配，替代session/runtime/其它physical保留。E使用原 production outbox factory且causes同provider同对象，无手工Record或提前flush。
- 七旧新case完整各phase与retry schema／列／NULL／BLOB对照闭合；同run全表原样回滚。仅4个具名SQL CURRENT_TIMESTAMP列、r2唯一housing(version1).applied_at初始化UTCms列及每case/GUIDkind非零双射可跨run等价。原值／各自UTC界／rowidentity／时序／deliveryFK／batch／origin保留在cross-run-equivalence与timestamp-order-checks。原success／refusal／A／B真实帧逐bytes一致。VC-B2日志身份和VC-B4 C组差异单列。
- EDI变异只改消费者CommitRolledBack分支，可编译，仍native787／原Commit栈／同cause／ConfirmedRollback／完整资产回滚，却不返回原ACK并actualClose1，独立oracle exit1。原源exact恢复后重建A/B/正常4模式绿，见mutation目录。编译或fixture错误不算RED。
- Release全solution --no-incremental 0/0；四fresh full：App5252（5234pass+2原fail+16skip）、Protocol1778pass、Networking199pass、Host203pass。其它四Task27完整full有项目依赖与nonowned source等价证明：Http57、Infra976（974+2skip）、Persistence484、Pvf80。完整9014→9029仅新增15个本Flow，无删除／改名／旧状态漂移；未新增socket失败。单CMD2秒不从套件耗时推出。
- DFO final bundle逐bytes取本四fresh与Task27其余三实际control完整JSONL，原CLI expected九类67反例+7control=74、outside/exe/orphan全0。Golden15 current=HEAD=f01dc226；10 Characterization全pass/0skip。
- 真实capture／纯family总数173/201，唯一65600由LegacyQueued转ProtocolCommand、旧fine换whole，bindings/metadata/source注册其余逐项同前；36P/1I/164Legacy，136Tx/52Direct/9RO/4WF，47P消息/126Legacy消息，telemetry14，原三exit种类及所有owner/phase完整相同，Cera无新raw。strict及原8selftest全部通过，仅canonical两实际ID闭合。
- 完整tracked NUL15284：原existing owned8与nonowned15276互斥且并集；nonowned currentSHA=before=HEAD，新2source与delete fine独立，原LF/BOM保持。最终Git changed/untracked集合与manifest及authored whitespace相合。

正式native实际freeze01。全量结束后，主控允许仅两canonical证据目录locator改具体文件并同步MD，freeze02其余30项完整相同；不冒改后重跑full。该治理差异、原preclose01拒绝、全部import/fixture/checker诊断保留。Windows csproj反斜线解析的行政checker纠正另存r2，不改原frozen driver或raw。

局限：probe用受控内存transport和原OutboundSender，未做TCP/A21实机；close cause真实登记但未模拟TCP disconnected回调，通用生命周期继承由原full tests覆盖。受控fault不冒生产事故，全表观察可提前enlist同一个既有SQLite participant。NonEffect重复/重试是新购买。Astra／用户明确分支收尾／最终8fresh／单CMD≤2秒和Spec002终局仍为后继。
