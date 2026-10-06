# 马战协议、模拟时间线与HTML说明页

用户要求的顺序已执行：①查询全部已定位协议；②重建可能的模拟包时间线；③根据模拟结果生成HTML，解释打开、UI更新、状态迁移、投注和比赛流程。

## 打开

用本机Chrome打开 [index.html](index.html)。这是UTF-8单文件，样式、协议JSON、时间线和纯模型均已嵌入；不需要启动服务、联网、安装依赖或读PVF。页面不连接GM端口、不发真实游戏包、不读写生产数据库。

- **流程演练**：16场景、137步骤，可下一步/上一步/自动播放/拖动进度/点选时间线；同时显示UI结构示意、管理器state、scene、counter、自动CMD、公告/弹窗、服务端账本与前后差异。
- **协议清单**：11方向条目，按方向/协议/字段搜索，展开载荷、长度、RVA/VA、副作用和来源。
- **状态与边界**：state与stage矩阵、本地推进链、打开/查询/投注说明、独立scene条件试验与未知项。

骑士A–H、库存100、材料槽7、等级20和固定赛果均为解释用fixture。UI是结构示意，不是原生客户端截图或像素复刻。T+是序号；自动播放900ms是阅读节奏，不是赛程或已证明的原生动画时长。

## 研究结果

- [protocol-catalog.md](protocol-catalog.md) / [protocol-catalog.json](protocol-catalog.json)：3 C2S CMD、3 S2C ACK、4马战NOTI及共享NOTI006C。
- [coverage-review.md](coverage-review.md)：局部注册范围、共享库存/邮件依赖、初始state与首14B边界。
- [opening-review.md](opening-review.md)：ensure/show、打开立即state门、受保护重开路径的未知。
- [timeline.md](timeline.md) / [scenarios.json](scenarios.json)：候选完整闭环与错误/恢复/兼容对照。
- [simulator.mjs](simulator.mjs)：纯模型，页面按此模型产生快照与副作用记录。

“全部协议”指本轮全部已定位的条目和已确认依赖，不是全程序排除证明。原服时序、state5发送时点、任意阶段静默精确恢复仍Unknown。本页是ClientStatic驱动的模拟解释，不能升级为WireCaptured或ClientVerified。

完整候选用每组state4→BB0/1/2使三组动画可达；它仍带多次状态公告，**不是最终上线方案**。正常本地决赛到scene12后，本例不再自动BB3，避免再次冠军公告。兼容marker只抑制B8直接公告，不抑制BB/tick/scene12；无插件时相同字节是负state。

006C步骤只抽象093D map存在，不等价于真实包单个enabled字节。状态初值0是假定已reset；构造和登录时点无动态保证。共享库存和邮件业务单列，未编造它们的马战专属opcode。首14B以??显示，合成body不含frame头、seq、checksum，不能直接发服。

## 验证与复现

在本目录使用已安装Node运行，彼此无输出冲突时仍按下列顺序执行：

```powershell
node verify.mjs
node coverage-model-review-test.mjs
node generate.mjs
node browser-test.mjs
node render-chrome.mjs
```

最后一步使用本机 `C:/Program Files/Google/Chrome/Application/chrome.exe`、独立测试profile、无头渲染。原Chrome配置不被复用；profile、完整DOM与stderr保存在工作树artifacts目录，本目录verification保存截图和结果。没有使用GPT内置浏览器。Windows Chrome的实际窄屏viewport有下限，本次测试实际viewport为1422×1104与662×904，按实际值报告。

- 模型：367断言，覆盖四个BB stage×14个scene、B8/counter、只读查询、进入门、库存版本、marker、字节预览和完整三组推进。
- 独立反例：11项通过；修复了选中骑士预览、marker范围、Stage0未消费冠军槽等问题。
- Chrome：两个实际viewport各110项通过，逐步遍历16场景，检查按钮、进度、筛选、展开和scene试验；未出现横向溢出或替换字符。
- [model-verification.md](model-verification.md)、[coverage-model-review.md](coverage-model-review.md)、[Chrome验证](verification/chrome-verification.md)。

研究模型的测试与原生游戏客户端验收不是同一件事。本任务只新增说明资料，未修改Server/、Web/gm-admin、ClientHack插件、数据库、配置或部署。
