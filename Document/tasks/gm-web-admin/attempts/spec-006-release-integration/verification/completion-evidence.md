---
kind: rks-verification
decision: pass
---

# Spec006 完整新鲜验收验证

独立当前approved报告CF1EF45F；Spec A83EC50E、完整7产品manifest 0CDCB36B及7/7实际文件匹配。HEAD仍aa5b663672c1beaac82484e57c3bbbeff5429d44。审查F-001已修复并经决定性RED/GREEN及毒化环境真实原生候选复核。

| 契约条件 | 当前决定性结果 |
|---|---|
| SC-1 / AC-001,002 | 当前CI顺序npm ci→54项前端→TypeScript/Vite→原.NET，Windows/Linux矩阵和全部Action40位SHA保留；新鲜标准package重新npm ci/build和win-x64 self-contained publish，HTTP全部3资源SHA/MIME、initialized=false、私有401通过，自己进程退出/临时目录清理。 |
| SC-2 / AC-002,004 | 新鲜完整.NET8项目5373通过/0失败/2显式skip（档案存在时的missing-PVF分支）；实际PVF显式只读SHA仍3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06。构建0警告/0错误，Python11项通过含原5历史守卫及隔离别名回归，前端54/54及TS/Vite exit0。 |
| SC-3 / AC-003 | 当前独立完整结果/修复重审approved，GM01..21有操作/预期/证据和A21待验；新鲜zip包含当前修正文案，全部8份说明相对链接存在，7份源文档逐字节匹配，README为包内索引。 |

当前新鲜候选53211094字节，SHA256 5d14b17a2d917a84ac7d7779d7efdda561e0000e49d78210c53b484ade461cb1；校验文件一致、CRC通过；元数据3资产与当前dist/zip身份相同，默认GM/Launcher/Game全false，无数据库/PVF/日志/env或Node。该未提交候选release.commit仍aa5b基线；根收尾从干净接受HEAD再生成最终可交付包，不回填本结果ownSHA。

详细日志：artifacts/gm-tests/spec006/fresh-build.log、fresh-full-tests.log、fresh-test-counts.json、fresh-frontend-tests.log、fresh-frontend-build.log、fresh-release-tests.log、fresh-package.log、fresh-identity-ci.json、package-isolation-proof.json。两个中间计数提取断言因日志中文标签用“通过数”而非“通过的”失败，已正确解析八项目原始日志，不是测试失败；未重跑或掩盖服务端测试。

Python缺资源/环境别名RED及raw工具日志保留ignored artifacts，产品/MD/YAML完整内容将接受。历史内置浏览器crash原因未确认，实际本机Chrome邮件预览/三币单批/正常刷新记录已验；真实A21到账/邮件体验/在线提醒留给用户。Linux/远程CI未执行。source main 5fbe2dad且clean；现服/真实玩家/管理员/客户端未切换或发放。全部changed路径均为7产品或根声明durable Document/tasks/gm-web-admin。
