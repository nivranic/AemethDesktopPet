# GitHub 桌宠对照与本次整改

日期：2026-10-16。以下基于固定提交的源码抽查，不是完整安全审计，也不能证明任何项目一定不会被杀毒软件标注。本次停止申诉流程，以代码与软件规范整改为交付内容。

| 项目及固定提交 | 源码依据 | 本项目采用的做法 |
|---|---|---|
| VPet `06489f13cd9d2d8de38e235d58b21ef6bc6641ed` | [托盘菜单](https://github.com/LorisYounger/VPet/blob/06489f13cd9d2d8de38e235d58b21ef6bc6641ed/VPet-Simulator.Windows/MainWindow.cs#L2282-L2342)、[退出清理](https://github.com/LorisYounger/VPet/blob/06489f13cd9d2d8de38e235d58b21ef6bc6641ed/VPet-Simulator.Windows/MainWindow.xaml.cs#L522-L580) | 保留明确的显示/隐藏/退出入口；隐藏停动画计时器；退出释放托盘、菜单和位图资源。 |
| VPet，同上 | [登录自启设置](https://github.com/LorisYounger/VPet/blob/06489f13cd9d2d8de38e235d58b21ef6bc6641ed/VPet-Simulator.Windows/WinDesign/winGameSetting.xaml.cs#L1510-L1564) | 采用可见、可撤销、当前用户范围的自启开关。VPet 使用 Startup 快捷方式；本项目保留已有 HKCU Run 集成以避免双重启动，不复制权限失败后管理员重试逻辑。 |
| VPet，同上 | [代码许可](https://github.com/LorisYounger/VPet/blob/06489f13cd9d2d8de38e235d58b21ef6bc6641ed/LICENSE)、[动画授权](https://github.com/LorisYounger/VPet/blob/06489f13cd9d2d8de38e235d58b21ef6bc6641ed/README.md#L134-L163) | 明确代码许可和角色素材授权是不同事项；为由素材生成的图标保留同样的来源声明。 |
| Shimeji-Desktop `49c9268fdff1b2070f425f5388a9a13f7b2c06b2` | [EXE/单实例/版本配置](https://github.com/DalekCraft2/Shimeji-Desktop/blob/49c9268fdff1b2070f425f5388a9a13f7b2c06b2/pom.xml#L235-L272)、[ZIP 组织](https://github.com/DalekCraft2/Shimeji-Desktop/blob/49c9268fdff1b2070f425f5388a9a13f7b2c06b2/assembly.xml#L5-L32) | 普通 GUI EXE、清楚的版本信息、单实例、白名单 ZIP、依赖说明及 LICENSE；不因其使用 Java 就改换当前技术栈。 |
| Shimeji-Desktop，同上 | [托盘与备用控制窗](https://github.com/DalekCraft2/Shimeji-Desktop/blob/49c9268fdff1b2070f425f5388a9a13f7b2c06b2/src/main/java/com/group_finity/mascot/TrayMenu.java#L49-L64)、[退出](https://github.com/DalekCraft2/Shimeji-Desktop/blob/49c9268fdff1b2070f425f5388a9a13f7b2c06b2/src/main/java/com/group_finity/mascot/Main.java#L905-L911) | 用户必须保有控制入口；本项目保留托盘、重复双击唤出以及 MCP 显示/退出。 |

VPet 同时有网络与遥测功能，本项目没有此需求，因此保持离线，不照搬。Shijima-Qt 已归档，不将其称为当前活跃项目。

## 本地代码发现和修复范围

1. 管道名占用时原实现会无退避创建重试：改为首次同步绑定、失败显式返回，运行期绑定失败终止服务并关闭程序。
2. 隐藏后仍保持 15ms 动画轮询：改为隐藏停止、显示按帧边界调度，鼠标观察单独采用适度轮询，减少动态降低频率。
3. 托盘误释放 Atlas 缓存位图：修正所有权，显式释放自有托盘菜单和原生图标。
4. 管道只允许用户 SID 但未拒绝 Network SID：加入显式拒绝网络登录，保留当前用户访问。
5. 退出/隐藏偏好依赖调用方：原生命令也保存偏好；外部清理测试使用精确 PID 终止专门启动的实例。
6. 用户无法在应用内关闭自启、缺少隐私告知：增加登录启动开关与关于/隐私入口。
7. 整目录打包夹带历史证据：新增白名单发布与自身集成移除脚本、来源许可和隐私文件。

现有普通用户清单、本地命名管道、离线逻辑均保留。Windows 绘图 API、未签名和系统 Framework 编译器本身不是恶意证据；不做改名躲避检测、混淆、加壳或反复更换哈希。

## 验证边界

按模型/Node 测试、原生绘制及 IPC、自启选项读写、显示隐藏与外部终止后不复活、发布包内容检查分别记录。本地功能通过不能替代 360 重新判定，也不等于远程登录边界已做端到端验证。用户此前确认 0.3.2 仍被标注；新版本结果须实际观察。
