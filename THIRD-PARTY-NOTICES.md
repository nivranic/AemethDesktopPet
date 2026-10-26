# 第三方来源与许可

## 本项目代码

本地独立实现，根目录 `LICENSE` 为 MIT 许可。本项目不是 ZCode、Codex 或角色权利方的官方产品。产品名称仅用于说明集成用途，不表示获得其认证或背书。

## Aemeath Mini 素材

现有素材来源声明见 `assets/aemeath-mini/NOTICE.md`，保留的上游许可见该目录的 `LICENSE`。声明中的来源项目为 <https://gitee.com/lzy-buaa-jdi/ameath>，保留的许可署名为 `sinlatansen`。

PNG 图集及应用 ICO 图标均由现有桌宠素材转换，图标同样受素材相关权利约束。代码的 MIT 许可不会自动授予角色形象、商标或第三方原始美术的权利。现有来源声明不能代替对完整素材权利链的核验；公开发布或商用前应确认所需授权，或替换为自有/明确获授权的素材。

## 架构参考

本次参考了 GitHub 的 VPet 与 Shimeji-Desktop 的启动、托盘、退出和发布组织方式；没有复制其代码或素材进入本项目，因此没有把它们的许可套用于本项目。具体源码链接、固定提交和取舍记录保存在开发资料 `docs/github-desktop-pet-review.md`。

## 运行依赖

Windows 的 .NET Framework 与系统绘图/窗口 API 由操作系统提供；ZCode 插件桥接需要用户安装 Node.js 18 或以上。运行包不重新分发这些运行时，不静默下载安装依赖。
