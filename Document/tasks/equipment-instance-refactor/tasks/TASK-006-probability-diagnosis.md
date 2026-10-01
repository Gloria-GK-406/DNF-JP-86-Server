# TASK-006 — 连续强化成功的现场诊断

## Task Unit Contract
- Revision: 1
- Outcome: 核对用户长剑+1至+15连续成功的实际服务调用、运行内容、随机源及判定；增加每次draw/失败阈值/结果可观察性，进入可定位的现场复验。不宣称尚无证据的概率缺陷已修复。
- Owned boundary: 普通强化规则服务的只读判定日志、诊断工件、当前部署及人测状态；不改变公式、概率、随机源、wire或资产。
- Dependencies: 当前rev2未封存，TASK-005自动与独立完整复核通过；用户当前连续成功反馈，原SC-4/5/6/8未验。
- Required checks: 实际PVF普通表、生产DLL实际注册类型、纯规则全draw和真实随机抽样；相关Application全量/Release；日志包含原始draw和阈值，计算与提交证据区分；fresh independent complete-result review；用户正常退出后才切换，再记录新请求。

## Detailed Plan
1. 保存实际日志及当前只读资产；核对服务PID、内容路径与字节身份，不修改玩家现有+15。
2. 在主仓库artifacts独立probe引用当前运行DLL，通过实际Infrastructure/Application模块注册取得内容源、规则和随机源；不注册Persistence，不调用领域结算。遍历目标+2至+15，每个等级全100000draw及100000次实际随机抽样，核对失败可出现及精确阈值。
3. 现有现场日志没有draw/阈值，不能把旁路抽样升级为现场根因结论。在规则服务记录每次计算的模板、old/target、draw、failureThreshold、penalty、protectionRetain、outcome/newLevel/consumeProtection；明确只是计算结果，提交仍由UPGRADE_ITEM证明。
4. 自动检查、Release publish和独立复核；现有服务保持运行至用户正常退出。切换保留原库、备份、原概率和全部资产。
5. 实机下一次正常请求用于核对实际draw与阈值，并配对提交结果。没有发现根因则保持未知，不强制随机失败或调整概率。保护失败门继续待验。

## Readiness and Attention Check
ready：仍为rev2相同验收边界中的局部诊断，没有新玩法或公式授权扩展。当前关注是找运行现场与纯规则差异，原资产发放/正确强化位段/保护回归全部保留；无终态或accepted commit可回滚。

## Result
- Changed: RuleService只有结构化计算日志增量，概率/抽样/结算/wire保持。完整33文件指纹93fad50a72ab3fe88cc2ed55b3134f3755d3e0de4915bcd9e4412ed22f1ddd75；对TASK-005仅该文件改变。Release runtime已准备，尚未切换。
- Why: 冻结日志/只读快照确认14次连续成功、UID689+15，但旧现场每次draw不可恢复；部署DLL纯quote/实际随机probe可正常失败。随后旧现场sequence123自然失败15→10，超级券50→49、其他三券不变，已排除无条件成功；仍以日志取证定位后续每次判定，不猜测修概率。
- Checks: Release solution build0警告/0错误，publish命令exit0（quiet日志空为正常，7主DLL与Release输出一致）；git diff --check通过。两个并行Application全量各2355pass/1fail，分别原宠物fixture清理文件占用和原技能fixtureSQLite已释放，保留TRX。相同程序集以串行参数完整重跑2356pass/0fail/0skip，exit0，不改任何测试或产品来隐藏失败。其他未变目标保留TASK-005完整结果。
- Evidence: [诊断证据](../verification/probability-diagnosis.md)，主仓库artifacts/equipment-reinforcement-probability-20260930/下全部原始日志/TRX、纯probe及manifest。
- Remaining: [独立完整review](../reviews/change-review-probability.md)已approved，root再核对prepared源码/七DLL/原始TRX，exit0。当前转入verification/manual_required；安全切换及真实下一次请求draw/threshold证据仍待用户正常退出与操作。此前连胜根因未确认，超级保护后端已核对，用户显示/普通保护及重登门待反馈。用户退出是必要操作，由question主动询问，不纯等待todo、不提交。

后续TASK-007部署补充：用户已退出；诊断runtime在期限修正/额外资金部署时完成切换，PID40060四端口及bootstrap ready，实际PVF路径与三客户端候选一致。详见[期限部署证据](../verification/protection-expiry-current.md)。新请求日志仍待人测，原根因未知与其他人工门保持。
