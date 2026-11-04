# 蓝图编辑器第四轮手动验收修复

## Context

第四轮手动验收集中在值输入编辑、节点布局细节、注释和折叠行为：

- 双击值输入框出现白色矩形，编辑器失去焦点。
- 右侧编辑栏需要移除。
- 展开/收起按钮希望使用 Avalonia/Fluent 风格 chevron，hover 背景与图标中心对齐。
- 预览高亮圆圈从过左变成过右。
- 节点展开后底部多出一块空白，`exec` 行下方应只留约 4px。
- 所有导线应绘制在节点之上，并保持较低不透明度。
- 多行注释首尾仍多出空行。
- 所有节点标题和内部行文字没有左对齐。
- 值输入框应可正常输入。
- 收起节点时，子项、子节点输入和贝塞尔曲线都应隐藏。
- 布尔输入应生成条件表达式节点，而不是普通值表达式节点。
- 名称子项应接收 string 输入；没有输入时显示输入框。
- 空语句不应显示展开/收起按钮。
- 已有节点被拖到很远处时如何找回。

## Root Cause Notes

### 1. 双击值框白色矩形、失焦

`BlueprintWorkspace.BeginValueEdit` 使用 `Popup` 直接承载未套主题的 `TextBox`：

- `TextBox` 没有显式背景/前景，Popup 独立弹出层可能使用默认白色主题。
- `Popup` 默认可能走平台独立窗口，打开时抢走编辑器焦点。
- 目前 `Popup` 没有设置 `ShouldUseOverlayLayer`。

应改为 overlay layer 内、使用当前 FluentAvalonia 主题资源的 `TextBox`，显式绑定主题背景、前景、边框、CaretBrush，避免白色矩形和焦点跳转，同时兼容浅色/暗色主题。

### 2. 右侧编辑栏

`BlueprintEditorView.axaml` 第三列 `Grid.Column="2"` 是节点检查器。移除该列和 `Grid.ColumnDefinitions` 中的 `Auto` 后，画布可占满右侧。

### 3. chevron、主题与动效

`DrawChevron` 对普通节点使用 `header.Right - 22`，对注释使用 `header.Right - 28`；而 hover 背景矩形固定在 `header.Right - 44`、宽 28，中心是 `header.Right - 30`。所以普通节点 chevron 不在 hover 背景正中心。

此外自定义绘制使用固定深色画笔，无法跟随 FluentAvalonia 浅色/暗色主题；chevron 也没有项目管理页分类项那种展开/收起动效。应改为使用主题资源画笔，并用 FluentAvalonia 的 chevron 几何/动效风格；背景矩形和图标都以 `header.Right - 30` 为中心。

### 4. 预览圆圈过右

输出端口三角形绘制范围是 `x - 8` 到 `x`，视觉中心是 `x - 4`，不是 `x`。当前 `ExecOutputPoint` 返回 `Right`，所以圆圈过右。应返回 `Right - 4`；输入三角中心同理是 `X + 4`。

### 5. 节点底部多余块

`BlueprintLayoutEngine.Padding = 12`，普通节点 body 固定为 `rows * RowHeight + Padding`，每行只有 32px，但底部额外留了 12px。普通节点底部不应再保留额外 Padding；行本身已足够容纳 `exec` 等小字。注释使用独立 `CommentPadding`，不跟随普通节点。

### 6. 导线层级

`Render` 当前先画执行/值导线，再画节点，最后画注释导线；不同导线层级不一致。按验收要求，所有执行导线、值导线、注释绑定导线都应绘制在节点之上，并统一使用较低不透明度，形成蓝图式叠加层，而不是被节点遮挡。

### 7. 多行注释首尾空行

`CommentText` 直接 split `Text`，没有清理首尾空行；Python `_comments` 虽然跳过空注释值，但合并/投影后的文本仍可能带首尾换行。应在 C# 注释行工具和 Python 注释合并时统一去掉首尾空行。

### 8. 标题与行左对齐

标题从 `header.X + 14` 开始，行标签从 `rowRect.X + 16` 开始（`rowRect.X = rect.X + 1`），实际是 `rect.X + 17`，相差 3px；输出行使用 `rowRect.X + 16`。应统一为 `rect.X + 14`。

