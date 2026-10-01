# TASK-007 — 保护券去过期声明、客户端部署及额外资金

## Task Unit Contract
- Revision: 1
- Outcome: 四券不再声明2010/2015静态过期日期，三个客户端及服务读取对应修正内容；角色2额外5000万金币精确一次到账，ready供用户继续验收。
- Owned boundary: 四券PVF脚本、候选档案/备份/三个客户端部署、已复核诊断服务切换、离线资金领域事务和部署文档。无新服务端业务代码。
- Dependencies: 契约rev3明确授权；TASK-006完整review approved/source33 fingerprint93fad50...及已准备包；游戏退出、无Established连接现场检查。
- Required checks: 每个客户端原档案独立备份，四脚本仅删除expiration date及其值，所有其他档案raw内容/路径/类型不变；原四券action/普通概率表保持，实例ExpireTime核对；候选真实PVF强化内容/行为专项；金币副本试跑与全业务表差分；完整独立review、部署hash/服务ready/实际余额只读验证；用户最后GUI门保留。

## Detailed Plan
1. 核对三个实际客户端目录和原SHA：debug/protocol相同，protocol2不同；分别原档案为模板，不能覆盖已有差异。检查当前游戏进程和Game Established状态，无游戏时可按明确部署授权继续，不重复询问退出。
2. 使用已有PvfLib二进制overlay/SaveAs工具，从Dfo.Pvf独立读出的四脚本删除[expiration date]及日期token，不重编码其他字段。生成候选并用独立Dfo.Pvf遍历全档案，精确比较只四个目标raw变更，其余文件/类型/路径保持；输出receipt和备份。
3. 审查现有券实例ExpireTime；为0则无须修改实例，不改通用期限规则。候选protocol用于服务内容回归，两种增幅保护继续只备物资，不新增玩法。
4. 原库在线备份并以实际CharacterDomainService.AddGoldAsync和Transactional CommandBus在副本先加50000000，核对最终carry cap及仅character_stats角色2carried_gold变化。生产guard按实际前值且receipt拒绝重复。
5. 全部候选/命令就绪，独立完整mixed review（未变产品引用已复核目标并重新核对身份）；游戏退出状态再检查，停确切旧服务，分别原子替换三个PVF，追加金币一次，再启动已有诊断包。失败按明确备份回退本次文件/服务，不抹其他资产或重复发放。
6. 核对部署后三个SHA与各候选一致、服务实际PVF路径/启动日志/四端口/bootstrap/stderr、+50000000及其他资产保持。更新清单，以当前余额/UID等级/券余量为基线，用户验证券不再显示过期及保护/重登。

## Readiness and Attention Check
ready：用户直接授权这次内容/资产变更，新增SC-9对应最终可用性；保留已存在实例期限规则和各客户端差异。当前只为继续验收准备资源，不借改PVF修改概率或直接制造装备结果。

## Result

- Changed: 分别从三个客户端自身原PVF删除四券expiration date/value两token；其他712006文件raw/路径/类型不变，protocol2差异保留。三个实际客户端已原子替换；角色2真实领域事务追加50000000一次，2992701→52992701；诊断runtime已切换PID40060。
- Checks: 候选Infrastructure12/App强化84全通过0skip；每档案712010全raw比较；实例期限正确byte41为0，无core写入；副本与生产47业务表差分仅金币变化；33源指纹/七主DLL身份未变，独立完整review及脚本整改复核approved。部署后SHA/进程路径/四端口/ready/空stderr/实际内容路径/资金与其他资产核对通过。
- Incident: 首次File.Replace空参数绑定故障退出1，未换PVF/未发金币，旧runtime1088恢复ready；修正明确备份路径并独立第2轮审查后部署exit0。不将失败命令改写为通过，不恢复整库或重发。
- Evidence: [当前期限与部署证据](../verification/protection-expiry-current.md)，[完整审查两轮](../reviews/change-review-expiry.md)，主仓库artifacts/equipment-protection-expiry-20260930原始备份/三receipt/TRX/production receipt/deployment/postcheck/log。
- Remaining: 用户登录确认四券不再过期及金币可见，普通/超级实际保护失败与正常重登、结构认可按原人工清单继续。后台部署交付done，根契约verification/manual_required；不提交、不seal、不标ClientVerified。
