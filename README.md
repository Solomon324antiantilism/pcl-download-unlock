# PCL 下载恢复工具

一个中文界面的 Windows 单文件工具，用于恢复**指定 PCL 构建**中被关闭的 Minecraft 版本下载入口。

**用途：解除 10 月 20 日 PCL2 版本更新后带来的 Minecraft（我的世界）版本下载限制。** 当前自动补丁仅支持下文列出的、已经核对 SHA256 的 PCL 2.99.0.0 构建。

**[下载最新 EXE](https://github.com/Solomon324antiantilism/pcl-download-unlock/releases/latest)** · [审查记录](docs/REVIEW.md) · [第三方许可](THIRD_PARTY_NOTICES.md)

当前版本：**1.0.1**。不需要 Node.js、Python、Visual Studio、旁置 DLL 或联网下载依赖。

> 这不是适用于所有 PCL 版本的通用补丁。工具会核对原始文件 SHA256；不匹配时只显示检查结果，不进行修改。本项目是独立工具，不代表 PCL、Mojang 或 Microsoft。

## 有什么用途

- 自动查找常见目录里的 PCL，也可以手动选择文件或搜索文件夹。
- 显示程序的实际版本与支持状态，而不是依赖文件夹名称判断。
- 为已验证的构建恢复版本列表加载器和 Minecraft 版本选择卡片。
- 修改前生成副本并校验，创建原始备份，再替换目标文件。
- 校验备份后还原，识别旧流程的相邻备份；文件已被更新或另行修改时拒绝覆盖。

它不修改整合包安装、账号登录、游戏所有权验证、主题或自动更新设置。恢复下载入口不等于已经验证实际 Minecraft 下载成功。

## 下载与使用

1. 在 [v1.0.1 Release](https://github.com/Solomon324antiantilism/pcl-download-unlock/releases/tag/v1.0.1) 下载 [PCL-Download-Unlock-v1.0.1.exe](https://github.com/Solomon324antiantilism/pcl-download-unlock/releases/download/v1.0.1/PCL-Download-Unlock-v1.0.1.exe)。界面仍为中文；普通用户无需下载源码或 Markdown 附件。使用同页的 `SHA256SUMS.txt` 核对文件；标为 `LEGACY` 的旧清单使用了发布前的中文文件名，不应再用于当前附件。
2. 关闭要修改的 PCL 启动器；不需要强制结束正在运行的 Minecraft 游戏。
3. 双击工具。自动查找后选择目标，或点击“选择 PCL 文件”；多个候选不会默认选中目标。
4. 确认路径和版本，点击“恢复下载”，等待完成。工具会先备份、校验，再替换。
5. 打开 PCL，进入“下载”页面测试。要撤销修改，关闭 PCL 后再次运行工具，选择同一安装并点击“还原备份”。

备份保存在 PCL 同一目录，名称为 `原EXE名.before-download-unlock-日期-标识.bak`。请保留它；不要将其他程序的备份手动改名后套用。工具不会覆盖既有备份或强制结束启动器。

附件命名：`PCL-Download-Unlock-v1.0.1.exe` 是可运行工具，`pcl-download-unlock-v1.0.1-source.zip` 是含固定依赖的源码包，`SHA256SUMS.txt` 是按实际附件名生成的校验清单。GitHub 自动生成的 `Source code (zip/tar.gz)` 为标签快照。EXE 的 SHA256 应为 `AEB87A6611E607E78CCFAE3813A8AB13445A26E845444C3AE18A051D2EEBC566`。

可在 PowerShell 中执行以下命令，将结果与 `SHA256SUMS.txt` 中对应文件的一行比较：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\PCL-Download-Unlock-v1.0.1.exe
```

## 系统要求

| 项目 | 要求或验证状态 |
|---|---|
| 系统 | Windows 10 / Windows 11 |
| 运行库 | .NET Framework 4.8+；Windows 10 1903+、Windows 11 已预装 |
| 架构 | x86 单文件，可在普通 x86/x64 Windows 运行 |
| ARM64 | Windows 11 的 x86 仿真为预期兼容，尚未实测 |
| 联网 | 工具本身不发起网络请求；Minecraft 下载本身仍需要网络 |
| 权限 | 默认普通用户权限，不自动提权；需要对目标目录有写入权限 |

已在当前 Windows x64 环境执行 x86 程序和测试。没有逐一实测所有 Windows 10/11 版本，因此不承诺“所有 Windows 都可用”。[微软运行库要求](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements)

## 支持的 PCL 构建

仅支持原始 SHA256 为 `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` 的 PCL 2.99.0.0。已验证修改产物 SHA256 为 `D8F095ACBD7ADDE4E1E26C2A0FEE0FB58E359E9630A842DAC16D43DF73CD62B8`。其他构建只检查，不修改。

出现“不支持此构建”通常表示文件来自不同发行、被更新或已经另行修改。不要删除哈希检查来强行套用补丁。同版本号也可能对应不同文件。

`RestrictMcDownload` 是编译期常量，只修改 EXE 元数据中的 `True/False` 不足以恢复功能。本工具同时恢复已核对的相关 IL 路径，并对比其余方法 IL、字段常量与嵌入资源。

## 备份、记录与隐私

操作记录位于 `%LOCALAPPDATA%\PCLDownloadUnlock\Transactions`；保留相邻备份即可在下一次运行中还原，也兼容旧流程留下的 `.before-download-unlock-*.bak`，无需原聊天或旧清单。

该目录包含操作清单和准备阶段的本地副本，不会自动上传。提交问题时请只提供必要信息，并隐藏本地用户名、目录路径等个人信息。本仓库和公开 CI 不包含用户日志、账号凭据、PCL 原始/修改后程序或 Minecraft 游戏文件。

## 从源码构建

仓库包含全部源码、构建脚本、dnlib 4.5.0 的 net45 DLL 与 MIT 许可。需要 Windows 自带的 .NET Framework 4.x C# 编译器，无需安装 Visual Studio 或联网还原依赖。

在允许执行本地脚本的 PowerShell 中：

```powershell
.\build.ps1
```

产物为 `dist\PCL下载恢复工具.exe`。可指定输出路径：

```powershell
.\build.ps1 -OutputPath 'C:\MyOutput\PCL下载恢复工具.exe'
```

dnlib DLL 的 SHA256 为 `97EF5328D1D1703924C48D9CD1D45B7227FE3836E116DCDED623B6A0DBD52C23`，脚本会先核对它。编译器本身会写入构建时间和模块标识，因此这里的“可复现构建”指可离线从源码生成同等功能程序，不保证 EXE 逐字节一致。

## 测试

不需要 PCL 文件的公开冒烟测试：

```powershell
.\tests\run-smoke-tests.ps1
```

GitHub Actions 在 Windows 上执行上述离线构建和 10 项冒烟检查，生成单文件构建产物。实际 CI 状态以仓库 Actions 页面为准。

给测试脚本提供匹配上述原始哈希的本地 PCL 文件。测试仅复制它到独立工作目录后操作，不修改输入文件。

```powershell
.\tests\run-tests.ps1 -OriginalPcl 'C:\PCL\Plain Craft Launcher 2.exe'
```

审查后的完整测试共 22 项，覆盖修改和精确还原、旧备份、重复执行、未知构建、文件外部改动、备份损坏及替换前变化、损坏历史记录、只读目标、文件占用、多安装、取消搜索，以及中文名称和改名后的运行中进程识别。64 位系统上以 x64 隔离进程验证 x86 工具的跨位数检测。

该工具只校验及恢复下载入口；文件修改成功不等于 Minecraft 版本列表或实际下载已验证成功。

## 许可证

本工具的原创代码采用 [MIT](LICENSE) 许可证。dnlib 保留其原始 MIT 许可，见 [第三方声明](THIRD_PARTY_NOTICES.md)。本仓库不分发 PCL 或 Minecraft 二进制文件。
