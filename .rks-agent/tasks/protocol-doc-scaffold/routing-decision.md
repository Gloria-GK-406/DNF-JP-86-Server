# Routing Decision

- TaskContract: `.rks-agent/tasks/protocol-doc-scaffold/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 经四轮对齐收敛。用户明确把工作切成两步，本任务只做第一步
  （静态可得的包结构与字段可能含义），并明确把响应流程与真实语义划给后续实机开发阶段。
  可观察结果具体：`protocol/` 下按方向分层的一批文档 + 全量索引，逐条可与普查身份集合比对。
- Requirement stability: 可枚举且可复核——身份四元组完整、族归属正确、名称有来源、
  body 段二态（有布局 / 显式未知带原因）、无无证据语义断言、既有手写文档不被降级、
  重新生成不毁人工内容。
- Execution direction: 已确定。机械部分由静态分析脚本产出（既有两份普查脚本已证明该路径
  在本 dump 上可行，4 秒扫完 1099 个调用点）；语义部分按上游模块批量委派子代理。
  用户已明确认可"能直接脚本出来大部分结果在那个基础上搞就更好"。
- Remaining uncertainty: 仅非语义的局部选择——布局提取的闭合率未知。因验收写成"有布局
  或显式未知"的二态形式，闭合率高低只改变 unknown 比例，不改变任务语义或验收边界。

## Marginal Control Value

- User precision signal: 速度与精度并重，但精度的定义被用户亲自钉死在"诚实"上——
  「比起瞎编诚实的填写 unkown 是可以接受的」。同时要求异步加速并授予全权自主推进。
- Direct failure mode: 本任务向权威资料库批量写入上千篇文档，且要多个子代理并行产出。
  Direct 下有两条具体失败路径：其一，schema 未先冻结即 fan-out，N 个代理产出 N 种格式，
  合不回去且无法逐条校验；其二，跨越多次上下文压缩的长跑中进度状态活在对话里，
  压缩后丢失，已完成与未完成不可分辨，导致重复劳动或静默漏做。
- Useful control: Spec 冻结文档 schema、未知分类与文件所有权划分（防第一条）；
  持久 Task Unit registry 使状态权威地存在于磁盘而非上下文，且每个单元有独立完成证据
  与独立复核（防第二条）。完整结果复核另行覆盖"是否出现无证据语义断言"这一最关键风险。
- Time-for-quality rationale: 两条失败路径的代价都是整批返工——上千篇文档格式不一或
  进度不可辨，重做成本远高于先写一份 spec 与一份任务登记。用户虽要速度，但其速度诉求
  指向的是并行执行，而 spec 正是并行产出能合并的前提，二者不冲突。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一验收边界——全部协议文档骨架建成且诚实。C→S 与 S→C 两个方向、
  机械提取与语义富化两类工作，都服务于同一个可接受结果，任何一半单独交付都不构成
  用户所要的"地图"，不存在独立的验收、所有权、集成或回滚边界。
- Why Direct is insufficient: Direct 无法提供跨上下文压缩的持久进度状态，也无法在
  fan-out 之前给并行执行者一份共同的产出契约。这两项都不是靠更仔细的直接执行能替代的。
- Why Multi is unnecessary: 两个方向共用同一套线性反汇编骨架、同一份 schema、
  同一个索引，产出互相校验（CMD 是请求/响应共用编号，两侧对照本身即校验手段）。
  拆成独立 Spec 会人为制造需要再同步的边界。
- Reroute conditions: ① 若布局提取在正对照上无法复现既有已验证结论，则机械路径不成立，
  须回到意图对齐重新选择方法；② 若语义富化被发现需要独立于文档骨架单独验收与回滚
  （例如用户要求先交付骨架再单独立项做语义），则回到 Multi Spec 路由；
  ③ 若发现上游枚举与客户端存在系统性错位，名称层失效，须回到 TaskContract 修订假设。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `documentation`
- Why this is sufficient: 需要的控制恰好是 Standard 的三项——持久 Task Unit 与依赖登记
  （抗压缩、可跨会话恢复）、Task Unit 级独立快速复核（并行产出的即时纠偏）、
  完整结果复核（抓"无证据语义断言"这类系统性问题）。`documentation` Profile 的
  `spec → implementation → review → verification` 拓扑无 plan 阶段，与本任务相符：
  要冻结的是 WHAT（文档 schema 与未知分类），HOW 就是写脚本与派工，不值得再立一份 plan。
- Why lighter is insufficient: `sdd-lite` 自身规定"主 agent 既是编排者也是执行者，
  生产工作若需要委派则回到路由"。本任务必须委派——用户明确要求用子代理异步加速，
  且语义富化按上游模块切分后天然是数十个独立单元。Lite 亦不提供持久 Task 登记，
  无法保护上下文压缩下的进度状态。
- Why heavier is unnecessary: `sdd-governed` 的独立挑战、独立制品审批与漂移控制，
  针对的是高后果逃逸失败。本任务不触及安全、凭证、隐私、金钱、法务、不可逆外部变更，
  也无数据丢失路径：产出是 Git 仓库内的新增文档，既有手写文档受"只增补不覆盖"约束保护，
  错误可由重新生成廉价纠正。Governed 的额外控制换不回相称的收益。
