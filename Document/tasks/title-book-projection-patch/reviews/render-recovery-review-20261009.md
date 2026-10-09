---
kind: rks-review
verdict: approved
---

# 称号渲染恢复增量审查

独立审查者：`/root/title_render_recovery_review`。范围为本轮 ANI 保留、分类刷新、提示借用缓存防护及其最小补救；依照 `reviewing-work/references/code.md`，读取 TaskContract、Spec、修订说明、实际完整插件/策略/测试及原生 dump。未修改生产代码、未构建或部署。

最终批准身份：

- `title_book_mirror.c` SHA256：`27E710012A1FC78EEED16A86AA90AFC4AF0E6CCFDA14DF19D56EF9E1E1DA39C8`。
- `title_book_policy.h` SHA256：`A9CC02813391A443662C42B84E7129BB07F2D0085412D3915B6991A4A2146FC2`。
- `test_title_book_policy.c` SHA256：`EB6673F1069E2591450FFCD99814321107185C0CDA44A7AFADD4AC11DAE53D23`。

## 原生路径核对

- **ClientStatic**：dump base `00400000`，`VA0257AB03` 调用 `VA02579230`，返回地址为 `VA0257AB08`，与报告的 RVA0217AB08 一致。提示函数 ECX 为 self、两个栈参数、`ret8`；fastcall 空 EDX 桥匹配。`+2B8` 为缓存对象，`+2BC` 为字节所有权标记；state3 于 `02579441..57` 读取 actor 装备13。`025793A9..BF` 确实在所有权判断之前解引用旧缓存。新入口只比较缓存地址与当前权威对象，失配清借用缓存，既不解引用旧对象也不销毁实体；拥有预览交原生管理。非自身视图/未启用仍委托原函数。
- **ClientStatic / CurrentImplementation**：`02576560` 初始化 `cache+18` 静态帧和 `cache+8` 展示 ANI；setter 的 `02576914..6C` 管理真正解锁效果 `cache+10`。同展示 ID 的正常佩戴刷新不再清 ANI；缺静态帧仍可调用原生资源函数。setter及资源函数均为 cdecl。
- **CurrentImplementation**：分类 mask 始终包括收到分类，绑定改变时补旧/新来源分类。来源策略仍要求分类、索引、原件ID、实际佩戴ID均匹配；非来源重复ID归一化为可用。原生重建调用 setter 的路径保留，没有新增全分类二次覆写。state1 的原生解锁阶段仍由原生完成并随后重建，需在实机验收中覆盖解锁与佩戴相邻发生的瞬态。

## 初轮发现与补救复审

初轮目标 SHA256=`F55AB6A7A6882C62190E419EA3383151AEA7EBAE10795CF2DB4DFE290AE84D16` 存在 **P2：展示 ID 实际更换时旧 ANI 可残留**。当同一行由 A 变 B、状态保持2/3时，原生 setter 不执行旧2→0的资源清理；原生资源初始化在 `02576643..46` 的新 ANI 列表为空路径，或 `02576669..6B` 的路径不匹配分支直接返回，不能保证释放 A 的 `cache+8`。0166重建物品容器仍保留行 cache；配套 `TitleBookDomainService.StoreAsync:158..165` 支持未佩戴书槽的原子实体交换，不能依赖每次 ID 更换一定经过空行。影响既有真实称号移动/资源正确性。此为静态代码路径发现，未声称在当前实机重现。

最终代码已关闭此发现：只有旧/新展示 ID 均为正且不同，并且原生 setter 已成功写入目标 state 和展示 ID，才把空引用通过 `VA028894B0` 移入 `cache+8`，随后初始化新资源。独立反汇编确认该函数 ECX 为目的引用、一个栈参数、`ret4`，清源并递减原控制块强/弱引用；调用桥及空两字引用符合原生语义。同 ID、初始 -1 ID、移出0 ID不触发这项额外释放；没有触碰 `cache+10`。

## 验证及结论边界

独立运行当前 Release 策略测试通过，新增断言覆盖借用缓存失效、拥有预览保留、分类 mask、同ID/不同ID资源决策。独立运行签名检查通过：21处dump目标与1处独立实启Flush签名；新引用管理函数也在全部目标先校验的集合内。读取最新 CTest 日志为该策略测试通过；Release构建由执行者报告，本审查未重复构建。

本增量没有未解决的阻断缺陷，允许控制器进入完成验证及既有授权范围的测试部署。以上为 ClientStatic 与 CurrentImplementation，不能视为 ClientVerified，也不能凭返回地址断言已经精确证明当次崩溃释放对象。实机仍应验证同页替换/取消、悬浮旧佩戴称号切换、跨分类、各 ANI、真实物品ID更换及开关窗口。
