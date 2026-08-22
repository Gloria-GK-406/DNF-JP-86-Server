# Routing Decision

- TaskContract: `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-001-table-by-table-ruling/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根控制器在成功者决定时划定，且不含未决语义——判据、红线与要覆盖的表集合都已在根契约中固定。要产出的是一份记录，其完成形态可直接观察。
- Requirement stability: 七条成功条件全部可清点：53 张表、420 列、每处合并的判据说明、两处具名裁定、目标形状的可清点性、每条事实的可复核位置。改动前的库形状构成封闭清单，任何遗漏都能被计数发现。
- Execution direction: 方向已知。裁定所需的事实类别（定位键、基数、写入者、是否存在写入路径）都能从当前代码直接读出，本轮已用机械清点取得其中大部分。
- Remaining uncertainty: 只剩记录本身的组织方式与存放位置，属于不改变成果语义的表述选择。

## Marginal Control Value

- User precision signal: 用户指定按 multi-spec 处理，并在此前否决了分步交付。既往同类持久化任务一律选择 SDD，理由是「只是为了实现别漂移」。
- Direct failure mode: 裁定看起来完备而实际有缺口。53 张表、420 列的规模下，Direct 会自然地只写「明显的那些」，把边角表一笔带过；而恰恰是边角表的漏列会在实施阶段变成静默丢列——记录里没有它，实施者也就不会去找它。第二种失效是判据被事后合理化：先想好要合并，再倒过来解释三条判据成立。
- Useful control: Spec 把「53 与 420 这两个数目必须对得上」「每条事实必须注明可复核位置」钉成验收条件，使缺口成为可计数的红而不是可读性问题；对完整结果的独立评审复核判据是否被倒推。
- Time-for-quality rationale: 这份记录是后续四块实施的设计权威，它的缺口会按四倍放大到实施里；相对于它自身的产出成本，事前把清点条件钉死几乎不增加时间。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界——一份覆盖固定表集合的记录，只有一个交付物、一个评审对象、一个回滚边界。其中的两处具名裁定（邮件附件、金币上限）是同一份记录内的条目，不构成独立可接受的成果。
- Why Direct is insufficient: Direct 无法事前把「数目必须对得上」与「每条事实注明可复核位置」变成验收条件，而这两条正是防住缺口与倒推的全部依靠。
- Why Multi is unnecessary: 只有一个交付物，拆不出第二个可独立接受或独立回滚的成果。
- Reroute conditions: 若裁定过程中发现某块实施的边界必须先由代码试探才能确定（例如某表的写入者无法从静态读取判明），则该处退出本记录、改由对应实施成果在其边界内裁定，本记录只登记该处的悬置理由；若悬置处达到使目标形状无法合成的程度，退回根控制器重新裁定成果边界。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `documentation`
- Why this is sufficient: 起点是 Lite，而本成果没有需要向上购买的控制。可观察边界已由子契约固定成可清点的数目（53 张表、420 列、两处具名裁定），中心执行方向已知（从库形状声明与各读取类中读出定位键、基数与写入者，逐条分类，合成目标形状）。Lite 提供的两个控制恰好对上本成果的两种失效：稳定 Spec 把「数目必须对得上」「每条事实注明可复核位置」钉成验收，挡住悄悄跳过边角表；对完整结果的独立评审复核判据是否被倒推——先想好要合并再回头解释三条判据成立，这种错误只有把整份记录一起看才能发现。
- Why lighter is insufficient: 本尝试由 Multi Spec 父控制器派发，每个尝试一律走 Single Spec，不存在更轻的路线；即便抛开这一点，Direct 也无法事前把清点条件固定成验收，而缺口一旦漏进记录，会按四块实施成果的倍数放大。
- Why heavier is unnecessary: Standard 的独有控制是持久的实施排序与依赖映射。本成果是一次成文的记录，没有「错误的早期选择会在最终评审之前扩散」的路径——裁定错了就写在记录里，评审看得见，改动成本只是改这份记录。两处具名裁定是同一份记录内的条目，不构成需要事先排序的依赖。Governed 的独有控制是独立挑战与审计强度证据，其硬性门槛（人身或系统安全、凭据与访问控制、隐私与受保护数据、资金法务合规、不可逆外部变更、无恢复手段的持久数据丢失）本成果一条也不触及：它只读代码并产出一份 Markdown 记录。
