# 实现记录

## T-005 交付验证，2026-09-09 17:30

补充真实PVF到生产商城规则/SQLite的两项联通测试：110000→3×1/3600，及120000时装、105016宠物蛋、101075宠物装备、102425纯实体礼包组合。两项通过；组合余额73020、时装7天期限、宠物起始等级1及全部组件均从生产内容源经购买命令落库后重读，未用伪造目录替代。证据 `t005-archive/`。

完整Release构建exit0、0警告/0错误；同工作树执行 `dotnet test Server/86JP.Server.slnx -c Release --no-build --logger trx --results-directory .rks-agent/tasks/cera-shop-purchase/verification/t005-full --verbosity quiet`，PVF为当前A21档案，exit0。七份TRX逐条UnitTestResult统计2653 Passed /0 Failed /2 NotExecuted；项目和文件SHA256清单见 `t005-full/results-summary.json`。2个NotExecuted是既有缺档案用例，在PVF可用环境条件跳过；清空子进程环境PVF后单跑 `--filter FullyQualifiedName~A_missing_archive` 得2 Passed /0 skipped，见 `t005-no-archive/`。

协议人工区补充直接指令布局、23字节失败与24字节成功，保留机器区原文并明确纠正旧参照解析；目录阶段升到实现服务端功能，不标ClientVerified。用户说明位于 `Document/CeraShop-PhysicalPurchase-20260909.md`。主线HEAD仍为b54978c08c63a7a6fd0351c9b4abd93cd215afbe、工作区干净；运行中Host18116启动时间仍16:01:33，未切换或改用户库。独立review阶段从此完整差异开始，结果另记reviews/change-review.md。

执行控制：按 plan 的 T-001 至 T-005 顺序管理本轮阶段，阶段输出写入本记录，不建立独立 Task Unit registry。Standard 主代理保留全局决策，T-001 内容解析长循环委派一个有界实现阶段；共享工作树写入串行。已有并行工作仅为只读取证。完整差异在最终 review 阶段独立审查。

当前阶段：T-001 内容定义与解析。测试保护真实报价和权益分类，避免负价时装免费、礼包空发等静默错误；断言返回的商品内容和费用，不断言内部依赖结构。后续交易测试保护独立连接可重读的全部支付与实例结果。

## T-001 — 内容声明与 typed PVF 解析（2026-09-09，执行轮1）

- **Changed:** 新增 `ICeraShopDefinitionSource`，Data.Content 不可变商品/物品/价格/礼包选择/时装能力/宠物species声明，`PvfCeraShopDefinitionSource` 与Infrastructure单例注册，内容测试及 `protocol/patterns/data/traces/a21-cera-shop-content-20260909.md`。两个API分别按商品号与物品ID查询；未知返回null，未兑现/格式不可靠声明附诊断。avatar价格为所选档位的最终价格，Count=1；期限保留非负声明供后续规则明确兑现。
- **Why:** 数字必须是Int32而不能把string offset误读为费用。礼包目录0数量与负价时装均不能按一般标量处理；权益联合目录/专用文件/有出处路径识别，不能用[etc]标签一刀切。宠物蛋用明确species解析start level，保持蛋模板。时装能力支持档案已证实的index/stat/operator/amount四元组，未支持选项结构明确诊断。
- **Checks / test-value gate:** 测试保护静态源向消费者返回的费用、奖励组件、合法选项和服务分类；这些错误会在后续购买造成免费发货/漏发/错发，既有内容测试不读取商城。判据比较输出事实而非内部依赖，重构解析结构不影响断言。先建立可编译null返回壳，再运行首个真实110000测试，预期行为缺失导致失败；其它样本与synthetic异常判据为实现后的补充验证，不冒称逐项TDD。
- **RED:** 环境 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；命令 `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --filter FullyQualifiedName~PvfCeraShopDefinitionSourceTests --logger 'trx;LogFileName=cera-content-red.trx' --results-directory .rks-agent/tasks/cera-shop-purchase/verification`。exit1，编译成功、真实PVF已找到，1失败/0通过/0跳过；decisive `Assert.NotNull() Failure: Value is null`，测试第15行。
- **Initial GREEN:** 同命令将LogFileName改为 `cera-content-green-initial.trx`；exit0，1通过/0失败/0跳过，真实110000→3×1/3600。
- **Final GREEN:** 同环境，命令 `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --logger 'trx;LogFileName=cera-content-final.trx' --results-directory .rks-agent/tasks/cera-shop-purchase/verification`，exit0。逐条读取TRX UnitTestResult：276 Passed / 2 NotExecuted / 0 Failed；新增商城16条全部通过。两个跳过是既有“missing archive”场景在真实PVF可用时的条件跳过。`git diff --check`通过。
- **Owned diff:** 生产新文件3个（Abstractions接口、Data定义、Infrastructure源），既有InfrastructureModule仅新增1条注册；测试新文件1个、协议取证新文件1个、本执行记录与TRX。没有实例存储/领域交易/Protocol代码或运行数据库写入，没有提交。交付后控制器负责全差异独立review及后续T-002—T-005。
- **Remaining:** T-001无阻塞。T-003必须检查product/item.Diagnostics、递归验证全部礼包组件及选择，拒绝Service和未知项，落实期限日期边界与商城能力索引。非默认package条件、目录限购、未知支付与未解释时装socket/flags目前显式拒绝；不宣称实机购买验证。

