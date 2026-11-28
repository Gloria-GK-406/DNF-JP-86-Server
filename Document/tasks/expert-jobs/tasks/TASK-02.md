# TASK-02 · 副职业摊位运行态与会话

## Task Unit Contract

- Revision: 1
- Outcome and references: T-002、RQ-003、RQ-007。实现城镇摊位开关、访客绑定、会话清理与区域投影，提供验证后的摊位操作上下文。
- Owned boundary: 现有专家商店服务与相关会话、城镇集成；摊位创建、进入、关闭及其通知切片；放弃后的同区广播。
- Required checks: 编译及静态检查精确会话、旧摊位、区域和关摊生命周期的接线。
- Dependencies: TASK-01。
- Result review: required。两种店铺都使用此运行态，错别会话可能让付款给错误摊主，生命周期错误会留下旧访客或旧摊。

## Detailed Plan

1. 扩展 `IExpertStoreDomainService`，每一摊持有精确店主会话、当前完整区域、名称、位置、费用、种类和实例身份。访客项绑定精确访客会话和该具体摊位。采用现有私店的参与者/区域读取和互斥约定。
2. 创建、进入和使用入口验证当前角色职业、设备、在线位置、店铺和访客状态。对新增的资产结算提供同一动作内复核、可提交关摊和同区收件数据。阶段关闭按当前 SQLite 事务的非失败型参与者提交，已提交的离区/下线使用立即关闭。顾客离开只解自己的绑定。
3. 接 `0x0257`、`0x0258`、`0x0259`，创建/关闭投影解码和受众依照 A21 证据与上游。兼容现有 `0x021C` 的已证前缀，并为尾部保留待采记录。离区、回选、换角、断线与耐久耗尽共享精确关闭语义，补城镇初次和再次进入时的摊位视图。
4. 对照 `GamePrivateStoreCommand` 的双会话保护和 `CommandFootprintMode.DeclaredNonRequest`，公开店主和顾客的精确身份给后续两个玩法切片，写入中再次验证同一实例。保留 NPC/便携分解入口。

## Result

- Outcome: done
- 完成专家商店唯一运行态的扩展。摊位记录精确选中会话、完整区域和实例，访客绑定具体摊位。离区、主动关闭、下线、回选和换角清理精确会话。旧会话无法关闭后继会话的摊位。
- 玩家操作使用 `IExpertStoreGameplayDomainService.FindUse` 捕获访客与摊主，再在资产工作单元内调用 `ValidateUseAsync` 复核同一摊位、双方在场以及职业设备。耗尽使用 `IExpertStoreDomainService.StageClose(visit.Store)`，放弃职业使用 `StageCloseOwned`。SQLite 成功提交后才撤下摊位及访客，回滚释放关闭保留。
- 接入创建、进入和关闭命令 `0x0257/0x0258/0x0259` 与 `0x021B/0x021C` 投影。创建依据当前 PVF 的费用上限、步行区及 NPC 坐标检查摆放点。初次入区、换区和回城补发可见摊位，离区和放弃向原区及相关访客发送关闭。
- `0x021C` 沿用本仓已有 `u16` 摊主 ID 形状。上游还附角色 ID 低位，该尾部、创建与关闭的实机顺序留给 A21 验收。
- Abstractions、Application、Infrastructure、Protocol 已用 `dotnet build --no-restore -m:1 /p:BuildProjectReferences=false` 依赖顺序编译，全部 0 警告、0 错误。工作区差异检查和新增文件 LF 检查通过。按任务要求尚未运行自动测试和 A21。

## Independent Quick Review 01

```yaml
kind: rks-review
verdict: changes_required
```

独立复核 TASK-02 revision 1、基线 `5313d455f` 上的首次实现。已按 reviewing-work 的 Task Unit 与 Code 准则核对摊位实例与精确会话、访客清理、事务提交回滚、区域投影和开进关命令。PVE 分解与附魔结算留给对应单元。

- 摊位互斥存在反向缺口。专家商店创建会查私店，私店 `OpenAsync` 仍可在同一会话已有专家摊位时创建。先开专家摊位再开私店会使两次投影争用同一角色。应在私店的开店入口也检查该精确会话是否已有专家摊位。
- 专家商店的事务访问器用于必经的事务型关摊，目前声明为可选且默认为空。现有 `RequiredDependencyArchitectureTests` 会拒绝这种服务构造参数。应将依赖改为必需，并同步测试夹具。

- 快审整改：私店开摊依据当前精确会话检查专家摊位，与专家摊位开店侧形成双向互斥检查。专家商店事务访问器为必需依赖，测试夹具使用真实或该场景的访问器，共享城镇夹具接入同一状态契约。Application、Application.Tests、Protocol.Tests 增量编译全部 0 警告、0 错误。保留既有私店文件的 CRLF，其余原 LF 和新增 LF 保持原样，常规差异与忽略行尾空白的差异统计一致。未执行测试。

## Independent Quick Review 02

```yaml
kind: rks-review
verdict: approved
```

复核 TASK-02 revision 1 在基线 `5313d455f` 上的整改。专家商店和私店开摊均检查另一类摊位，专家商店事务访问器为必需依赖，所有现有手工构造均已接入访问器。核对整改后的源码和夹具差异，未见直接回归。执行结果记录 Application 及两个相关测试项目编译通过。独立运行 `git -c core.whitespace=cr-at-eol diff --check 5313d455f` 通过。A21 未运行。
