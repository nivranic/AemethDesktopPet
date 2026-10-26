---
name: pixel-pet
description: 显示、隐藏和配置本地 Aemeath Mini 像素桌宠，检查或临时演示动画状态。
---

使用本插件 zcode-pet MCP 工具：pet_show、pet_hide、pet_status、pet_configure、pet_set_state、pet_quit。

- 用户要求显示时调用 pet_show；隐藏时调用 pet_hide；退出宠物时调用 pet_quit。
- 0.3.2 起 hooks 只连接已运行的桌宠，不会自动重生。首次使用可双击 bin/ZCodePet.exe；需要独立于宿主进程生命周期时，通过桌面或用户选择的登录启动项运行。
- 设置 speed 在0.5到2之间，scale在0.5到2之间。调速改变现有帧的停留时间，不会增加中间帧。
- 默认状态由 hooks 接入，等待权限/AskUserQuestion→waiting，工作→running，工具失败→failed，正常 Stop→review。
- pet_set_state 是短时演示，不能当作真实任务进展。
- ZCode 当前 hooks 没有覆盖所有网络错误、鉴权失败和手动取消，不能宣称所有任务都已实时跟踪。
- 不发送问题正文、工具参数或回答给宠物；不要代替用户处理审批。
- 此插件目前支持 Windows，使用已打包的独立原生窗口，不修改 ZCode 安装包。
- 0.3.5 起桌宠右键菜单与托盘菜单含“重启资源管理器”（全局热键 Ctrl+Alt+R 同效），用于任务栏/资源管理器假死时的用户自救；该操作在桌宠进程内完成，不经 MCP 工具触发。
