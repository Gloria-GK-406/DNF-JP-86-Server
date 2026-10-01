---
kind: rks-review
verdict: approved
---
# TASK-007：保护券期限、三客户端内容和追加资金的完整混合结果审查

2026-09-30，由生产者之外的新鲜独立 reviewer 直接执行，未另行委派。依据 `reviewing-work/references/code.md` 与 `references/deliverable.md`，读取仓库 AGENTS.md、TaskContract rev3、Spec、Plan、TASK-007、执行游标、`verification/protection-expiry-current.md`、上一完整 approved `change-review-probability.md`，以及实际候选、工具、操作脚本、数据库副本和原始 TRX。本 gate 不要求 challenge set。

**approved**：当前完整准备结果没有未解决的阻断缺陷，可以进入授权的生产部署及新鲜核验。本审查未停止/启动服务、替换客户端文件、发放金币或修改生产数据库；批准不表示已经部署 ready、GUI 已通过、根契约已完成或 ClientVerified。SC-5/SC-9 的部署后证据及 SC-6/SC-8/SC-9 人工门继续由控制器完成。

## 目标身份及继承边界

工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，Git 基线 `6e60c4c67417ce06b88e9e99277fb1e4c0239726`。独立从 Git 枚举所有 Server 变更、新增、删除，重算逐文件 SHA 与排序 compact JSON 集合 SHA：**33 文件，`93fad50a72ab3fe88cc2ed55b3134f3755d3e0de4915bcd9e4412ed22f1ddd75`**，与 TASK-006 approved 目标逐项相同；`git diff --check` 通过。因此实例身份、养成迁移、强化位段、保护结算、通知和诊断日志等未变产品证据按上一完整 review 继承，本次完整检查新增静态内容及其资金/部署接口。未改成功率、装备等级、通用实例期限校验或增幅玩法。

诊断 runtime 的 Abstractions/Application/Data/Persistence/Protocol/Networking/Host 七主 DLL 与当前 Host Release 逐字节一致；Grant 副本中的 Abstractions/Application/Data/Infrastructure/Persistence/Pvf 六 DLL 与当前 Release 一致。新旧 runtime 的 appsettings.json、experience.json 逐字节相同，配置仍读取 `DNFClient-A21-protocol/Script.pvf` 和原数据库绝对路径。

## 内容修改与保真

