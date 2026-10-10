# 自动化证据清单

全部原始 TRX 保留在本工作区忽略的 `artifacts/mirror-arad-entry-20261010-verification/`，避免提交约13MB机器输出；本目录保存测试日志、计数/哈希清单和PVF探针文本。下表记录全部TRX的计数与SHA-256，未把重叠测试次数相加。

| 结果文件 | 原始TRX Counters: total / passed / failed / notExecuted | SHA-256 |
|---|---|---|
| artifacts/mirror-arad-entry-20261010-verification/baseline-pvf-counts.trx | 2 / 0 / 2 / 0 | `d85699c238de512c83093987fc3607855879ae35a9bdc0cf4a7bee2628c182ea` |
| artifacts/mirror-arad-entry-20261010-verification/final-mirror-content.trx | 4 / 4 / 0 / 0 | `bdcc8bb1370ff3b1bbb13b37241c9c12a82507a67a15321e4cb92e5921172d93` |
| artifacts/mirror-arad-entry-20261010-verification/final-mirror-domain.trx | 94 / 94 / 0 / 0 | `902f7febc3e093b7a49b30c9b67419dfec335bb2aa29227c2de7616956b4694a` |
| artifacts/mirror-arad-entry-20261010-verification/final-mirror-protocol.trx | 61 / 61 / 0 / 0 | `6ed8826fd71dd058a2539939f25673d5c490070e73e747333d52c97aeb58694d` |
| artifacts/mirror-arad-entry-20261010-verification/final-Persistence.trx | 468 / 468 / 0 / 0 | `057734981f85a7cea56463716e7b1e8001d94040f7efdf4dbfaf7691eda6dff7` |
| artifacts/mirror-arad-entry-20261010-verification/full-Application.trx | 3495 / 3495 / 0 / 0 | `3c832329f016c6f7b943ca4f2be2ddedc1003488d45232ac0968530264e3cf68` |
| artifacts/mirror-arad-entry-20261010-verification/full-Host.trx | 156 / 156 / 0 / 0 | `b8741ad9b61a88c4303cb13ae1e0677ac7cc6dca6c0211610aed7a19167dbcd5` |
| artifacts/mirror-arad-entry-20261010-verification/full-Infrastructure.trx | 856 / 852 / 2 / 0 | `3eb1f88d094ba6f8282a130d80c7623b810a9081179c49152a0863f7d42c1c44` |
| artifacts/mirror-arad-entry-20261010-verification/full-Persistence.trx | 468 / 467 / 1 / 0 | `beeb9a5759ab91b6815b158e730369c8f505da1f67d0dfc9d0a0bda57d4ccfd3` |
| artifacts/mirror-arad-entry-20261010-verification/full-Protocol.trx | 1426 / 1426 / 0 / 0 | `0ab16f66a547308e4887f84ad5f2f396a154e43b3edb18f206b6ef9a327a5a76` |
| artifacts/mirror-arad-entry-20261010-verification/mirror-event-integration.trx | 8 / 8 / 0 / 0 | `baf4f857fcca291b24cc60f2f10dce0dc6c06b0d74601f1a320b1dd781042cf3` |
| artifacts/mirror-arad-entry-20261010-verification/mirror-protocol.trx | 61 / 61 / 0 / 0 | `35e1f9af8ee4e89adbde209c508a4d62c6b84483bfcc35b4e6291523e889bb21` |
| artifacts/mirror-arad-entry-20261010-verification/mirror-quest.trx | 41 / 41 / 0 / 0 | `551e29eb7d4cae5168e9e8bbcc28bc8cbc2e04849d58f24144eb0b3ce8e929ba` |
| artifacts/mirror-arad-entry-20261010-verification/missing-archive.trx | 2 / 2 / 0 / 0 | `ce48faa9a388e2bad3a1143e7c0a72cf3d9dfd06aebcbf7c0480d6700fd2cf26` |

注：Infrastructure原始Counters.notExecuted=0，但UnitTestResult实际有2个NotExecuted，executed=854。这是xUnit/VSTest的计数呈现差异；本任务按outcome记为2个skip，并在无PVF环境单独执行通过。