### 9. 收起节点未隐藏值输入

`DrawValueWires` 遍历所有可见节点时没有检查 `node.IsCollapsed`，所以父节点收起后，父节点仍有的 `ValueInputs` 会在左侧画出输入节点和值导线。应在 `DrawValueWires` 开头跳过 `node.IsCollapsed`。

### 10. bool 输入生成了值节点

`NodeProjection.AddValueInputs` 对所有复杂表达式统一创建 `value.expression`。`condition`/`guard` 端口类型是 `boolean`，应创建 `condition.expression`，显示为条件表达式节点。

### 11. 名称 string 输入

`AddArgumentPorts` 当前把所有参数设为 `"any"`。`name`/`名称` 应为 `string`，在没有值输入节点时显示可编辑输入框。应以 `NodePort.Type` 决定基础值框是否显示，而不是只看文本是否像字面量。

### 12. 空语句的折叠按钮

`DrawNode` 对所有非注释节点都绘制 chevron；`control.pass` 只有 exec-in/out，没有 body rows，也没有子节点，因此不应显示折叠按钮。应增加 `HasCollapsibleContent`，并让命中测试使用同一判断。

### 13. 远处节点找回

节点位置是持久化视图状态；拖到很远处后没有 UI 入口触发 `BlueprintLayoutEngine.AutoArrange`。应增加“重新排列节点”按钮，重置当前文档所有节点 `HasPosition=false` 后重新布局并保存。

## Approach

1. **值编辑**：值框改为 overlay `Popup`，`ShouldUseOverlayLayer=true`，TextBox 使用 FluentAvalonia 主题资源（浅色/暗色自动适配）；双击后保持编辑器焦点，提交后写回 Python。
2. **主题化绘制**：工作区背景、节点背景/行背景/边框/文字全部读取 FluentAvalonia 主题资源；`ActualThemeVariantChanged` 时重绘；节点编辑器和画布同时支持浅色/暗色。
3. **节点几何**：普通节点底部不保留 Padding；输入/输出端口中心统一为 `X+4` / `Right-4`；标题和行都从 `rect.X+14` 开始。
4. **导线层级**：节点先绘制，执行/值/注释/预览导线统一在节点之上以低不透明度绘制。
5. **折叠**：增加 `HasCollapsibleContent`；空语句不画 chevron；chevron 使用 FluentAvalonia 风格几何和展开/收起旋转动效，背景与图标中心一致；`DrawValueWires` 跳过折叠父节点。
6. **类型化值节点**：bool 端口生成 `condition.expression`；`name` 端口类型为 `string`，无输入节点时显示输入框。
7. **注释**：注释绑定导线和其他导线一样在节点之后绘制；注释首尾空行清理。
8. **恢复布局**：新增 `MainViewModel.ArrangeBlueprintNodes`，通过 `AutoArrange` 重置并保存布局；画布底部增加按钮，并加入右键菜单。

## Files to Modify

- `src/VisualizedAgentCoding.App/Controls/BlueprintWorkspace.cs`
  - Popup overlay、FluentAvalonia 主题 TextBox、焦点。
  - 主题资源画笔、浅/暗色重绘。
  - FluentAvalonia 风格 chevron/动效、中心对齐。
  - 端口中心、行/标题左对齐、导线层级、折叠值输入过滤、空语句 chevron。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintLayoutEngine.cs`
  - 普通节点不保留底部 Padding；值节点布局。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintNodeRows.cs`
  - 基础值框判断（string / 基础字面量）、值节点类型判断。
- `src/VisualizedAgentCoding.Application/Python/NodeProjection.cs`
  - bool 条件表达式节点、`name` 的 string 类型、注释文本清理。
- `src/VisualizedAgentCoding.App/Views/BlueprintEditorView.axaml`
  - 移除右侧检查器列；新增重新排列按钮。
- `src/VisualizedAgentCoding.App/App.axaml`
  - 补充/复用 FluentAvalonia 主题资源与编辑器 TextBox 样式。
- `src/VisualizedAgentCoding.App/Views/BlueprintEditorView.axaml.cs`
  - 重新排列事件转发。
