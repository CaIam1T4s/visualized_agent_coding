# Visualized Agent Coding

基于 Avalonia 和 .NET 的 Python 可视化编程实验项目，探索将 Python 源码编辑、蓝图节点、角色与舞台结合的桌面开发环境。

> 本项目已停止维护，仓库用于保存实验代码、设计文档和开发记录。部分功能尚未完成，不建议用于生产环境。

## 项目目标

以 Python 源码作为唯一真源，通过文本编辑器和蓝图节点画布提供不同的代码观察与编辑方式，并结合角色、背景、事件和舞台运行时，探索可视化编程工具的实现。

## 已实现功能

- Python 文本编辑、语法诊断、代码补全与引用查询。
- 蓝图节点投影、自由布局、缩放、平移和折叠。
- 同一语句链内的执行顺序重连。
- 基础参数编辑、注释绑定和布局持久化。
- 项目、脚本、角色、背景及资源管理。
- 项目变量、列表及部分事件和舞台运行能力。
- Python Helper 与桌面应用之间的通信。

## 已知限制

- 复杂表达式尚不支持完整的节点化编辑。
- 值导线尚不支持完整的手动连接和断开。
- 跨方法、跨循环及跨分支的结构重排尚未完成。
- 参数写回及嵌套语句插入、删除仍有边界问题。
- 部分界面和运行行为仍需进一步验收。
- 主要在 Windows 环境验证，其他平台未验证。

## 技术栈

- C# / .NET 10
- Avalonia
- AvaloniaEdit
- Python 3.10+
- Jedi / Parso

## 构建与运行

需要安装 .NET 10 SDK，以及可通过 `python` 命令调用的 Python 和 pip。

在仓库根目录执行：

```powershell
dotnet build src/VisualizedAgentCoding.App/VisualizedAgentCoding.App.csproj
dotnet run --project src/VisualizedAgentCoding.App/VisualizedAgentCoding.App.csproj --no-build
```

首次构建需要获取 NuGet 和 Python 依赖。Python 依赖根据
`tools/python-helper/requirements.txt` 安装到
`artifacts/python-helper/vendor/`，不会安装到全局 Python 环境。

如果运行时需要指定 Python 解释器，可设置
`VISUALIZED_AGENT_PYTHON` 环境变量。

## 测试

```powershell
dotnet test tests/VisualizedAgentCoding.App.Tests/VisualizedAgentCoding.App.Tests.csproj
python -B -m unittest discover -s tools/python-helper/tests -p "test_*.py"
```

## 目录结构

- `src/`：桌面应用、应用逻辑、领域模型、蓝图核心和 Python 运行时。
- `tools/python-helper/`：Python 解析、分析和通信辅助程序。
- `tests/`：自动化测试与手动测试示例项目。
- `docs/`：设计文档、教程和界面截图。
- `plans/`：开发与手动验收记录。
- `artifacts/`：自动生成的依赖和临时输出，不纳入版本控制。

## 开发记录

- [项目总体计划](docs/project-plans.md)
- [Python 可视化编辑器设计](docs/python-visual-editor-plan.md)
- [蓝图命名迁移说明](docs/blueprint-migration.md)

部分文档记录的是历史阶段的设计或实现状态，可能与最终代码存在差异。

## 许可证

Copyright (C) 2026 CaIam1Tas

本项目的原创代码采用 **GNU General Public License v3.0（GPL-3.0-only）** 许可。
你可以按照 GNU 自由软件基金会发布的 GNU 通用公共许可证第 3 版的条款，重新分发和修改本程序。

本程序不提供任何担保，包括适销性或特定用途适用性的默示担保。完整条款见 [LICENSE](LICENSE)。

第三方依赖、引用代码和素材仍遵循各自的许可证与版权声明。
