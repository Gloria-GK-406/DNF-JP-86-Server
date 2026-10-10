---
kind: rks-verification
decision: manual_required
---

# 验证记录

本次交付：绿名独立分类与 ×3 经验、个人逐怪击杀账本、NOTI 0x0023 结算投影。

2026-10-10 最终相关测试：Application 208、Protocol 179、Infrastructure 24，合计 411 通过，0 失败、0 跳过。
测试使用 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
报告位置：`E:/Project/DNF/DNF-JP-86-Server/artifacts/named-monster-experience-20261010/tests/*.trx`。

- 分类：A21 三组内置区间边界、DGN 扩展、Layout 传递、Cell 经验资格冻结。
- 经验：类别 × named 叠乘、两次下取整、频道等加成按新 Base 独立计算，保留旧非绿名采样用例。
- 账本：实际发奖 Breakdown、角色隔离、重复提交、失败重试、旧快照不可变。
- 协议：u32 序号/经验、可变长度、同序号后写覆盖的线级形状、翻牌尾部不变、通关奖励不叠加击杀所得。
- 命令构造依赖边界测试通过；未改变数据库、客户端或 PVF。

发布构建、运行产物 hash、进程路径、监听端口、health/bootstrap 与数据库备份由部署目录 `deployment.json` 记录。

## Unverified Conditions

用户需重新登录并新开地下城，验证具名怪列表经验、切房/结算/翻牌与实际经验变化。自动测试和静态客户端分析不替代实机验收。
客户端逐怪字典仅用房间序号；跨房同序号会后写覆盖，服务端内部账本不会合并这两次击杀。
