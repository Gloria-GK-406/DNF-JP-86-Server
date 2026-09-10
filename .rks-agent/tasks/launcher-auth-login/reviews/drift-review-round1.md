---
kind: rks-drift-review
classification: task-rework
reviewed_at_utc: 2026-09-10T10:05:08.889Z
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
