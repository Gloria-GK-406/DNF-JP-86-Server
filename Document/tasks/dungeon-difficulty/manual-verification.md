# 实机验证

## 环境

使用本任务 Release Host 和源数据库的独立副本 runtime/validation.db。仅在副本中将角色1的蜘蛛洞穴150最高难度设为Adventure；原数据库无难度记录，未修改。此设置是测试准备，不代表实现了通关解锁触发规则。蜘蛛剧情链沿用用户此前完成状态。

## 顺序

1. 登录原85级角色，打开蜘蛛洞穴，确认普通、冒险可选；先选择普通进入，停在首房。
2. 助手核对0010 offset4=0、001C difficulty0和实例日志Normal后，用户回城选择冒险进入，停在首房。
3. 核对0010 offset4=1、001C difficulty1和实例日志Adventure；用户清理首房并换房，核对房间创建链不改变本局难度。
4. 正常退出角色后重新登录，确认权限仍为普通、冒险。

每次只向用户给当前一步。上述步骤已完成；结果见 live/normal-entry.md、adventure-entry.md、adventure-move.md、relogin-permissions.md。
