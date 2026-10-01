# 第三轮验收修复与部署记录

```yaml
kind: rks-verification
decision: manual_required
```

当前验证结论是代码已实现、自动回归通过、可进入授权部署；实际部署以回执为准，客户端验收尚未完成。

## 授权与范围

用户确认“我退出游戏了，你完成修改和实现任务后，直接部署再给我一个新的验收清单”。在现有 task/solo-play-roadmap 工作树、基线28724395上直接完成验收修复；不重开已经完成的effect重构SDD阶段。

本次范围：H11赛丽亚词条转换、H22通关德利拉出售、城镇选择瞬移药剂。部署包含既有558b8d6b的H16满包提示修订，同步三个客户端PVF解决H19客户端旧期限声明。保持原数据库、配置及H18疲劳／H19物资准备，无新补给；地下城商店购买、特殊装备售价、其它瞬移变体不在本次实现范围。

## 实现与证据

| 项 | 根因／实现 | 验证与边界 |
|---|---|---|
| H11 | A21第二short固定0，旧handler错误要求-1；Change改0，Unseal仍-1，非法字段warning且不扣费 | ClientStatic构包RVA12EF0E3／12EF0EC；规则及词条effect未改；转换实机待验 |
| H22出售 | 原解析只认item shop，且领域只认城镇；现在识别simple shop及visible on dungeon clear，按Boss已清／ClearOpen／参与账号和角色放行 | 真实德利拉NPC1000／Shop1000；通关及拒绝矩阵通过；普通购买明确拒绝通关专用商店 |
| H25瞬移 | 原0x00ED未注册；新增严格13字节解析、规则构建消耗／位置／冷却effects，统一执行器结算 | 主栏／快捷栏、最后一件、冷却、源及变体、名单、gate等级、地下城拒绝、真实PVF通过 |
| 瞬移故障 | SQLite位置写失败全回滚；提交阶段更新位置及TownArea，投影只发送当前状态 | 通知失败保持已提交状态；同seq不重复扣物品；之后另行移动再重放仍投影新地点 |
| H16 | 已提交的魔盒NoRoom错误码7E→02随本次版本上线 | 单次／批量原通过，满包文案待本轮复验 |
| H19 | protocol2 Script.pvf保留旧2015期限，而实例期限0；退出后备份并同步到canonical PVF | 三客户端SHA256相同，任务券无expiration date；使用仍待验 |

2600014药剂真实PVF等级60、冷却10秒、城镇名单；素喃gate area3、474/234、最低69。目的地使用服务端gate，不信任客户端area0／32767坐标。成功库存／00AC冷却／0017场景及0009名册NOTI；失败CMD00ED，具体UI仍需客户端裁决。位置依频道是否瞬态决定持久化，保持频道策略。

已更新协议人工区0016／01B6／00ED／00AC及CMD00ED页，并在catalog人工区补新增入口与输出。没有把代码或解码日志当作WireCaptured，也没有提前标ClientVerified。

## 最终自动验证

Release完整解决方案build：0 warning，0 error。真实PVF配置；完整八测试项目回归采用 xUnit.ParallelizeTestCollections=false，5281通过、0失败、2跳过。

| 项目 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Application | 2662 | 0 | 0 |
| Protocol | 1230 | 0 | 0 |
| Infrastructure | 754 | 0 | 2 |
| Persistence | 393 | 0 | 0 |
| Networking | 86 | 0 | 0 |
| HttpApi | 50 | 0 | 0 |
| Host | 26 | 0 | 0 |
| Pvf | 80 | 0 | 0 |

两项跳过是PvfQuestDefinitionSource／PvfTownDefinitionSource的“缺少档案”测试，因为本次配置了真实PVF；真实内容测试实际执行。额外最终47项瞬移、商店购买及重放定向测试通过。

第一次全量保留在artifact/full：三个登记列表仍使用旧数量／旧flag，已随新入口修正；另有两项SQLite连接被释放异常，夹具含全局ClearAllPools，测试集合串行后完整回归通过。不将首轮失败抹去或擅称基线缺陷；最终结果只引用final目录。

协议覆盖检查仍有历史欠账，完整checker不能宣称通过。当前54个报告与28724395基线比对，新增00ED登记已在catalog列出；Mailbox未解析输出及其它历史catalog遗漏未在此次扩展修复。证据为documentation-current.txt／documentation-baseline.txt。目录链接和新增页人工核查；机器覆盖块因已有未解析Mailbox登记不能自动生成，保留原块并在人工区登记本次变更。

## 独立审查

acceptance_fix_review按reviewing-work代码检查完成：首轮发现新增德利拉解析意外放开城镇BUYITEM（P2）。已在普通购买规则拒绝VisibleOnDungeonClear，并补handler级资产不变回归；复审approved，无剩余阻断缺陷。补“另一城镇移动后重放旧瞬移”的实际NOTI坐标验证。审查不代替本轮实机验收。

## 运行交付与数据保持

本地结果提交后发布独立runtime，继承旧h15-cera-restrictions-20261001/runtime的appsettings.json和experience.json。acceptedCommit、DLL／PVF哈希见manifest.json；实际切换成功、PID与ready结果仅以deployment.json state=deployed为准。

产物根目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/acceptance-fixes-20261001/`。

- build-final.txt、tests-final.json、final/*.trx、target-final.trx：当前源码验证。
- client-pvf-sync.json、DNFClient-A21-protocol2-before.pvf：三客户端同步及旧文件备份。
- manifest.json、runtime/、deploy.ps1：结果提交的部署身份和入口。
- before-deploy.db、before-deploy.json、post-deploy.json：SQLite备份及逐业务表行哈希对照；部署不改变业务行。
- deployment.json、listeners.json、runtime.pid、stdout.log、stderr.log：实际切换结果；7001／10010／10011／18080的listener必须均属于新PID，API status=ready。

部署前确认无游戏进程和已建立Game连接，并验证旧PID路径；失败恢复旧运行进程，不自动恢复或覆盖数据库。PVF同步另有独立备份。本轮只做一个本地结果提交，不push／merge／清理工作树。

按[第三轮验收清单](../tasks/item-effects-unification/human-test-list-20261001-round3.md)接手。H21依用户安排暂缓，H10／H12缺样本或分项待验状态保留。

## Unverified Conditions

第三轮H11／H18／H19／H22-SALE／H25／H16-FULL及重登需用户实机反馈；已有协议覆盖54项欠账保留。未据自动结果宣称完整任务实机闭环。
