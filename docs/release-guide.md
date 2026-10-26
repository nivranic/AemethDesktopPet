# 发布与清理系统集成

这些工具用于普通 Windows 桌面应用的可审查发布与退出管理。它们不修改 360 或其他安全软件，不自动提交样本，不创建计划任务，不开启自启。

## 发布包

在插件源码根目录使用 **PowerShell 7**。先退出桌宠，完成构建及项目检查，再指定一个尚不存在的绝对输出目录：

```powershell
pwsh -NoProfile -File .\native\build.ps1
node --test .\tests\node\*.test.cjs
pwsh -NoProfile -File .\scripts\package-release.ps1 -OutputDirectory "<新发布目录的绝对路径>"
```

脚本可离线运行，不需要 Git，也不运行 EXE。它核对 `package.json`、插件清单、EXE 的产品版本和文件版本；例如产品版本 `0.3.3` 对应文件版本 `0.3.3.0`。不一致时停止，不能用旧 EXE 冒充新版本。签名必须在打包前完成，签名后的文件哈希才是发布哈希。

每次生成以下内容：

- `portable/zcode-pet-plugin/`：便携插件，双击 `bin/ZCodePet.exe` 可启动。
- `marketplace/marketplace.json` 与 `marketplace/zcode-pet-plugin/`：使用相对路径的本地市场。
- 两个带版本号的 ZIP，与上述两种目录布局对应，保留 `.zcode-plugin` 隐藏目录。
- `release-info.json`：版本、样本 SHA-256、实际 Authenticode 状态和文件白名单。
- `SHA256SUMS.txt`：所有产物文件的 SHA-256（校验清单自身除外）。

运行包只包括 EXE、角色许可与 NOTICE、角色描述与两种现有精灵图、三个 Node 运行脚本、插件清单、hooks、插件技能、包信息、主许可和 README。`PRIVACY.md`、`THIRD-PARTY-NOTICES.md` 存在时一并收录。测试、源码、日志、本机验证记录、旧保活脚本和维护脚本均不进入运行包。打包前检查文本中是否混入盘符绝对路径、UNC 或用户目录路径；发现则拒绝打包。此检查不能代替对发布文档和素材的人工审查。

输出目录必须为新目录，不允许写入插件内部、已有发布目录或备份目录，不接受目录联接/符号链接。失败时保留已写内容供排查；该目录不是有效发布包，应修复原因后选择另一个新目录重试。发布者需检查 ZIP 文件清单与哈希，再对外分发。

`NotSigned` 表示没有 Authenticode 签名；填写产品名称或发布者字符串不会建立身份信任。即使状态为 `Valid`，也仅表示本机签名校验结果，不等于安全软件认可、商标许可或无恶意行为证明。脚本不上传、不下载、不调用远程签名服务；本机 Authenticode 校验由 Windows 提供。

## 清理系统集成

先通过桌宠菜单退出，并在 ZCode 中禁用/卸载插件。下列脚本只负责删除用户自己曾注册的 Windows 集成，不停止进程，不修改 ZCode 配置，不删除源码或 `%LOCALAPPDATA%\ZCodePixelPet` 用户数据。

```powershell
# 只读取和预览，WhatIf 不创建备份目录、不删除任何项。
pwsh -NoProfile -File .\scripts\remove-integration.ps1 -BackupDirectory "<新备份目录的绝对路径>" -WhatIf

# 确认预览结果后执行；仍会核对归属并先备份。
pwsh -NoProfile -File .\scripts\remove-integration.ps1 -BackupDirectory "<新备份目录的绝对路径>"
```

若本次仅清理旧保活任务、保留用户当前的登录启动选择，在以上预览和执行命令中均追加 `-KeepLoginEntry`。此模式不读取、不改写也不删除登录项，只核对和清理旧任务。

仅接受当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `ZCodePixelPet` 字符串值，且命令必须精确指向当前插件 `bin/ZCodePet.exe`，参数为空或仅 `--wait-host`。旧任务必须位于任务计划程序根目录，名称为 `ZCodePetKeepAlive`，只有一个动作，动作精确指向当前插件 `scripts/keepalive.cmd`，不带参数。其他路径、附加动作或未知参数都会终止操作，不会扩大删除范围。

脚本先导出完整任务 XML 和登录项原值/类型到新备份目录，再逐项重查并删除、读回确认。访问被拒绝、配置变化或删除失败会报错；已完成的删除不会自动恢复，因此部分失败时应依据备份检查剩余项。备份可能包含本机路径，仅保存在本地，不应进入发布包。通常使用原注册用户即可清理 HKCU 项；任务的读取/删除权限取决于原任务 ACL，脚本不会自动提权。遇到权限问题应核对任务归属与权限，不能误删其他用户或其他安装目录的任务。

清理只撤销系统集成，不等于完成 ZCode 卸载。最后应在 ZCode 中检查注册来源、插件状态，并确认桌宠已退出。恢复时参考备份中的登录项原值；不建议重新导入旧保活任务，也不要因为回滚程序而恢复周期性拉起行为。

## 公开分发边界

根目录 MIT 许可覆盖本项目代码；素材沿用 `assets/aemeath-mini/LICENSE` 与 `NOTICE.md` 所记录的上游许可和来源，两者必须随包保留。源项目 MIT 声明不自动授予角色、游戏名称、商标或原始美术的全部权利。公开分发前需核对素材授权范围；需要可广泛分发的品牌/商业版本时，应使用自行创作或取得明确授权的角色资源，并如实更新 NOTICE。不得把本地运行成功、安全软件暂未报警或代码签名当成素材权利核验。