- `src/VisualizedAgentCoding.App/ViewModels/MainViewModel.Blueprint.cs`
  - `ArrangeBlueprintNodes`，重置 HasPosition 并保存布局。
- `tools/python-helper/nodes_service.py` / `test_nodes.py`
  - 注释首尾空行清理测试。
- 测试：
  - `BlueprintLayoutEngineTests.cs`
  - `NodeProjectionTests.cs`
  - `BlueprintLayoutMapperTests.cs`
  - `BlueprintSourceReorderTests.cs`

## Reuse

- `BlueprintLayoutEngine.AutoArrange`：直接用于“重新排列节点”。
- `BlueprintNodeRows.Build`：继续作为行/端口/值节点判断的唯一来源。
- `BlueprintLayoutMapper.Fingerprint` / `BlueprintDocumentReconciler`：保持位置恢复。
- `DrawBezier`：普通导线和注释导线共用。
- `DrawPort`：统一输出/输入端口中心坐标。

## Steps

- [x] 值编辑 Popup 改为 `ShouldUseOverlayLayer=true`，TextBox 使用 FluentAvalonia 主题背景/前景/边框/CaretBrush，修复白色矩形和焦点，并兼容浅/暗色。
- [x] 删除右侧检查器列，画布占满。
- [x] 工作区、节点、行、边框、文字改用 FluentAvalonia 主题资源；主题切换时重绘。
- [x] chevron 中心统一为 `header.Right - 30`，hover 背景以该点居中；使用 FluentAvalonia 风格几何和展开/收起动效。
- [x] 输出端口中心改为 `Right - 4`，输入端口中心改为 `X + 4`；预览圆圈同步。
- [x] 普通节点 body 不额外保留底部 Padding；注释继续使用独立 padding。
- [x] 所有执行/值/注释/预览导线统一在节点之上绘制，并使用低不透明度。
- [x] `CommentText` 和 Python 注释合并清理首尾空行。
- [x] 节点标题与行文字统一左对齐到 `rect.X + 14`。
- [x] `DrawValueWires` 跳过 `node.IsCollapsed`，折叠时隐藏父节点值输入和值导线。
- [x] bool 复杂表达式生成 `condition.expression` 节点，其他复杂表达式继续 `value.expression`。
- [x] `name` 参数类型改为 `string`；无值输入节点且类型为 string 时显示可编辑框；输入框只接受普通名称字符串，不接受变量/表达式/值节点。
- [x] `HasCollapsibleContent`：无 body、无子节点的空语句不显示 chevron，命中测试同样跳过。
- [x] 增加“重新排列节点”按钮和右键菜单入口，调用 `ArrangeBlueprintNodes`，重置所有节点位置并保存布局。
- [x] 补充测试：端口中心、注释空行、bool/string 值节点、折叠隐藏值导线、空语句无 chevron、AutoArrange。

## Verification

1. 双击基础值框：出现深色内嵌输入框，不再出现白色矩形，编辑器保持焦点；输入回车后 Python 源码更新。
2. 右侧检查器消失，画布扩展到窗口右侧。
3. 收起/展开 chevron 位于 hover 背景中心，具有 FluentAvalonia 风格展开/收起动效；空语句没有 chevron。
4. 预览圆圈中心与输出三角中心重合，不再过左或过右。
5. 普通节点底部只保留约 4px padding，没有明显多余块。
6. 所有导线绘制在节点之上且低不透明；多行注释没有首尾空行。
7. 标题和所有行文字左边缘对齐；节点背景/画布/输入框随 FluentAvalonia 浅色/暗色主题变化。
8. 收起父节点后，其子节点和左侧值输入节点/值导线都消失。
9. bool 条件生成“条件表达式”节点；名称参数显示 string 输入框。
10. 点击“重新排列节点”按钮或右键菜单项后，远处节点回到自动布局范围。
11. 运行 `dotnet test tests/VisualizedAgentCoding.App.Tests/VisualizedAgentCoding.App.Tests.csproj` 与 `cd tools/python-helper && python -m unittest discover -s tests`。

## Decisions Confirmed

1. **重新排列入口**：画布底部状态区按钮和右键菜单都要提供。
2. **名称 string 输入**：名称输入框只接受普通名称字符串，不接受变量名/表达式，不生成值输入节点。
