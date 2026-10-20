---
kind: rks-verification
decision: pass
---

# 南部溪谷完成核验

核验对象为本地可供部署和用户实机验收的实现，代码HEAD `05f1a0cf527511a111901c0c7a0d8369d760ed36`。不包含部署、运行数据修改或原生客户端实机通过。核验时分支为 `task/southern-dale-20261019`，构建/最终全量测试前后HEAD一致，产品工作树和暂存区均无差异。

依据TaskContract revision 1、Spec/Plan及[独立复审](../reviews/change-review.md)。复审绑定同一HEAD，approved；[前轮](../reviews/change-review-r1.md)的F-1已通过独立复审关闭。

## 检查结果

| 检查 | 结果与证据 |
|---|---|
| 最终Release构建 | `dotnet build Server/86JP.Server.slnx -c Release --no-restore --verbosity minimal`，退出0，0警告、0错误；`artifacts/southern-dale-20261019/verify-final-build.log` |
| 最终完整无PVF回归 | 清空进程内PVF_ARCHIVE_PATH后，`dotnet test Server/86JP.Server.slnx --no-restore -m:1 --verbosity minimal --logger trx --results-directory artifacts/southern-dale-20261019/verify-final-no-pvf`，退出0；8项目共7523项，6653通过、870跳过、0失败；TRX逐份读回计数 |
| 真PVF修复与相关回归 | `remediation-pvf/`的3份TRX：Application210、Protocol21、Host11，共242通过，0跳过、0失败；分别包含SouthernDale、共享身份、牛头、ItemDelivery、通知/领域事件、CurrentView/Replay、附魔并发、0015/033F和活动HTTP。日志`remediation-Dfo.Server.*.log`含具体命令输出 |
| 真PVF内容目录回归 | `real-pvf-passed/` Infrastructure的45项：44通过、1跳过、0失败；跳过项为“缺少PVF时的报错”场景，因为当前明确提供归档。此后内容解析代码未改动 |
| 审核F-1的行为复现 | `review-f1-red.log`两项均因实际通知没有0015而失败；`review-f1-green.log`两项通过，后续又包含于Application210项回归 |
| 差异与仓库边界 | `git diff --check`和暂存差异检查通过；只提交本任务拥有的源代码、测试和说明。原有未跟踪调查/outputs/cache未纳入提交，源分支`task/next-round-20261019`仍为Spec baseline |

真PVF仅只读使用 `artifacts/collection-box-text-20261019/Script.server.pvf`，SHA-256 `7E679699E814F60CE2B9315E661DF62A31F7D784DD49FE221FB9F2BE66EA425B`。真PVF用例与缺归档回归分开；没有将被跳过的测试计为通过。

## 成功条件对应

| 合同条件 | 决定性证据 |
|---|---|
| SC-1 / AC-001 | EventInformationIdentityTests相同无详情依赖合并、不同线上内容拒绝；SouthernDaleTests四种开关组合及过期/未开始；Tau回归；选角编码和在线通知复用同一活动投影，0015/033F启停输出回归通过 |
| SC-2 / AC-002 | SouthernDaleTests真实PVF、真实两成员队伍；缺票、低等级、关闭、非法模式、事务回滚、成功后重复入场均保持期望票数和疲劳；实际路由接入EnterDungeonCommand |
| SC-3 / AC-003 | 登录/在线共用领取键，05:59→06:00、持有1补1、持有2不加、服务重建；真实规则为RefillToTarget/2；限制为已满的真实任务物品槽后跨三日仍只有2张待领邮件，无多日累加；两种活动票的旧零点记录保守兼容 |
| SC-4 / AC-004,005 | 当前7格全部16候选房间经通用工厂构建，包含APC、宝箱怪及三领主；无黄金卡、无虚弱。真实MonsterDeath领域调用链结算，第二成员收据写入失败时全队钥匙/收据/actor死亡回滚；重试只发一次，服务重建仍识别已领 |
| SC-5 / AC-006 | 20549真实定义、钥匙兑换、每日完成限制、关闭新接取；7615钱包金币及额外2钥匙配方，材料不足/满钱包不耗输入；2681819+2681820真实MagicBox开箱；当前视图/Replay与启停/日切实际0015通知均覆盖 |
| SC-6 / AC-007 | 最终Release、完整无PVF、真PVF重点回归及独立完整审核/修复复审通过；[实现与实机步骤](../../../SouthernDale-20261019.md)明确策略和证据边界 |

## 证据边界

该pass只针对合同约定的本地实现与检查。033F类别1任务映射仍为ClientStatic/High；没有新增原服实帧或用户原生客户端验收。山麓入口、NPC任务UI、特殊房间脚本/宝箱表现、四人联机与日切实时效果仍按交付清单进行实机验收；issue #153保持待验。

早期真PVF全量回归发现目录计数、注册清单、活动清单及测试夹具问题，均已修正并定点复核。一次附魔同步测试在跨项目并行时触发5秒等待超时，后续串行重点回归包含它并通过，未改附魔业务。没有宣称最终重新跑过全部真PVF测试；当前结论采用完整无PVF与适用真PVF重点覆盖。

独立审查额外发现的在线任务资格刷新缺口已作为单独修复提交，两个实际编码帧回归保护其启停及06:00行为。
