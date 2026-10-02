---
kind: rks-review
verdict: approved
---

# Spec006 独立结果重审

目标仍为HEAD `aa5b663672c1beaac82484e57c3bbbeff5429d44` 上未提交的7产品结果。Spec SHA256保持 `A83EC50E978A9A89D112A490CFD86474046A6404EE896354EC17146B8A78C3CB`；当前manifest SHA256为 `0CDCB36BB403F027A7A14555D5A03D0A7C16DCF2AD8AFAB1D8DFC541A51FE26E`，已逐项核对实际文件身份。沿用 reviewing-work 的Code与Deliverable判据；本轮仅重审F-001修复、测试及验收表措辞三个变动产品与直接回归。首次完整审查和其余未变产品结论保留于 [change-review-r1.md](change-review-r1.md)。审查者独立于root生产者，未改产品、运行服务或构建，也未执行Git写操作。

**F-001已解决。** 当前 `release.py` 先构造固定隔离值，将配置键按 `__`→`:` 与大小写归一，过滤继承环境的全部同键别名，再写入组件开关、GM临时地址、fresh临时数据库、空 `Pvf:ArchivePath`/`PVF_ARCHIVE_PATH` 及Production环境；全部bootstrap输入继续按归一前缀清除。原Host配置/Persistence/PVF调用路径上的别名竞争和显式档案继承缺口已消除。发布副本、自己的子进程终止及HTTP逐资源SHA/MIME、initialized=false、私有401验证保持不变。

新增行为回归调用真实 `smoke`，仅替换Popen边界并捕获生产函数实际交出的子进程环境，逐项断言同键唯一及固定值，包含冒号、双下划线、不同大小写、PVF、bootstrap与运行PATH保留。不是另一份复制的隔离实现。`red-isolation-aliases.log` 显示旧实现归一后留下 `['true', 'false']` 而失败；`green-isolation-aliases.log` 显示11项通过，原5个真实Git历史守卫仍通过。

`package-isolation-fix.log` 记录原生Windows重新npm ci/build、自包含publish及3资产HTTP、未初始化、私有401成功。生产者报告此次输入包含数据库/监听/档案别名与bootstrap，并未创建指定外部数据库；静态过滤和上述实际子进程环境断言直接支持这些输入被移除。独立只读复核该候选zip：CRC通过，SHA256 `35508040938c53fee040cb6a90b1aa0d852ba2447678b9ed9071538417900014`，3资产与当前dist及manifest字节一致，8文档相对链接全部存在。

验收表准备步骤已改为“填入PVF支持的合法物品ID和数量，先预览检查”，与 `OfficialMailView.tsx` 的物品ID/数量InputNumber一致；GM01..21其余内容保持不变。该措辞修改晚于上面的修复冒烟候选，zip内仍是旧措辞，因此这个zip仅作为F-001原生隔离证据，不是最终交付包；root已确认接受后从干净HEAD重打，并对8份随包文档逐字节核对当前源文件。

当前审查范围无未解决阻塞缺陷。批准不代替随后fresh完整.NET/PVF回归及最终干净候选身份核对；Linux/远程CI未执行、真实A21待验、现服未切换的边界沿用首次报告与当前文档。
