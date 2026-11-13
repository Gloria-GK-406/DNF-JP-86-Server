# H16 魔盒满包错误提示修复（未部署）

授权：“先修改不需要部署，我继续测试”。工作分支 task/solo-play-roadmap，修复基线3188846c。仅修改魔盒单开CMD00D0／批量CMD0450的 NoRoom 失败码7E→02；领域判定、effect计划、扣除和成功回包保持原实现，共用 Error 的其它消费者保持。

用户 H14 验收通过（缺材料由 client 检查）；H16 单开、批量成功路径通过，满包提示失败，不能把整项记为已完成。

## 原因与证据

运行日志2026-10-01 21:28:33：MagicBoxBatchOpening slot107 count10 result=NoRoom，随后只发送长度17的CMD0450失败应答。编码器的旧分支生成body007E，用户看到防沉迷。日志不是原始帧，本次未新增WireCaptured证据。

静态输入：DNFClient-A21-debug/DNF.unpacked-b1-debug.exe SHA-256 d7618a102251f1db724f2514c74a4cc855df26e6a4bc308f8ee9f2397db0a99a；实际运行目录DNFClient-A21-protocol2/dstr.dat SHA-256 60e9857773831b341bae0d490fcaeaf0af184b9776717e2fcf293e1bc7f92f10。FILE/102资源SC01、key5819AF17、链式XOR解密为UTF-8。

| 分支 | 单开RVA | 批量RVA | 字符串ID与中文 |
|---|---|---|---|
| 7E | D16FC5 | D17FE6 | 70005：正在适用防沉迷系统的限制 |
| D2 | D16FAC | D17FCD | 70149：帐户安全模式，不能进行敏感操作 |
| 普通失败（02） | D16F94 | D17FB5 | 746：物品栏空间不足 |

失败分支先减69、再减15匹配7E、再减54匹配D2；02均不匹配，进入普通失败分支。这是当前A21指令与资源直接证据；不是官服错误码枚举的全量还原。旧协议记录把参考实现映射当作语义，旧测试也断言7E；本次保留错误被推翻的原因并同步protocol记录/catalog。

## 验证与后续

- 新增单开／批量NoRoom编码检查，修改前2项均失败（期望0002、实际007E）；修复后codec21项通过。
- Protocol完整回归1228通过，无失败／跳过。现有满包不扣源／材料的Application定向回归1项通过（既有业务代码未修改）。
- 证据：E:/Project/DNF/DNF-JP-86-Server/artifacts/h16-box-no-room-20261001/ 下 red.trx、codec.trx、protocol.trx、capacity.trx及checks.json。
- 按用户要求未部署；PID18764及H15运行目录保持，全部运行Dfo DLL哈希与原manifest一致。用户继续测试使用旧服务；稍后部署才会生效。新满包提示与资源无扣除仍需部署后的实机复验。

## 2026-11-13 后续：#315 改为抽奖前预检

本节修复只改了错误码，保留了"满包整单拒绝且不扣礼盒"的语义；这让"能否成功"取决于抽中了什么，玩家可借布置背包零成本重抽。#315 改为：

- 魔盒在抽奖前检查装备、消耗品、材料、专业材料、宠物、时装六栏各至少 N 个空格（N=2；#325 起幸运开盒不再加倍，见下节），不满足仍回 02、不扣任何东西；通过后扣盒子与钥匙，放不下的结果转系统邮件（不过期）。
- 70 依据：`CUser::check_multibox_error` 0x08675edc 检查 ITEM_TYPE 1/2/3/10/5；`CSlotBoundChecker::init` 0x08373674 把 ITEM_TYPE 10 定为槽 201–248，对应 A21 `SortItemMapping` 的 `(0,10) => ExpertMaterial`；`check_locked_multibox_error` 0x08675fd2 的 N=2 与 `DataManager+0x877e` 倍率。时装栏是本服补上的。

## 2026-11-13 后续：#325 幸运开盒不再加倍

- 线上 0.3.1：消耗品栏只空 2 格的角色在幸运值 8 时被要求每栏 6 格，幸运值只在开盒成功后前进，于是一直卡在 8、盒子打不开。
- 修复：魔盒预检固定每栏 N=2，与幸运值、批量次数无关；幸运加倍的额外份数和其它放不下的结果照常转系统邮件。70 的 `DataManager+0x877e` 倍率值仍未知，本服不再采用。
- 测试：`PackageOpeningTests.Magic_box_needs_two_empty_slots_per_tab_even_when_lucky`、`Lucky_copies_that_do_not_fit_after_the_two_slot_check_are_mailed`。
