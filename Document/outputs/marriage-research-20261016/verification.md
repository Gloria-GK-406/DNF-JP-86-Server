---
kind: rks-verification
decision: pass
---

# 一期信息探查交付验证

本次 `pass` 只表示三路信息探查交付完整、主要结论可追溯、修改没有越过资料范围；不表示婚姻功能实现或客户端验收通过。

## 核验结果

| 条件 | 结果与证据 |
|---|---|
| 三路报告及本地引用完整 | PVF、A21、参考实现均交付；综合报告和专项报告的相对链接目标全部存在 |
| 工件身份可信 | 主控重新计算服务端 PVF、A21 dump、70 二进制 SHA-256，与各报告一致 |
| PVF 证据未漂移 | 主控逐项核对 [20 项 manifest](pvf/evidence-manifest.sha256)，全部匹配 |
| 脚本可解析 | 5 个 Python 脚本 AST 检查通过；扫描器由子代理独立重建，0 警告/0 错误，扫描复跑 hash 一致 |
| A21/70 证据可复现 | 主控复跑完整反汇编、窄摘录和 70 扫描，前后 hash/输出一致 |
| 关键结论交叉核对 | 主控核对 14B 入站头、03F0/1 构包指令、完整接任务路径；参考报告中错误的“14B 名字区”已撤销 |
| 资料库入库边界 | 四条协议的人工区和 catalog 已更新；对比调查前 HEAD，四个 machine 块和 03D8 的 a12 块逐字不变 |
| 生产范围不变 | `git diff --name-only -- Server ClientHack Tool` 为空；本轮没有执行数据库/PVF/客户端写入或服务操作 |

结构化结果见 [verification-results.json](verification-results.json)。原始反汇编及扫描 TSV 保留输出中的空格；文档/脚本与协议人工区执行 whitespace 检查，原始数据文件不以行末空格判断证据有效性。

## 全仓检查的既有失败

运行：`python protocol/patterns/data/check-server-documentation-sync.py --repo-root .`。

结果：exit 1，14 项覆盖缺口，包括祭坛回包的 wire override、五条 C2S 和八条 S2C catalog identity。没有本轮婚姻新实现或注册。

在生产源码、override 和协议机器块不变的前提下，将检查器读取的 catalog **仅在内存中替换为调查前 HEAD 版本**复跑，exit code 和完整输出逐字一致。因此确认这些失败不是本轮新增。未修改无关覆盖记录来制造全仓通过。

- [本轮检查输出](documentation-sync-current.txt)
- [基线 catalog 对照输出](documentation-sync-baseline-catalog.txt)

## 未进行的验收

没有启动婚姻双客户端流程，没有新增正常婚姻实帧，没有实现或部署婚姻功能。原生入口、业务字段映射、仪式和重登仍按主报告列出的 Unknown 跟踪。生产源码没有改变，因此没有为本轮资料交付重跑整套服务端测试。
