# 父任务累计漂移检查

```yaml
kind: rks-review
verdict: approved
disposition: aligned
```

## 目标与检查范围

依据父TaskContract Revision1、approved spec、task-units plan及完整001—011 registry，检查**累计交付**是否仍覆盖批准目标并保持中央方案，而非沿用TASK-011局部approved。使用reviewing-work及references/drift.md。本次只读当前tracked/untracked源码、实际调用链、coverage/实现说明/人工清单和已有验证；仅写本文件，没有运行dotnet或改变生产、测试、部署状态。

冻结对象为基准b5506390之后全部Server变化：独立枚举230 paths（含删除的旧guard），按path＋现文件SHA256/DELETED组成LF manifest后独立重算digest：

`022A677546B6B07D5F887340B342F8E0DFE4F424A5CE20C64C16E7AF5D492A25`

与controller目标一致。registry当前11个Task均done只是检查入口；本结论来自父要求与实际累计结果比较。没有发现遗漏的高阶实施结果、中央路线偏移、破坏依赖的局部决定或未经授权扩展。

## 高阶工作与累计交付

| 父Plan工作 | 当前承担与实际结果 | 要求覆盖 |
|---|---|---|
| T-001 共享体系/普通使用/饲料 | 001建立DomainEffect/EffectChange与统一Apply；普通自耗、不自耗、冷却、秘药、宠物饲料完整计划，Feed业务入口删除；011补真实源/pet联合回滚 | RQ-001—004；SC-1/2/6 |
| T-002 装备改造/共用分解 | 002的六领域入口→EquipmentEffectPlanRuleService→基础handlers/instance；成功失败、保护、销毁、返材料均显式；费用容量/字段/UID/销毁保护保留 | RQ-002/003/005；SC-3/6 |
| T-003 制作/开包 | 003完整source/钥匙/多堆成本、prepared/virtual奖励、权益、luck、quota/冷却；Opening仅shared Apply、flush及最终receipt，未包装旧Settle | RQ-002/003/006；SC-4/6 |
| T-004 专用品/普通任务 | 004承担fatigue/coupon/water/rename，005承担普通Finish奖励/转职/技能/关联进度；011补真实物品源+quest/skill/pet联合回滚 | RQ-006/007；SC-4/5/6 |
| T-005 共用经验 | 006将Kill/helper/canonical/solo-party各recipient/Clear接共享ApplyAwardAsync与已决定CharacterExperienceEffect；005任务同效果；旧Award/Finish/Career完整writer关闭 | RQ-003/007/008；SC-5/6 |
| T-006 完整遗漏/整合 | 007事务不确定与租约、008exact保护与claim、009pure当前快照/预编码batch、010完整请求账本/footprint/旧guard关闭、011同库/动态注册/扫描/全suite/文档/人工清单 | RQ-008/009及所有SC/AC的整合约束 |

T-004拆成004/005、T-005由006承接没有丢失父结果。T-006逐依赖详细化为007—011，是兑现父整批失败、内存提交、同请求只结算一次、通知与遗漏检查的必要整合；不是新增道具、重写范围外系统或另起中央方案。各单位临时不足（special同库、全入口重放、当前投影与窗口）在后继被具体消化，未因局部pass隐性消失。

## 中央方案与任务间接口

当前仍是领域收集事实→规则生成完整只读/不可变record计划→统一ApplyEffects→基础instance/container/store→提交后协议投影。重新读取shared executor及Equipment/Opening/Special/Quest计划和XP入口：规则使用已收集领域事实及静态规则，不写live状态；executor完整识别类型/整体preview/全部Validate后序列Apply，不重新判成功失败。没有任意委托或“运行整个旧业务”的DomainEffect。Ledger的handler委托是网络流程接缝，不是业务effect或缓存奖励委托。

物理地址/owner/session、装备UID/预期模板与当前字段读取贯穿计划及底层；设备连续变化不以旧完整core覆写，Destroy保留不可复活约束。special实际状态CAS、prepared placement和shared preview相互接入，普通无退款技能学习豁免没有吞掉mixed退款同行变更。普通任务reward与XP共用决定保持earned明细和成长cap独立，未把cap伪装成earned0。

