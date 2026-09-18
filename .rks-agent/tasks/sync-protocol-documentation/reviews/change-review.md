---
kind: rks-review
---

# 独立完整成果审查（2026-09-18，第一轮）

```yaml
kind: rks-review
verdict: changes_required
```

目标：`sync-protocol-documentation` 工作树，基线 `1677e4e2` 加本轮全部未提交和未跟踪成果。范围为 TaskContract、Spec、direct Plan 约束的协议资料、生成脚本和同步检查器；同时检查与当前 `main` 的集成漂移。适用 `reviewing-work` 的 Code 与 Deliverable 准则。此 verdict **不**批准合并或清理。

## 阻断发现

1. `protocol/patterns/data/check-server-documentation-sync.py:63-67, 86-112, 325-344, 434-455, 470-497`：C→S 注册提取只识别当前 `AddGameCmdMessageDecoder<...>(GameProtocol.X|0xNNNN)` 语法。如果这一注册语法整体改为另一种有效写法（或提取器因重构失配），`inbound_registrations` 返回空列表而非错误；`compare` 只检查 `expected_in - catalog`，不检查 C→S 提取结果为空或相对已登记代码覆盖骤降。因此即使全部已注册的 C→S decoder 从提取器视野消失，S→C 保持不变时正常检查仍可报告 `failures: 0`。`--self-test` 没有运行源码提取或 `compare`，只是检查手写集合差值，无法捕获此退化。这使 RQ-001/RQ-007、AC-001/AC-007 所需的“注册协议遗漏自动发现”保护存在静默失效路径。最小修正：对源码根目录及每个受覆盖的注册族加入非空/注册调用形状哨兵，未解析的注册调用应失败；自测试至少用受控源码/目录走正常提取与比较，证明缺登记和提取失配均报错。

## 已核对内容与后续门槛

- `0x0084` 新页区分 A21 三个空请求实帧、参考响应和当前服务端实现，未把参考或测试越级写成真实客户端完成验证；`0x0003`、`0x002D`、`0x0048`、`0x0117` 的改动亦分开实现状态与协议闭合程度。机器生成的索引例外由 `index-overrides.tsv`、生成器和验收例外清单承接；未发现第二个必须先修的证据等级缺陷。
- 当前分支的同步检查为 77 C→S、43 CMD + 82 NOTI、19 显式覆盖，退出码 0；`--self-test` 退出码 0。`check-acceptance.py`、`check-a12-residue.py`、`check-controls.py` 退出码 0。`check-regeneration-safety.py` 本机首次运行失败，是未提供仓外 A21 dump 环境变量；需在后续新鲜验证中指定同版本冻结输入重跑，不能把这次失败算作生成安全通过。
- 当前 `main` 比基线前进 20 个提交，且共同修改 `protocol/game/client-to-server/0x0075.md`。该文件合入时须保留主干后续的实帧/业务记录，并使本分支的索引例外说明与最终页面一致；其他主干协议文件虽有更新，合并后的 catalog 数量、链接与生成稳定性仍须重新验证。此项是集成门槛，不是当前分支的独立阻断缺陷。
- 记忆更新备注 `C:/Users/Gloria/.codex/memories/extensions/ad_hoc/notes/20260916-102618-dnf-jp86-protocol-evidence.md` 已存在；审查不触碰它。

修复阻断项后，须对改动后的完整目标进行新一轮独立复审；本轮审查不替代 TaskContract 的新鲜完成验证。

## 第二轮独立复审（2026-09-18）

```yaml
kind: rks-review
verdict: approved
```

目标仍为基线 `1677e4e2` 加本工作树当前全部未提交/未跟踪成果；对照同一 TaskContract、Spec、direct Plan，以 Code 与 Deliverable 准则复审完整结果，重点重验首轮阻断项和修复的直接影响面。本结论不代替新鲜完成验证，也不授权合并或清理。

首轮阻断项已解决：`check-server-documentation-sync.py` 的 `source_scan_failures` 现在要求源码根目录、C→S decoder、S→C CMD 与 NOTI encoder 注册均非空，并逐文件比较已知 C→S、直接 S→C 和 StateSynchronization 泛型注册的调用形状数与成功提取数。无法解析的 `GameProtocol` 参数会形成“调用点 1、提取 0”的失败，缺失源码根目录也明确失败。正常路径在 `main()` 中实际调用这些检查。`--self-test` 由受控 C# 源文件走提取与 `compare`，分别检出 C→S 缺 catalog、S→C NOTI 缺 catalog、C→S 无法解析、S→C 调用形状失配和源码根目录缺失；这不再只是手写集合演算。本次独立运行正常对账为 77 个 C→S、43 CMD + 82 NOTI、19 条 override、0 failures，自测退出码 0；`git diff --check` 退出码 0。审计说明已相应更新检查范围和剩余盲区。

