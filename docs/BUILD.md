# 构建

## Windows 桌面入口

需要 Windows x64、PowerShell 7 和包含 C# 编译器的 .NET Framework 4.x。默认查找 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`。

```powershell
.\Build.ps1
.\Build.ps1 -OutDir D:\build\replay
.\Test.ps1
```

源码入口在 `src/ReplayLauncher.cs`，其余 C# 文件共同编译到一个 WinForms EXE。构建不依赖开发者本机的工作目录，也不读取个人 settings.json。

## 修改与重编译 MQL5

`assets/` 中含本项目的 `.mq5` / `.mqh` 源码与已验收的 `.ex5`。MetaQuotes 标准头文件由自己的 MT5 安装提供；不会随项目复制。

```powershell
.\BuildNative.ps1 -MetaEditor 'C:\Your MT5\metaeditor64.exe' -MT5DataFolder 'C:\Your MT5 Data'
```

脚本在 `.build/native/` 中编译三个 MQ5，要求日志出现 `0 errors, 0 warnings`。编译成功后将新 EX5 复制到仓库 `assets/`，再执行 `Build.ps1`。该步骤会更新开发副本中的原生二进制。

桌面程序启动回放时会展开本项目的 MQH 源码并复制到所选 MT5 数据目录；启用外部自定义指标时使用该安装的 MetaEditor 编译。

## 可复用测试

`Test.ps1` 编译并运行 `CheckFinishStoreV125` 与 `CheckObservationV126`。测试使用独立合成会话和临时目录，不会启动 MT5。测试输出留在 `.build/tests/`，可以通过 `-WorkDir` 指定新目录。

原有891项检查包含更多历史回归与本机 MT5 场景，它们不是本脚本的计数。请按 `docs/VALIDATION.md` 的场景在目标 MT5 环境补做验收。
