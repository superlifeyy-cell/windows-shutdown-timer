# 定时关机 / Windows Shutdown Timer

一个简洁的 Windows 定时关机工具：蓝白大字界面、托盘倒计时、关机前提醒，以及单文件自动安装。

**[下载最新版](https://github.com/superlifeyy-cell/windows-shutdown-timer/releases/latest)**

下载安装 `定时关机.exe`，双击后自动准备程序文件、创建桌面快捷方式并打开界面。普通用户无需编译源码，也无需手动放置 HTA、脚本和图标。

![主界面](docs/main-window.png)

## 功能

- 快速设定 30 分钟、1 小时、1.5 小时、2 小时、3 小时；也可输入自定义整数分钟数。
- 显示剩余时间和预计关机时间；成功设置约 2 秒后自动隐藏到系统托盘。
- 双击托盘图标或桌面快捷方式恢复界面。
- 通常在剩余 5 分钟、1 分钟时弹出居中、置顶的提醒窗口，可直接取消关机；1 分钟短计划在剩余约 30 秒时提醒。
- 提醒约 20 秒后自动收起，未选择时保留计划。目前提醒窗口没有专门的提示音。
- 点击“×”时，有活动计划先确认；确认取消后退出，选择继续则保留倒计时并隐藏。没有活动计划时直接退出。
- 界面、共享脚本和图标内置于 EXE；重复运行校验并修复配套文件，避免重复启动托盘程序。

## 安装与更新

1. 从 [Releases](https://github.com/superlifeyy-cell/windows-shutdown-timer/releases) 下载 `定时关机.exe`，或下载只含该 EXE 的压缩包。
2. 双击 EXE。程序按当前用户安装，无需管理员权限，桌面图标使用 EXE 内置图标。
3. 以后双击桌面“定时关机”即可打开。安装完成后，快捷方式不依赖下载文件所在位置。
4. 更新时运行新版 EXE。安装过程保留正在运行的旧版及其计划；旧版退出后，下次启动启用新版。旧版有活动计划时，“取消并退出”会取消该计划，可在新版重新设置。

运行文件由程序维护在 `%LOCALAPPDATA%\ShutdownTimer\versions`；计划状态保存在 `%APPDATA%\ShutdownTimer\state.ini`。程序没有设置开机启动。

## 使用说明

系统需支持 Windows HTA（`mshta.exe`）和 .NET Framework 4.x。关机仍采用 Windows 自带的延时关机机制，保留系统通知。

**到点后，仍在运行的程序可能被强制关闭，未保存内容可能丢失。请提前保存文档，或在提醒窗口中取消关机。** 工具不会替你保存文件。其他软件修改的系统关机计划可能与本工具显示不同步。

![关机前提醒](docs/shutdown-reminder.png)

## 从源码编译

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

脚本使用系统的 .NET Framework C# 编译器，生成 HTA 和共享脚本，将它们连同图标嵌入 EXE，输出到 `dist`。不依赖本项目开发者的本机路径，也无需额外的 Python 包。

源码位于 `src`，图标位于 `assets`，模拟检查位于 `tests`。检查方法和验证范围见 [验证说明](docs/verification.md)。
