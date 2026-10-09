---
kind: rks-verification
decision: manual_required
---

# 完成验证

本轮可确认的主张：配套方案完成研究、实现、独立审查与自动验证，已部署到本地JP86和用户实际protocol2客户端，供实机验收。不能确认整个窗口行为已修复或ClientVerified。

| 条件 | 当前证据 |
|---|---|
| SC-1 具体方案 | spec/plan，三路ClientStatic研究，ClientHack Document/TitleBookMirror-20261008.md；明确自定义TBM1与原生字段 |
| SC-2 原件/来源 | Application完整3294及最终定向54，含重复ID、UID持久化、取消/更换、原子真实替换/回滚；独立审查approved |
| SC-3 勾选/提示 | 插件构建无警告、策略测试1/1、签名20个dump+1个实启，服务器禁止000E书回填/假0168；窗口待实机 |
| SC-4 拖动/外观 | ClientStatic op6路径，准确book来源guard、Main源在途guard；回归包含旧镜像独立外观保留、真实限时UID/期限迁移；实际拖动待实机 |
| SC-5 失败/等待 | ACK匹配本请求并释放busy、失败清原生pending，原子资格验证先于删除；实际保护函数和快速点击待实机 |
| SC-6 部署 | attempt2/deployment.json：PID45676，bootstrap与GMready，六端口监听同PID；client DLL哈希相同，备份保留，DBquick_check与全表比较通过 |

最终日志：title-mirror-application.trx=3294/3294（新增限时案例前完整套件）；title-mirror-title-final.trx=54/54（包含该增量）；title-mirror-protocol.trx=1397/1397，均0失败/跳过。首次Protocol注册检查曾偶发失败，独立单项及后续完整两次通过，未修改生产注册来掩盖该次失败。初始TDD RED因构建挂起没有观察结果，不能宣称完成RED证据链。

部署记录绝对路径：`E:/Project/DNF/DNF-JP-86-Server/artifacts/title-book-mirror-patch-20261008/attempt2/deployment.json`。运行身份 `0.2.2+title-book-mirror.20261008.9da9b83e`；源指纹由prepared.json/source-manifest.txt记录。Client源码最终hash=B202EC0A3A04A3EA479AAA6DB768348CC93AC5460C1EFF1C4575CD635D3E83D6；Domain最终hash=C9191C5E83C28EE205B64AA59F5283CF2644577D0E0253091D2341DB01B74901。

## 尚未验证

原生0166消费边界、实际flush调用、native ACK尾部与资源生命周期、窗口勾选、重复ID点击和快速切换。操作清单见spec-result.md。插件仅识别合法TBM1后激活，24B无revision/characterId，不声称具备跨会话重放检测或五帧原子性。ReadTrace必须保持关闭，以免提前hook改变目标原语签名。

## 实启修订验证

旧源码/部署hash以上保留为首次交付历史。修订后Client源码SHA256=`8FA45EA161272B00F2C3F767D7ECA695F51E70A2B371F42C5717E9167E634AF8`，实际DLL=`025BF6FEC0AEAAD3995C8EFD50A16F7EF24AEDAB5B466AF14F6182EE70A0ADB2`。独立只读实启样本修正Flush保护入口阶段差异；全部21处仍精确校验。自建PID56796日志确认0200及所有插件初始化完成、游戏线程放行；仅停止该测试进程，未登录角色。当前server PID45676路径正确、GMready。完整恢复证据见`artifacts/title-book-mirror-patch-20261008/startup-recovery/deployment.json`。启动通过不等于游戏内验收通过，decision仍为manual_required。
