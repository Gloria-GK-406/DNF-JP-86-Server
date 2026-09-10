---
kind: rks-drift-review
classification: aligned
reviewed_at_utc: 2026-09-10T10:27:38.568Z
---

# Final Plan Drift Review

## Reviewed identity

- 独立 reviewer：`/root/final_drift_reviewer`；未参与本变更的实现或 Task quick review。
- 工作区：`E:\Project\DNF\DNF-JP-86-Server\.worktrees\launcher-auth-login`。
- 基线与当前 `HEAD`：`e567c803b99260786aaddb0d2d4612dc3334b6f4`；实现为该基线之上的未提交工作树。
- 受控输入：`TaskContract.md` revision 1、approved `spec.md` revision 2、approved `plan.md`、`sdd-execution.yaml`（TASK-01—TASK-06 均登记为 `done`）。
- 审查快照：35 个 tracked changed paths、351 个尚未跟踪 paths；tracked binary patch Git blob 为 `cd004c406433c1bb5cc5bc11b2e7a2c37ab7aac4`，未跟踪文件的 `path<TAB>SHA256` 排序清单 SHA-256 为 `A897C6AE5BD7ADDA11E61BFDBF35F1BAFB61FDD809182A96F4443B1AD7F756F3`。`git diff --check e567c803 --` 为 0。
- 审查范围：基线至该快照的完整产品/测试/文档路径及 Task 证据；未把执行者或既有 reviewer 的通过结论当作裁定依据。

## Conclusion

当前变更的认证、票据、HTTP、Game 登录、WPF 和运行手册仍位于批准边界内，中央方法没有被现实推翻；但 AC-015 / Plan T-009 的必需可视证据尚未交付。该缺口可在同一 Task 和同一 Spec 内用一次局部证据返工修正，因此本检查停止 progression，不能进入 change-adversarial 或 final-review。

## Decisive evidence

1. Approved Spec `AC-015`（`spec.md:181`）逐字要求登录、注册、加载、错误四态在 **100% 与 150% DPI 的常规桌面窗口**中做人工可视检查；`EV-003`（`:192`）要求对应启动截图或人工记录。Plan `T-009`（`plan.md:66`）重复了同一四态乘两档 DPI 的检查边界。这不是可由文件元数据替代的可选文档形式。
2. `LauncherVisualRenderTests.RenderState` 创建 `MainWindow` 后只取 `window.Content`，对固定 `760×560` DIPs 执行 `Measure` / `Arrange`（`LauncherVisualRenderTests.cs:103-123`），从未 `Show` 该窗口，也没有建立或验证 96/144 DPI 的实际 `PresentationSource`。必需控件和边界断言在 `dpi` 产生任何行为之前已经全部完成。
3. `dpi` 首次产生行为是在上述布局断言之后：它仅计算像素比例并传入 `RenderTargetBitmap` 的像素尺寸及 `DpiX/DpiY`（`:125-136`）。所以 96 与 144 两个 case 对布局执行的是同一个固定 DIP 场景；后者只是把同一视觉栅格化为 `1140×840` 并写入约 144 DPI 元数据。现有八张图也都没有窗口非客户区，不能证明真实桌面窗口在该 DPI 下的布局、字体舍入、控件可达性或 DPI 上下文切换。
4. 唯一真实窗口图片 `task05-live-login-current-dpi.png` 只覆盖 login。现存记录把其环境描述为 `AppliedDPI=168`（约 175%），并明确承认 96/144 矩阵是离屏输出、不声称完成真实系统 DPI 切换（`TASK-05-wpf-launcher.md:67,144,295,318`）。即使接受该 current-DPI 图片的环境说明，它仍既不是要求的 100%/150%，也没有覆盖 register/loading/error。
5. Practical effect：离屏矩阵确实证明了四态在一个固定 WPF DIP 画布中的可见性、互斥和静态边界，因而可保留为自动化回归；它没有执行 AC-015 特意要求的真实 DPI/桌面窗口变量。缺少的是明确、可观察的验收证据，而不是最终品牌美术，也不是认证链中央设计。
6. 其余边界抽查没有要求扩大本次分类：变更路径局限于受控 Task、`Document`、`Launcher` 与 `Server`；未触碰客户端二进制或只读取证目录。生产路径仍是 HTTP 应用命令 → Abstractions/Persistence 认证事务 → Game 票据兑换 → 持久提交后 OnlineUser 发布，LauncherApi/Application 的项目引用也未越过批准的禁用依赖边界。

## Minimum correction

1. 重新打开 TASK-05 中的 T-009 可视验收，不修改 TaskContract、Spec 或 Plan。
2. 使用当前 Release 启动器的 `--visual-state=login|register|loading|error`，分别在实际有效 DPI 为 96（100%）和 144（150%）的 Windows 桌面/显示器上显示真实 `MainWindow`，生成八份带窗口边界的截图或等价人工检查记录。
3. 每次记录同时保存由该 live window 读取的实际 DPI（例如 `GetDpiForWindow` 或该窗口 `PresentationSource` 的 transform）、状态、时间，以及对无遮挡、按钮/输入可达、无阻塞布局的人工结论；不能只引用 PNG `pHYs`、RenderTargetBitmap 构造参数或系统 registry 的全局值。
4. 保留现有离屏测试作为自动化回归，但不要把它表述为 live 100%/150% 证据。若八次 live 检查发现布局缺陷，仅在 WPF owned boundary 内做最小 XAML 修复并重跑 Launcher tests 与 Release solution build；若未发现缺陷，则只需补齐新鲜证据和 Task 验证记录。
5. 补齐后由新的独立上下文重新执行最终漂移检查。在此之前停止 progression。

