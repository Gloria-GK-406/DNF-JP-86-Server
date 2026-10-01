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