累计事务路径45→57→58→60、实际SQL参与者、提交后运行态与exact fence保持一致。010主执行identity/precommit冻结与009pure snapshot/Exclusive/batch组合解决跨单位重放接口；18Request/20wire/20main与3XP事件claim分开，不用参数相同当重传，也不把后续readonly作为新主提交。当前重放复用已提交操作的身份与footprint读取当前绝对状态，不重复执行效果、抽签、旧ACK或earned奖励；符合RQ-008所要求的单次业务结算。

## Success Conditions完整覆盖核对

- SC-1：多类型/多目标与注册分发真实存在；未知明确拒绝，whole-Abstractions动态DI集合/duplicate/resolve和普通不自耗/跨栏/同槽/换物回归有证据。
- SC-2：普通使用与饲料均显式计划，Feed公开业务入口删除；实际pet基础AddStomach与时钟职责保留，011真实源/pet事务与投影检查补齐。
- SC-3：强化/增幅保护与损毁产物、附魔/气息/封印/锻造/分解覆盖表和真实入口对应；身份/字段及失败保护证据未因迁移删除。
- SC-4：Recipe/所有opening及fatigue/coupon/water/rename费用和关联状态显式；容量/期限/永久表示/权益/luck/quota均有对应计划与回归，四组真实源同库已补。
- SC-5：普通Finish物品/金币/XP/转职/全页技能/依赖进度与Kill/Clear共享经验均纳入；合法下一次和事件claim、跨级/cap/明细/存储失败保持。
- SC-6：多effect、晚Prepare/SQL失败、确认rollback/typed uncertainty、内存发布、当前重放/发送失败有实际证据；网络发送部分成功不被伪称原子，也不因此允许已提交重付。
- SC-7：全范围caller扫描含未跟踪源，旧完整writers、六guard及Lottery/Vending旧缓存关闭；执行器底层与真正范围外具名解释，没有以wrapper算迁移。
- SC-8：代码、coverage、真实API说明、完整新鲜自动证据和顺序24项人工清单已形成；实机明确HumanPending。后继父挑战/最终审查/verification/完整本地commit属于尚未结束的父流程，不是遗漏实施结果。

对应RQ-001—009/AC-001—008均有累计实施责任与实际证据，不仅依靠registry done。SC-7的独立完整结果最终判定仍由后继final-review承担；此处批准的是路线与结果覆盖没有漂移。

## 边界、授权与证据

no-refund豁免可追溯TASK-004已批准边界：任何Refund!=0在首写前整批进入effects；仅all Refund0普通学习保留原table写，旧循环Remove条件不可达。基础setter、生命周期维护、quest accept/drop/trigger、shop/mail/repair/discard/move、combat耐久/材料、card/ground、tutorial/recovery、petclock/hatch/growth按父范围外具名保留。shared ApplyAwardAsync/领域Rename入口不误算旧writer，未把这些豁免扩成范围内完整业务旁路。

007—010网络/租约/事务保护虽增加横切接口，但服务于已授权RQ-003/008；没有新增固定等级券/升级宝珠、改概率费用上限、重写商城/战斗/条件系统或执行部署。Host composition仅唯一绝对临时DB；实际TCP验证为隔离测试。未修改用户数据库/PVF/client、服务切换、push/PR/merge/清理，没有中间commit。

最终8完整项目实际4912passed/0failed/2skip（PVF可达时两个missing_archive负例）；fresh solution build0warning/error，diffcheck clean。全部正式TRX与首失败历史保留，四组合9与完整Application不累加；runner实际collections-off/effective settings及独立TRX peak1有据。源码/自动测试仅CurrentImplementation；domain+scope同库、真实bus/SQL、TCPmetadata seam、actual close与注入Probe的证明层次明确。

seq-reset、无nonce古老raw碰撞、TouchSocket实际回调调度未直接取证及I/O部分投影等局限未被隐去；它们没有被伪称已验证能力，人工清单保留合法连续操作闭环。父契约明确自动验证可交付代码与人工队列，因此HumanPending符合批准的交付方式，而不是将实机验收偷偷删掉。

累计交付保持批准边界和中央路线，建议继续父fresh挑战/完整结果审查与verification。此drift检查现停止；不替代最终质量批准、完成声明或本地提交。
