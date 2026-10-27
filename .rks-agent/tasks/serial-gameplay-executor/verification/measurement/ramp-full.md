# 吞吐爬坡（合成负载）— r1

连接 20（单人 8，双人组 6），预热 30 s（按 400 包/秒，不计），每档 20 s，每档前 2 s 不计。

判定规则：某档的背景请求端到端 p99（判定口径：计划发出 → 第一条回包，截尾样本计入）> 50 ms、或该档出现错误（超时、断线、拒绝）、或实际包速率 < 目标的 90%，即停止；可持续吞吐取此前最后一个通过档的实际包速率。每档结束后先等计划发出在档内、仍在途的请求收口（完成、超时或断线，最多 65 s）再统计，负载在等待期间继续按本档速率运行。

| 目标包/秒 | 实际包/秒 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考（实际发出）p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 判定 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 0 | 2884（截尾 2880） | 73970.7 | 82070.7 | 82816.7 | 82970.7 | ≥82970.7 | 31029.9 | 4（50.000%） | 31029.9 | 31029.9 | 13 | 停止 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

可持续吞吐：**无通过档**；停止原因：p99 > 50 ms。

长请求 = SELECT_CHARACTER、CREATE_CHARACTER、SELECT_DUNGEON。错误清单：
- t=35.0s pair04: pair04M: connection closed during SELECT_CHARACTER after 30248 ms (first reply CMD  0x0004 (270B) 01000000).
- t=64.6s solo05: solo05: SELECT_CHARACTER timed out.
- t=64.6s solo00: solo00: SELECT_CHARACTER timed out.
- t=64.6s solo06: solo06: SELECT_CHARACTER timed out.
- t=64.7s pair01: pair01M: SELECT_CHARACTER timed out.
- t=64.7s pair03: pair03M: SELECT_CHARACTER timed out.
- t=64.7s pair02: pair02M: SELECT_CHARACTER timed out.
- t=64.7s pair00: pair00M: SELECT_CHARACTER timed out.
- t=64.7s pair05: pair05M: SELECT_CHARACTER timed out.
- t=64.9s solo02: solo02: SELECT_CHARACTER timed out.
- t=95.2s solo07: solo07: SELECT_CHARACTER timed out.
- t=95.3s solo04: solo04: SELECT_CHARACTER timed out.
- t=96.9s pair04: pair04L: connection closed during SELECT_CHARACTER after 30777 ms (first reply CMD  0x0004 (270B) 01000000).
