# 货代提单节点提醒器 · ForwarderAssistant

以**提单号**为主键的 Windows 10/11 桌面常驻小工具：管理每张提单上的关键时间节点（截关 / 开船 / 到港 / 免箱期…），到点用**自绘右下角弹窗**提醒，避免漏截关、漏还箱。绿色便携、双击即跑、数据随 exe 走。

## 特性

- **提单 + 船期（船名/航次）+ 节点** 三层模型；同一船期被多张提单共享，改动天生联动、零同步代码。
- **五态状态机**（仅记录 / 待办 / 需要处理 / 超时 / 已处理）：除「已处理」需人工确认外，其余状态随时间自动重算。
- **两种节点粒度**：精确到分 与 仅日期（当天 = 需要处理，次日 = 超时，按本地日历日判定）。
- **30 秒心跳调度 + 自绘弹窗提醒**：不依赖系统通知设置（本机系统通知总开关常被关，原生气泡不可靠）。
- **托盘常驻、单实例、开机自启**（可选，写 HKCU Run）。
- **Excel（.xlsx）批量导入**提单；**双击提单号**即复制到剪贴板。
- **绿色便携**：数据优先放 exe 同级 `data\`，该目录不可写时退回 `%LOCALAPPDATA%\ForwarderAssistant`。

## 技术栈

- C# / .NET 8（`net8.0-windows10.0.17763.0`，x64）
- WPF + WPF-UI（lepoco）
- SQLite（`Microsoft.Data.Sqlite`，SQL 手写、无 ORM）
- 依赖：`CommunityToolkit.Mvvm` · `WPF-UI` · `Microsoft.Data.Sqlite` · `H.NotifyIcon.Wpf`（2.3.2）· `ClosedXML`
- 发布：**self-contained 单文件 exe**（约 72–76 MB），免装运行库、双击即跑

## 构建与运行

```powershell
dotnet build -c Debug
dotnet publish -c Release -o dist
```

双击 `dist\ForwarderAssistant.exe`。首次运行会在其旁生成 `data\forwarder.db`。

> 重新发布前先关掉正在运行的窗口，否则 exe 被占用会导致覆盖失败。

## 目录结构

```
Models/      实体与枚举（Voyage / Shipment / Milestone / 五态枚举）
Data/        SQLite 连接与建表迁移、手写 SQL 仓储、演示数据
Services/    五态状态机、30 秒调度、Excel 导入、开机自启
ViewModels/  视图模型（列表行映射、状态筛选、提醒收集）
Views/       主窗口 + 提单/节点编辑对话框 + 船期管理 + 提醒弹窗
AppPaths.cs  数据根目录（便携策略唯一决定处）
app.manifest 高 DPI 适配 / 系统兼容声明
```

## 文档

- [项目计划.md](项目计划.md) —— 目标、硬性技术规格、关键设计决策、阶段划分
- [项目进度.md](项目进度.md) —— 进度记录、接力速览、已踩过的坑
- [剪贴板双击复制修复.md](剪贴板双击复制修复.md) —— 「双击复制」从十次九不中到百发百中的完整修复纪要

## 许可证

本项目采用 [MIT 许可证](LICENSE)，Copyright (c) 2026 ArshikaTomado。