## Round 2 — Revision 3

本节是 revision 3 与用户验收取舍生效后的当前裁定；上方内容完整保留为首轮审计记录，但不再描述当前 gate。首轮原文另以逐字节一致的 `reviews/drift-review-round1.md` 保存（SHA-256 `15017ff74c3a2cc219e76532ad94dd88a1c2bc62d51895160042e6c444e8f277`）。

### Reviewed identity

- 独立 reviewer 仍为 `/root/final_drift_reviewer`；复核对象是用户授权后的完整当前工作树，不采用 controller 或 Task quick review 的结论代替独立判断。
- 基线与当前 `HEAD` 均为 `e567c803b99260786aaddb0d2d4612dc3334b6f4`；35 个 tracked changed paths 的 binary patch Git blob 仍为 `cd004c406433c1bb5cc5bc11b2e7a2c37ab7aac4`。
- 当前受控输入：`TaskContract.md` revision 1（SHA-256 `7457a3925eaf062bffa5ea59b834912250f3304cdd94b94ba9aaee38708facba`）、ready `spec.md` revision 3（`545367bbe5a4c63c2b452287b66f07f2ed54ee1ed38c37b1279a79b0eb2227e3`）、ready `plan.md` 指向 revision 3（`a6140ad2991d8eefb7585a700d2dfa091205a3c45ad3eef96aa30672a12fcc64`）、`sdd-execution.yaml`（`00a92bd1016f22726175f0a71970fb23b5430247b17b5c937aadb588e1f0a320`，TASK-01—TASK-06 均为 `done`）及完整产品、测试、文档、Task 证据。
- 排除两份 reviewer 输出后，当前目标快照有 351 个 untracked paths；其排序 `path<TAB>SHA256` 清单（UTF-8、LF、末尾 LF）的 SHA-256 为 `0e92d36383d8e3c7a351c0bfb1ca0be247fda1596a6b0cbf6196e7912f1ef8b3`。`git diff --check e567c803 -- .` 为 0。

### Current conclusion

当前完整变更与 TaskContract revision 1、Spec revision 3 及其 Plan 一致，可以继续后续 change challenge / final review。用户授权的是临时登录页视觉证据投入的明确取舍；它没有改变功能、安全、事务、协议或架构结果，也没有掩盖已经观察到的产品缺陷。

### Decisive evidence

1. Root `SC-8`（`TaskContract.md:66`）要求第一版 WPF 可实际启动，并以启动烟雾和截图/人工检查证明登录、注册视图在常规 DPI 下没有阻塞性布局问题；它没有规定必须切换到两个精确缩放档位，也没有要求四态各自取得真实 HWND 截图。用户最初还明确授权 UI 后续优化、功能优先（`:13,72`）。
2. Spec revision 3 在开头记录了用户对这个临时登录页的验收取舍，并把 `AC-015`（`spec.md:185`）/`EV-003`（`:196`）明确收敛为四态离屏布局与截图，加当前桌面 DPI 的真实窗口启动烟雾；Plan `T-009`（`plan.md:66`）逐项匹配。该变化是 root 成功条件允许的证据成本调整，不需要修改 TaskContract。
3. 首轮指出的事实仍被如实保留：96/144 矩阵是离屏 `RenderTargetBitmap` 输出，不证明真实 100%/150% 显示上下文；唯一 live 图覆盖当前桌面的 login。revision 3 不再把精确双 DPI 四态实窗矩阵列为必需证据，因此这个已知证据边界不再阻塞当前验收；它也没有被重新表述成不存在的实窗测试。
4. 当前证据恰好覆盖 revision 3 的视觉边界：离屏四态矩阵提供控件可见性、互斥和静态边界回归，现有真实启动截图/记录覆盖当前桌面 DPI 的窗口烟雾；`TASK-05-wpf-launcher.md:322-327` 记录了用户原话、收敛范围以及实验性 DPI 注入测试未保留、产品代码未随之变化。
5. Spec 的 `RQ-001`—`RQ-016` 及 `AC-001`—`AC-014`、`AC-016`—`AC-018` 均仍存在：严格单 BaseUrl 与远程 HTTPS 门、现代密码和逐账号旧摘要迁移、至少 128-bit 一次性票据、Game 原子兑换、提交后身份发布、敏感信息禁记、Application 分层边界、Channel Info 到 Game 的真实协议链及提交后失败语义都没有因视觉取舍而放宽。
6. 完整 diff 的中央实现仍沿批准路径：HTTP adapter 派发应用命令，认证/签票/兑换通过 Abstractions 与 Persistence 事务接缝，Game handler 在兑换提交后发布 OnlineUser 并复用既有有序响应；Launcher 只消费 v1 HTTP 合同并按 35 槽模板启动客户端。未发现新增旧写路径、绕过票据的成功路径、未批准依赖或客户端二进制变更。
7. 因此，首轮停止 progression 的唯一原因已经通过用户有权作出的 Spec/Plan 验收调整消除；这不是用较弱证据冒充原要求，而是把当前验收重新对齐到 TaskContract 已授权的功能优先边界。后续独立审查仍应按 revision 3 和完整安全/功能条件判断最终结果。
