---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: PVF Content Infrastructure

## Context

### Source Request

用户在审阅主干 `Tool/PvfLib` 的实测问题后，决定不修补而重写，并授权：「你在牵出的重构工作区应该再牵出一个新的分支实现这个pvf读取的重构。应该包括pvf读取基础库的实现。基础层实现，然后继续完成前一个任务的G5。」

在此之前用户已就分层作出决定：PVF 读取能力作为库存在，与 `Dfo.Server.Application` 同级新建 project；基础设施层实现一个领域抽象接口，内部用该库；Application 的规则服务只依赖抽象接口。用户并明确要求跨越基础设施边界的数据「一定概念化…不能有 pvf 相关的知识，但是也没有领域知识」。

### Current Situation

分支 `codex/architecture-refactor` 已建立 Clean Architecture 骨架（7 个 `Dfo.Server.*` 项目，118 项测试通过），但 `Server/src` 对 PVF 的引用为零：`Tool/PvfLib` 虽在文件树中，却不在 `86JP.Server.slnx` 内，也无任何项目引用它。重构分支目前完全没有读取 PVF 的能力。

`select-character-domain-migration` 任务的 G5（`spec-006-load-world-entry-state`）已封存为 blocked，其 `spec-result.md` 记录的直接原因是工作区内不存在 `Script.pvf` 或等价的完整任务/收集箱/宠物导出目录，因而无法诚实地满足内容完整性边界；产品改动已回滚至基线 `b8e817c`。操作者现已提供 `Script.pvf`（56.1 MB，588,559 个文件条目），该阻塞的外部前提已消除，但仍缺少读取能力。

主干 `Tool/PvfLib` 的实测问题已记录于 `Docs/PvfLib重写_项目与文件结构.md`：启动常驻 276.4 MB、chunk 缓存无上限（全量触达后 RSS 913 MB）、单次取字段分配 489.8 KB、解密原语每 4 字节分配一个 `byte[4]`。该库另有 72% 的代码（9,325 行 / 30 个文件）是游戏领域模型，不属于格式库职责。

## Goal

重构分支具备一条自有的、只读的 PVF 内容读取链路：一个零领域知识的格式库，加上基础设施层实现的领域抽象接口，使 Application 能在不知道 PVF 存在的前提下获得类型化的静态内容定义，并使 G5 所需的内容类别可被诚实地满足。

## Scope

### In

- 在 `Server/src` 下新建与 `Dfo.Server.*` 平级的只读 PVF 格式库项目，并纳入 `86JP.Server.slnx`。
- 该库提供容器解析、分块解码、脚本记录读取与文件定位能力，且不包含任何游戏领域概念。
- 在 `Dfo.Server.Abstractions` 定义内容来源接口及其边界记录类型，命名与表达均使用领域语言。
- 在 `Dfo.Server.Infrastructure` 实现上述接口，此处是唯一知道 PVF 格式的地方；PVF 词法归一化在此收敛为单一实现。
- 内容来源覆盖 G5 `spec-006` RQ-001 所列的定义类别（任务、日常活动、收集箱、宠物/生物），并对缺失或畸形来源显式失败而非静默降级。
- 为格式库与内容来源提供测试，并保持既有解决方案测试不回归。
- 记录读取路径的配置方式，使 `Script.pvf` 无需入库即可被定位。

### Out

- G5 本身的实现（属 `select-character-domain-migration` 任务的后继尝试）。组合定义与角色进度以产出「可接任务」「可领日常」等业务结果的查询服务，按迁移矩阵的分组归属 G5，不在本任务内。
- 迁移矩阵行 49–51（日程元组）的定义来源；该三行标注含义未确认，按本契约约束不得以业务名称进入契约。
- Application 层规则服务的实现。
- PVF 写入、打包与解包能力。
- 主干 `Tool/PvfLib` 的删除、修改或其领域模型的迁移。
- G5 未要求的内容类别（如装备/物品元数据）的内容来源。
- 归档内容语义的逆向扩充；仅使用已确认含义的字段。

## Constraints

- 链路只读：本任务不引入任何修改归档的能力，服务端也不得依赖此类能力。
- 格式库不得包含游戏领域概念；`Dfo.Server.Abstractions` 与 `Dfo.Server.Application` 不得出现 PVF 格式概念。
- 跨基础设施边界的类型必须同时满足：更换底层存储时无需改动，且更改游戏规则时无需改动。
- 沿用重构分支既有约定：接口与边界记录置于 `Abstractions`，领域类型取自 `Data`，实现置于 `Infrastructure` 并以技术名前缀命名。
- `Script.pvf` 不进入版本控制。
- 含义未确认的归档字段不得以业务名称进入契约；若必须暴露，须显式标注为未确认。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 新格式库能正确读取操作者提供的 `Script.pvf` 全部条目，解码结果与既有实现一致 | 对同一归档逐条目比对新旧解码输出的一致性测试结果 |
| SC-2 | 读取过程不产生随读取量无界增长的常驻内存，且启动常驻显著低于旧库实测的 276.4 MB | 同一归档上的内存基准对照，含启动后与大量读取后的两个观测点 |
| SC-3 | 单次内容读取不产生与内容体量成正比的临时分配 | 分配量基准对照，以旧库实测的 489.8 KB/次取字段为参照 |
| SC-4 | 调用方无法观察到已被回收或替换的内部缓冲区内容 | 证明该情形不可达的测试或编译期约束说明 |
| SC-5 | 跨基础设施边界的类型不含 PVF 格式知识，也不含业务判定 | 类型定义审阅，加上 `Abstractions` 与 `Application` 中不存在 PVF 概念的可复核检查 |
| SC-6 | 内容来源提供的定义足以支撑迁移矩阵行 12、14、32/33、58 各自的组合运算，而不只是能读出定义；来源缺失时显式失败 | 以定义加一份构造的角色进度算出该行所需业务结果的测试，含存在、为空与来源缺失三种情形 |
| SC-7 | 既有解决方案测试保持通过 | 全量测试运行结果 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「你在牵出的重构工作区应该再牵出一个新的分支实现这个pvf读取的重构。应该包括pvf读取基础库的实现。基础层实现，然后继续完成前一个任务的G5。」 | 在自 `codex/architecture-refactor` 派生的分支上，实现 PVF 只读格式库与基础设施内容来源，含相应抽象、测试与本地结果提交。G5 的续做属另一任务，不在本授权内。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对 `codex/architecture-refactor` 的任何写入。
- 删除或改动主干仍在使用的 `Tool/PvfLib`。
- 超出 G5 所需类别的内容来源扩充。

## Assumptions

- 操作者提供的 `Script.pvf`（56.1 MB，588,559 条目）是本任务的权威归档；它经 `.gitignore` 排除，通过配置定位而非入库。
- 重构分支现有的分层约定（`Abstractions` 接口与边界记录、`Data` 领域类型、`Infrastructure` 技术前缀实现）在本任务期间保持不变。
- G5 对内容类别的要求以已封存的 `spec-006/spec.md` RQ-001 为准；若后继尝试修订该要求，本任务的 SC-6 覆盖面需相应复核。
- 主干 `Tool/PvfLib` 在本任务期间保持可用，可作为 SC-1 的对照实现。