保留的非阻断边界：检查器并非 C# 编译器；若将来新增**不同名称**的注册 API，且旧注册仍存在，调用形状哨兵无法自动识别，需同步扩展提取器。`server-wire-overrides.tsv` 仍依赖源码审阅和线级验证，不把静态映射当成协议事实。这些限制已在审计说明中明确，不影响当前代码形状和现有接入面的验收。与 `main` 的 `0x0075.md` 双方改动仍需合并时协调，并在合并后重跑资料对账。此前 `check-regeneration-safety.py` 因缺少仓外 dump 环境变量失败；须在验证阶段提供冻结输入后重跑，不能据本审查声称该检查已通过。

## 第三轮独立复审（2026-09-18，流程结果工件）

```yaml
kind: rks-review
verdict: changes_required
```

本轮目标为第二轮已批准的完整成果，加新增 `verification/completion-evidence.md` 和更新的 `spec-result.md`、`sdd-execution.yaml`；其余生产/协议内容未变，第二轮对其成立的检查沿用。适用 Code 与 Deliverable 的结论一致性准则，重点检查 TaskContract 的 SC-8 是否已有实际后置证据。

阻断：`verification/completion-evidence.md:2-3, 20-24` 把整项验证标为 `decision: pass`，`spec-result.md:2-4` 把结果标为 `outcome: completed`，但 TaskContract SC-8 要求一个本地结果提交及干净工作树。本轮实查 `HEAD=1677e4e2`，`git status --porcelain` 仍有 27 个已跟踪修改和 13 个未跟踪文件，因此 SC-8 尚未成立。正文虽说“提交后另行核对”，只能支持“本地接受前的检查已通过”，不能同时支持任务级 `pass`/`completed`。最小修正：在提交前将决策限定为前置检查通过而非 TaskContract 全部条件完成；提交后核实实际提交哈希、变更范围及干净状态，补 SC-8 后置证据，再宣称整项 `pass`/`completed`。`sdd-execution.yaml` 仍写 `verification/in_progress`，这部分当前是诚实的，应与其他状态保持一致。

次要精度问题：`completion-evidence.md` 的 SC-8 行称“全部 39 个改动路径”，当前 `git diff --name-only` 为 27，`git ls-files --others --exclude-standard` 为 13，总数 40。修正计数或去掉易过期的固定数，并重新确认每条路径归属。该计数本身不构成另一阻断，但与上述前置/后置状态同时修正最省成本。

本轮不推翻第二轮的协议内容审查；完成验证对其他 SC 的检查应由验证阶段维持新鲜证据。修复状态矛盾后需对变更的流程工件再审。

## 第四轮独立复审（2026-09-18，状态修复后）

```yaml
kind: rks-review
verdict: approved
```

目标为第三轮所述完整成果及其后修正的流程工件；TaskContract、Spec、direct Plan 和 Code/Deliverable 准则不变。第二轮对生产协议与文档内容的批准继续成立；本轮检查第三轮状态阻断项及其直接影响。

第三轮阻断已解除：`verification/completion-evidence.md` 现为 `manual_required`，明确列 SC-8 的结果提交与干净工作树尚未验证；新增的 `precommit-evidence.md` 虽为 `pass`，但将结论严格限定在 SC-1 至 SC-7、SC-9 和 SC-8 的提交前条件。`spec-result.md` 改为 `kind: rks-stage-result`、`stage: implementation`、`state: complete`，不再把整个 Spec/任务标为终态；`sdd-execution.yaml` 仍为 `verification/in_progress`。本轮确认 `HEAD` 仍是 `1677e4e2`，所以这些未完成声明与现实一致。路径数量的旧固定 `39` 已删除。本轮再次运行同步对账（77 C→S、43 CMD、82 NOTI、19 override）、其自测和 `git diff --check`，均退出 0；未发现修复引入新的产品阻断。

批准范围仅为**可进行一个经审查的产品结果提交**，不是 SC-8 或整项 TaskContract 已完成。提交后必须实际核查结果提交、工作树状态及产物范围，随后再形成最终验证结论；若采用仅含审计工件的第二次封口提交，应明确它不是第二个产品结果提交，并在该提交后再次核查干净状态。与主干 `0x0075.md` 的合并协调和合并后复验仍属后续集成门槛。
