---
kind: rks-verification
decision: pass
---

# 当前Windows候选隔离自检

本机标准package重新npm ci/build、win-x64自包含发布、HTTP冒烟exit0，日志artifacts/gm-tests/spec006/package-isolation-fix.log。审查F-001修复后，将Database:Path、Networking:Enabled、LauncherAuth:Enabled、GmWeb:ListenUrl和Pvf__ArchivePath等冲突配置及测试bootstrap作为继承环境输入；全部别名被隔离覆盖。不存在的外部PVF未被加载，must-not-create.db没有产生；fresh库与临时GM端口完成3资源SHA/MIME、initialized=false及私有401，子进程退出、临时目录清理成功。11项Python回归含原5历史守卫通过，新增smoke真实子进程环境回归的RED/GREEN保留。

该阶段zip SHA256 35508040938c53fee040cb6a90b1aa0d852ba2447678b9ed9071538417900014，53211067字节；校验文件/CRC、3资源manifest与当前dist一致，默认三开关关闭，无DB/PVF/log/env/Node，8份文档相对链接完整。独立审查指出该zip内验收表仍为措辞修正前版本：它仅证明原生隔离自测，不是最终交付包。

当前候选release.commit仍基线aa5b6636；根收尾将从干净接受HEAD重打，并额外核对全部随包文档与当前源文件字节一致。Linux/远程CI、A21实机未执行，保持最终清单待验。