- 独立核对每个客户端当前原件、备份与 receipt 原 SHA 一致，候选 SHA 与 receipt 一致。debug/protocol 原 SHA 为 `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41`，候选为 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`；protocol2 原 SHA 为 `4315D3B4E8B189D34CBAEFADBC81620438A242814139B479C059E4226AE10417`，候选为 `3847DAEE513DED7039AEFB2896FF1EB03E6FE321E40214BF12CE093BE57DE111`。各自原件是各自修改输入，protocol2 的已有差异被保留。
- 直接检查 Patch 实现和 PvfLib overlay/SaveAs：针对四个明确脚本路径，各要求唯一 kind3 `[expiration date]` 后接 kind6 日期值，仅剪除该连续 10 字节；不重新生成其他 token。SaveAs 只重建受影响 chunk，字符串池没有追加/改写调用。
- 三份 receipt 均报告 712010 文件、仅四文件变化。实际验证算法由独立生产 Dfo.Pvf 打开前后档案，按每个索引核对数量、路径/名称/类型及全部 raw 内容：目标必须等于预先计算的精确剪除结果，其他文件必须逐字节相同。算法和 receipt、候选身份相互一致，不仅比较目标文件。
- Reviewer 另用现有生产 PvfProbe 对三个客户端的各自原档案和候选，分别解码全部四券，共 12 组前后比较。每组解析结果恰好少 `[expiration date]` 和对应日期二 token，其余包含动作、说明、限制及字符串引用的 token 完全一致。7804 日期为 2010-06-30；10004511 为 2015-01-15；10007840/10007841 为 2015-06-04。两个增幅券仍只是材料准备。
- 直接核对生产 InventoryItemCore Encode/Decode，ExpireTime 位于 byte41..44；只读原库核对 UID979/981/982/984 均为0。没有需要清除的实例期限，不应修改 item_core 或绕过通用期限规则。

## 资金、检查与准备操作

Grant 通过实际 Infrastructure/Persistence/Application 模块与 Transactional CommandBus，仅注入 ICharacterDomainService，通过角色2实例读取金币并调用 AddGoldAsync(50000000)。事务内要求原余额2992701及最终精确52992701，carry cap 无法精确到账时抛错回滚；已有 receipt 拒绝执行，提交后若 receipt 写入失败，再执行也因余额 guard 拒绝，不能重复增加。

Reviewer 独立只读比较原备份、dry-run.db 和当前生产库：三库 quick_check 均 ok，**47 个业务表**逐行比较，生产仍与备份完全相同；dry-run 仅 character_stats 角色2 carried_gold 精确增加50000000，其他值相同。dry-run receipt 的 Before/Added/After 与实际数据一致，production-receipt 尚不存在。资金备份、只读验证脚本和生产 guard 足以支持一次执行；没有重复发放原材料/券或修改装备。

直接解析原始 TRX：Infrastructure 12 Passed/0 Failed/0 NotExecuted，Application 84 Passed/0 Failed/0 NotExecuted。Infrastructure 的一项通过实际候选 PVF 读取正常强化表及两种保护动作，其余11项是合成异常/内容测试；Application 84项是强化业务/规则/handler fixture 回归，不冒称84项全在真实 PVF 上执行。这些专项及全档案内容差分支持期限修改，未声称本次重跑整个 solution。

已审查 `preflight.py`、`deploy.ps1`、`verify-funds.py` 和 `postcheck.py`。部署前复查无 DNF 进程/无 Game Established、PID41000 的旧 runtime 路径、源/包/原件/备份/候选及全部生产业务数据身份；仅停确切旧服务，并等待退出后才 File.Replace 三个 PVF。金币执行一次后核对全业务差分，以 Hidden 启动已 approved 诊断 runtime，要求 bootstrap.status 为 ready 及四端口由新 PID 监听。失败路径只尝试恢复本次 PVF/旧服务，不恢复整库或重发金币，因此已提交的追加资金不会被服务恢复抹掉。postcheck 再核对三 SHA、金币52992701、UID689 强化10/独立附魔0、券50/49/50/50及实例 expiry0。

审查现场仍是旧 PID41000；当前没有 Game Established 连接，未部署候选。控制器在执行时仍应使用脚本的新鲜前置检查；成功后再核对实际进程路径、启动 PVF 路径、stdout/stderr、端口/bootstrap、三文件读回和生产差分。文档正确将这些列为待执行，不使用准备证据替代 ready。用户登录确认四券不再过期、资金可见、普通/超级保护失败表现及正常退出重登仍待验。

## 审核目标摘要

工件根：`E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-protection-expiry-20260930`。核心工具 SHA256：

| 文件 | SHA256 |
|---|---|
| Patch/Program.cs | 85fca6947fa5f5f15d6531e3ce2a0f3c521f33fae153798db37d0ee2f088b118 |
| Grant/Program.cs | bf54b87940bbfac4c8bf329f317e2958a15c471355852794ae48a34e4a03b288 |
| preflight.py | fea750d389553e57c1305ba8cd3d18fdd23e485b28a3c78d1c9c95904113243a |
| deploy.ps1 | f5d0f7231240d2fafa3667ad13d87b5a68f0f3fce882ade47ac3f76e44ac4703 |
| verify-funds.py | bc98ea86102e13117fed1b697bc830e174379120e70bfcb0ed8e865d2d10481e |
| postcheck.py | e02da620dcc88a537b5c997283d403c52838da7cd46d78b15875633694154dc1 |

阻断 finding：无。缺失审查前置输入：无。整根契约的人测门未完成；不 seal、不提交、不以静态日期修正证明既往连续成功的根因。

## 第2轮：首次部署故障后的最小整改复核

```yaml
kind: rks-review
verdict: approved
```

首次实际执行未通过：原脚本 `[IO.File]::Replace(...,$null)` 的第三个 string 参数在 PowerShell 绑定为 empty，报路径为空；此前静态审查没有发现该运行绑定问题。该失败发生在第一个 debug PVF 替换前，随后旧 runtime 恢复启动，不把失败命令记为成功。

本轮由同一独立 reviewer 针对明确整改及其直接回归重新审查。当前 deploy.ps1 SHA256 为 **`aa0872052e87f977a12ee0cfb6293a8c2fd16eef7ef0a8c9ebbb59b6dd986aa0`**。变化仅为 `ExpectedOldPid` 参数化及原子替换/回滚使用明确的备份文件路径：新参数仍要求进程路径等于旧 runtime；替换前拒绝已有 `atomic-before/<client>.pvf`，建立工件内目录，再以真实绝对路径作为 File.Replace 第三个参数；回滚保存 failed-candidate。不存在第三参数为 null/空串的调用，不扩大停止范围、不增加 DB 恢复或资金重试。

已读取文本实测工件：replace-test-target.txt 为 after、replace-test-backup.txt 为 before，支持显式第三路径的原子替换行为。Reviewer 没有另行替换文件。独立只读核对三客户端仍为各自原 SHA、47个生产业务表仍完全等于 before-funds.db、金币仍2992701，production-receipt 不存在；debug 候选临时文件及 atomic-before 残留不存在。因此可以使用新的明确旧 PID 重试，不会重复发款或覆盖上一成功替换。

现场 PID1088 的实际路径为旧 equipment-reinforcement-field-fix runtime，bootstrap.status 为 ready，7001/10010/10011/18080 四端口全部由1088监听；没有 Game Established 连接。新候选/源/Grant 等未变证据按第1轮继承。**approved** 当前最小整改脚本；控制器可用 `-ExpectedOldPid 1088` 重试并执行原定全部新鲜部署后检查。当前仍未部署新 PVF、未追加资金、未完成用户验收。
