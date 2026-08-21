# Routing Decision

- TaskContract: `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-003-item-carrier-cut-by-concept/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 设计权威已封存——`Docs/Refactor/TableOwnershipRuling.md` 把 `items` 的 15 列与其来源、`character_rentals` 的 4 列、X-1 的拆分、要携带的键与约束、可空与默认的继承规则及其例外、三条写不成 `CHECK` 的规则全部定死，并经十轮独立评审。本成果不再做边界判断，只照它执行，外加裁定指名交给物品侧回答的五处悬置事项。
- Requirement stability: 十条成功条件可枚举且各自可观察——目标表的列与裁定逐列比对、称号册变换的双向正确性、租借单一承载、判别列消除、上一代表退役、约束逐条落点、三条非约束规则存续、下发帧不变、守卫可证伪、上一轮形状未回退。
- Execution direction: 已知。spec-002 刚在同一套形状守卫与表征守卫的组合下完成同规格的一块，方法已验证。
- Remaining uncertainty: 五处悬置事项（D-5、D-6、D-7、D-15、D-16）需要在实施中给出结论，但每一处裁定都已写明判断所需的事实在哪里，且任一结论都不改变目标形状的其余部分。

## Marginal Control Value

- User precision signal: 用户指定按 multi-spec 处理；上一块（spec-002）在实施评审中确实抓到一处会到达客户端的真回归，说明这一层的控制不是形式。
- Direct failure mode: 次序失效与静默丢约束，且都不产生红灯。四条具名的：其一，称号册的 `category` 若原样搬进 `list_type`，与主背包的 `Main = 0` 撞键——第一个同时拥有两者的角色才会显形，而库里今天没有这样的行，测试不会红。其二，角色背包的读取今天不按 `list_type` 过滤，合并后若不排除 19..23，称号册的行会走进只接受 0/1/2/3/7 的容器类型解析而抛——同样只有同时持有两者才显形。其三，九张源表的约束若在改表时逐条搬运而非先清点后核对，丢掉一条不产生任何测试信号；裁定已点名两条不能直接存续（D-15、D-16），漏掉它们就是把「决定」变成「事故」。其四，三条写不成 `CHECK` 的规则只活在代码里，改写读写路径时丢掉任何一条都不会红——其中「角色的时装缺附加事实是错误、仓库的时装不带附加事实是正常」正是今天区分两种归属的唯一处。
- Useful control: 一份持久的实施排序与依赖映射（Plan）把这四条钉死，并把「先清点约束与规则、后改表」「先写新形状的守卫并证明可证伪、后改组装」定为有序任务；对完整结果的独立评审复核约束、变换与三条规则是否逐条落地。
- Time-for-quality rationale: 四条失效都不产生红灯且都在实机远离改动点处显形；本成果触及九张源表、五张目标表、约 60 列与一处六百余行的组装点，事后定位成本远高于事前排序成本。spec-002 的实测印证了这一点：它唯一的真缺陷出现在唯一没有字节级证据覆盖的那条路上。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界。四张源表并入 `items` 是一次不可分的写入——共用一个 `UNIQUE` 约束与一套容器类型编号，任何一张单独并入都会让另外几张的键失去意义；退役上一代两张表连带拿掉的迁移守卫与它们同生共死；租借与 X-1 虽在概念上独立，但共处同一个组装点 `InventoryEntryAssembly` 与同一份物品侧表征用例，拆开交付会让那份用例在中间状态无法自洽。回滚边界也是一个。
- Why Direct is insufficient: Direct 无法把四条不产生红灯的失效固定成可复核的顺序与清单，而这正是本成果的失效来源。
- Reroute conditions: 若实施中发现裁定的某条事实不成立且修正会改变目标形状，先更新裁定再继续，必要时退回根控制器重新裁定成果边界；若发现上一代两张表存在写入路径，物品域的边界须由根控制器重新裁定（根契约的假设已写明这一条）。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的独有控制正是本成果需要的那一样——一份持久的实施排序。四条失效都是「错误次序或漏项不产生红灯」，Plan 把它们钉成有序任务与逐条检查；code-refactor Profile 的 test-driven-development 与 reviewing-code 覆盖「先证明守卫可证伪、再改组装」这条内部硬次序。
- Why lighter is insufficient: Lite 无 Plan。本成果的失效不是「忘掉某条要求」（Spec 能挡），而是「以错误次序做对的事」与「搬运时静默丢掉一条约束」——Lite 的完整结果评审在这些都已发生之后才介入，看到的是自洽的错误。
- Why heavier is unnecessary: Governed 的独有控制是独立挑战与审计强度证据，其硬性门槛本成果一条也不触及：不涉人身或系统安全、不涉凭据与访问控制、不涉隐私与受保护数据、不涉资金法务合规、不产生不可逆外部变更；目标是本机开发库且项目已定擦库不迁移，无缺乏恢复手段的持久数据丢失路径。