### T-001 执行轮1补充：实例声明边界

控制器最后指出物品自身的expiration/trade声明尚不由新实例DTO承载，已增加top-level `[expiration date]` / `[usable period]` / `[trade limit]` 非默认值的 `UnsupportedInstanceDeclaration` 诊断；booster内部仍不参与。此处不同于已保留的商品档位/礼包组件期限。

补充检查命令与初始focused命令相同，LogFileName=`cera-content-instance-boundary.trx`；exit0，17通过/0失败/0跳过，包含新增synthetic实例声明诊断和全部真实商城样本。上一轮完整Infrastructure结果276通过/2条件跳过仍保留；本次只改上述分支并做聚焦回归。写入已释放给控制器。

## T-002 至 T-004，执行轮1

T-002新增创建事实入口、宠物八字段原子创建、商城三币条件更新和入场Pet容器绑定。宠物Core.Value由结算层设置为新分配world identity，确保NOTI000E与0069关联；普通移动继续保持该行。真正RED：新宠物提交后列表为空（t002-red首TRX，Assert.Single空）；三币CAS编译壳返回0使第二RED失败。GREEN三项全部通过；Persistence完整回归336通过。详情以各目录TRX为准。

T-003规则先展平整个购物车，再计算支付与全部槽位；领域购买沿用账号/在线用户双独占与同一数据库事务。直接时装期限按天生成，礼包组件期限传播，交叠期限取较早；不支持限时宠物及物品自身特殊过期/交易声明。宠物饱食度100/其余默认参考旧服务端，等级来自PVF。真正RED：给定3600报价和100000余额，计划缺失（t003-red，Assert.NotNull null）；GREEN水购买96400与一个3。规则14通过、真实SQLite交易10通过，涵盖并发、所有实体类型、归属与回滚。

T-004新增0040切片，严格游标解码，任意截断拒绝整车；成功24字节、失败1/4/11为23字节。共享钱包/宠物编码上提endpoint，原入场codec委托同一实现。真正RED：真实19字节请求未注册0040无法解码；GREEN注册与字段一致。额外codec7通过，Application商城28通过（含提交先于发送、发送失败不撤销、旧会话抑制），真实TCP拒绝和后续退出1通过。CeraShopPurchaseCommand提前纳入T-003交易测试，余切片在T-004补齐。

验证命令：dotnet test Server/tests/<项目>/<项目>.csproj -c Release --filter FullyQualifiedName~<场景> --logger trx --results-directory .rks-agent/tasks/cera-shop-purchase/verification/<目录> --verbosity quiet；目录t002-red/green/regression、t003-red/green/rules/transactions、t004-red/green/codecs/application/tcp。完整Persistence以--no-build不加filter运行。所有源码未提交、运行库与用户Host未改动。后续仍需真实PVF端到端样本、全方案回归、协议记录、独立完整review及最终fresh verification。
