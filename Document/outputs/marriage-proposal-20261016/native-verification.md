# 2026-10-16双端求婚测试核对

用户反馈：订婚申请、拒绝、再次申请和接受已操作，并出现订婚成功提示。本记录核对服务端日志与真实数据库，不补写用户未说明的逐端窗口细节。

| 时间（北京时间） | 操作及实际解析值 | 持久结果 |
|---|---|---|
| 11:48:34.300 | 角色1向角色8求婚，action1 | fac69362-ac1f-4ee2-81f0-a60342021fff |
| 11:48:38.634 | 角色8拒绝，target0/action1/outcome1 | 第一份关系Rejected，revision2，response1，双方active0 |
| 11:48:45.064 | 角色1再次向角色8求婚，action1 | f7bdd877-ba98-44f0-bba0-6ce82a415a36 |
| 11:48:49.115 | 角色8接受，target1/action1/outcome0 | 第二份关系Engaged，revision2，response0，双方active1 |

两次求婚均记录CMD03F0 ACK整帧27B及对方NOTI03CD整帧26B；两次答复均记录CMD03F1 ACK整帧28B及对方NOTI03CE整帧27B。发送日志有顺序和目标连接，但不是完整原始字节抓包。

实际为独立账号3与4、角色1与8。数据库正好两份历史记录：一份拒绝、一份有效订婚；有效参与者各占一个名额，没有Pending、重复有效占用或残留拒绝名额，integrity_check为ok。从第一份求婚到核对时无fail、decoder异常或Unhandled Game frame；11:50:33/34仍可见两个连接各自的连接检查回包。日志摘录见[native-roundtrip-20261016.txt](native-roundtrip-20261016.txt)，原始数据库只读快照摘要位于忽略目录artifacts/marriage-proposal-fix-20261016/native-test-state-20261016.json。

证据分层：用户可见操作与成功提示为ClientReported；服务器真实收发解析/通知日志与SQLite记录为RuntimeObserved。与ClientStatic发送链及自动化回归互相印证，本轮求婚/拒绝/接受闭环有支持，但没有完整原始帧，不把所有字段都升级为WireCaptured，也没有逐端截图或窗口细节。Engaged只是订婚；婚礼、完整婚姻UI重登恢复、服务重启后的原生界面和原生退婚/离婚不在本轮实测中。

当前只核对本轮行为并记录证据，不继续婚礼、不变更关系、不重启服务，代码保持待本步讨论后的统一本地结果提交。
