# 蓝图编辑器第二轮手动验收修复

## Context

用户对上一轮蓝图/节点画布做手动验收，发现以下问题集中在六类：

1. **起始链与重连**
   - 起始节点没有连到第一条语句。
   - 手动从起始节点连到第一条语句时提示“不能连接到自己或自己的子节点”。
   - 重连节点后节点位置发生变化。
   - 重连时绑定在节点上的注释会改绑到别的节点。
2. **注释节点**
   - 注释节点没有默认收起。
   - 展开高度与内部文字不匹配，单行文字没有垂直居中。
   - 注释文字字号应和标题一致。
   - 指向注释节点的注释节点应合并为一个节点。
   - 行内注释过长没有撑宽节点，且开头仍带 `#`。
3. **case guard**
   - `case` 的 guard 条件应作为独立的布尔输入项，不应和 case 体输出挤在同一行。
4. **交互**
   - 右键节点或空白处都不能呼出菜单，只会让编辑器失焦。
   - 预览导线松开鼠标后仍可能显示，并且会选中节点。
   - `+` 的悬浮提示应是加号背景高亮，不是文字 ToolTip。
5. **导线与标题**
   - 预览导线应使用固定控制点的贝塞尔曲线，不应按障碍节点增加/减少控制点或改走折线；只避开两端所在节点，允许穿过无关节点。
   - 节点标题中表示类型的文字没有加粗。
6. **标题与参数分离**
   - 节点标题和具体行不应再拼接参数值，只显示类型名称。
   - 参数应作为对应行的输入端口/输入值出现，而不是留在标题或行文本中。

## Root Cause Notes

### 起始节点没有连线

`MainViewModel.Blueprint.cs` 的 `AddStartNode` 把 `document.Roots` 全部挂到 start 下，但 `BlueprintWorkspace.DrawWires` 只连接 `start.Children.Take(1)`。如果第一条是注释节点，注释被跳过，start 就没有任何输出线；后续第一条真实语句也不是 start 的直接可见子节点。`DrawSiblingWires` 同样在遇到注释时直接跳过，导致语句链在注释处断开。

### 重连后位置改变

`ReconnectBlueprintNodes` 先把当前节点标记为 `HasPosition`，然后 `BlueprintSourceReorder.MoveAfter` 改源码，再通过 `PythonCode` 触发重新投影。`BlueprintDocumentReconciler.Merge` 只用 LCS 按顺序匹配节点；重排后移动的节点不在原位置，LCS 匹配不到，新投影节点就没有旧坐标，布局重新计算并被 `ResolveOverlaps` 移动。需要按稳定指纹/出现次数携带坐标，而不是只按序列位置。

### 重新连接改变注释绑定

`BlueprintSourceReorder.ExpandLines` 只把紧邻、无空行隔开的行注释一起移动；而 Python 侧 `_comments` 会跳过空行，所以注释和语句之间有空行时，注释被视为绑定在语句上，但重排时没有一起移动。重新投影后 `LinkComments` 按新顺序把注释绑到另一条语句。

### 注释默认收起失效

`NodeProjection.Map` 确实把注释设为 `IsCollapsed = true`，但 `BlueprintDocumentReconciler.Merge` 和 `BlueprintLayoutMapper.Apply` 用旧的 `IsCollapsed` 覆盖新投影；旧布局文件默认 `Collapsed=false`，于是注释被展开。需要区分“默认收起”和“用户显式展开/收起”。

### 注释高度/居中/字号

`BlueprintLayoutEngine.Measure` 对注释高度按 `Math.Min(5, CommentLines(node)) * 18 + Padding` 计算，但 `BlueprintWorkspace.DrawNode` 仍绘制 `lines.Length` 全部行，二者不一致；同时没有按实际行高垂直居中。注释文字使用 11pt，而标题 13pt。

### 注释合并

`NodeProjection.LinkComments` 只把注释的 `AnnotationTarget` 指向下一个节点；如果下一个节点本身是注释，就形成“注释指向注释”的虚线，而不是合并。用户已确认：**只合并“注释节点指向注释节点”的链**，不泛化合并所有跨空行/跨作用域注释。例如两个注释节点 A→B，B→语句，则合并为一个注释节点，文本为 A+B，并指向语句。

### case guard 行

`BlueprintWorkspace.BuildRows`（以及 `BlueprintLayoutEngine.RowCount` 的同类规则）把“输入端口 + 下一个输出端口”无条件配成一行。`control.case` 的端口顺序是 pattern 输入、guard 输入、branch 输出，于是 guard 输入和 case 体输出被配到同一行；截图中 guard 行同时出现左输入和右输出。

已确认目标形态：**保留 guard 行**，将 guard 条件表达式 `speed > 0` 作为该行的一个独立布尔输入节点，而不是把 `guard speed > 0` 合成一个标签。guard 行本身不承载 case 体输出；case 体输出单独一行。`guard` 只作为行标签/端口名，`speed > 0` 作为该行的布尔输入节点值。

### 标题加粗范围

已确认：标题中只显示类型名，参数不再进入标题；因此标题整体使用 Bold。`DisplayName` 本身改为类型名（如 `匹配`、`移动`、`case`），参数值在对应输入行中显示。

### 右键菜单

`BlueprintWorkspace.OnPointerPressed` 中调用 `ContextMenu.Open(this)`，并立即 `e.Handled = true`。Avalonia 的右键菜单生命周期更适合 `ContextRequested` + `MenuFlyout.ShowAt(control, true)`；当前写法没有稳定挂到窗口的弹出层，点击只让控件获得焦点。

### 预览导线残留/选中

开始重连时 `OnPointerPressed` 设置了 `selectedId = hit.Node.Id`，所以预览会选中源节点。释放时只处理 `OnPointerReleased`；如果指针捕获丢失（右键、窗口切换、菜单弹出）`rewiring` 不会清空，预览会一直留着。

### 导线形状

`DrawWire` 先做障碍检测，命中后调用 `DrawDetour`，用折线 + 多段贝塞尔绕过所有节点；这会改变控制点数量和曲线形状。应固定为一条三次贝塞尔，只改变两个中间控制点的水平偏移；端点已经位于节点边缘，不再额外绕行其他节点。

### 标题类型不粗

`TitleTypeface` 使用 `Inter` + `SemiBold`；中文回退字体可能没有合成半粗体，而标题中的类型/名称文本看起来仍是常规字重。需要显式使用可用的 CJK 字体和 `FontWeight.Bold`。

同时，当前 `NodeProjection.Describe` 把参数直接拼进 `DisplayName`（如 `移动 10 步`、`匹配 command`、`case "go"`、`设置变量 health`），而 `AddPorts` 里的通用参数端口只显示英文 key 且没有显示参数值。结果标题和行都混入了参数，参数本身却不是一个可用的输入。应改为：

- `DisplayName` 只保存类型名：`移动`、`旋转`、`匹配`、`case`、`设置变量`、`如果`、`当` 等。
- 每个参数生成一个值输入端口，端口名使用中文参数类型名（如 `步数`、`角度`、`匹配值`、`模式`、`guard`、`变量名`、`值`），`DefaultText` 保存源码中的参数表达式/值。
- 行渲染只显示端口名，参数值在该行左侧显示为独立的输入节点/输入槽（端口 + 值框），标题和行文本不再拼参数。
- `control.match` 标题为 `匹配`，`匹配值` 是输入行；`control.case` 标题为 `case`，`模式` 与 `guard` 是输入行，case 体输出单独一行。
- 布局宽度按“标题 + 行标签 + 输入值槽”计算，不再按 `DisplayName` 长度估算。

## Approach

1. **先修投影层的节点/端口/注释语义**，再修绘制层，避免在 UI 里补语义。
2. **固定贝塞尔**：所有执行导线统一使用 `DrawBezier`，删除 `DrawDetour` 和障碍扫描；控制点只按端点水平距离调整。
3. **视图状态显式化**：为折叠状态增加 `IsCollapsedExplicit`，布局文件记录 `CollapsedExplicit`；只有用户点击 `+/-` 才写显式状态。
4. **重连携带稳定视图状态**：重连前按节点指纹/出现次数快照坐标与折叠状态，重投影后按同一键恢复；只改变源码顺序和连接，不改变任何节点的画布坐标。
5. **统一行模型**：抽出节点行构建逻辑，供布局测量、绘制、命中测试共用，避免 case guard 这类端口配对在多个地方不一致。
6. **交互事件改用 Avalonia 原生路径**：`ContextRequested` + `MenuFlyout`；增加 `OnPointerCaptureLost` 清理预览；`+` 使用自绘 hover 背景。
7. **标题与参数分离**：`DisplayName` 只保留类型名并整体 Bold；参数改为对应行的输入节点（行内值输入端口 + 值框），端口标签用中文参数名、值来自源码参数；布局宽度按标题与输入节点计算。

## Files to Modify

- `src/VisualizedAgentCoding.App/Controls/BlueprintWorkspace.cs`
  - 起始节点连接第一条非注释语句；语句链跨注释连接。
  - 注释绘制行数、垂直居中、字号、行内注释宽度/截断。
  - `+` hover 背景，移除全局 ToolTip。
  - 右键 `ContextRequested` + `MenuFlyout`。
  - 预览导线清理、取消选中、固定贝塞尔、删除绕行。
  - 选中/命中/折叠状态显式化。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintLayoutEngine.cs`
  - 注释高度与绘制行数一致；行内注释加入宽度测量。
  - 使用统一行模型计算行数。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintDocumentReconciler.cs`
  - 按稳定指纹 + 出现次数合并坐标/折叠状态，支持重排。
  - 携带注释目标映射。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintLayoutMapper.cs`
  - 增加/读取 `CollapsedExplicit`，不覆盖注释默认收起。
- `src/VisualizedAgentCoding.Domain/Blueprint/BlueprintModels.cs`
  - `IsCollapsedExplicit`；必要时增加稳定指纹辅助方法。
- `src/VisualizedAgentCoding.Domain/Blueprint/BlueprintLayout.cs`
  - `BlueprintNodeLayout` 增加 `CollapsedExplicit`（保持向后兼容默认值）。
- `src/VisualizedAgentCoding.Application/Python/NodeProjection.cs`
  - `Describe` 改为类型名，不再拼接参数。
  - 参数端口使用中文参数类型名，并用 `DefaultText` 保存参数表达式/值。
  - 注释合并；行内注释去掉开头 `#`；case guard 输入端口/行模型；注释默认折叠不被后续覆盖。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintSourceReorder.cs`
  - 移动节点时把跨空行的前置注释一起移动；避免注释重新绑定。
- `src/VisualizedAgentCoding.App/ViewModels/MainViewModel.Blueprint.cs`
  - 起始节点/重连快照/重连后布局恢复；起始节点的重连校验特例。
- 测试：
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintLayoutEngineTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintProjectionTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/` 下新增注释/重排/reconciler 测试
  - `tools/python-helper/tests/test_blocks.py` / `test_nodes.py`

## Reuse

- `BlueprintLayoutEngine.Measure`、`RowCount`、`LayoutChain`、`ResolveOverlaps`：继续作为唯一布局来源，不新增第二套坐标逻辑。
- `BlueprintLayoutMapper.Fingerprint` 和 `BlueprintDocumentReconciler.Key`：已有“类型 + 显示名 + 文本”指纹思路，扩展为“指纹 + 出现次数”的稳定键。
- `NodePort.DefaultText`：已有字段，用来承载参数/guard 表达式值，无需新增平行模型。
- `BlueprintSourceReorder.ExpandLines` / `Reindent`：保留源码移动机制，只修正前置注释识别。
- `BlueprintReconnectValidator`：保留保守校验，仅增加起始节点的合法特例。
- `MenuFlyout.ShowAt` / `ContextRequested`：沿用 `WorkspaceView`、`ScriptActionMenu` 已有的 Avalonia 菜单模式。

## Steps

- [x] 起始链：`DrawWires` 的 start 分支连接第一个非注释子节点；`DrawSiblingWires` 跳过注释后连接相邻可执行节点。
- [x] 起始节点重连校验：允许 start → 其直接子节点的“已连接”情况，不触发移动/报错。
- [x] 标题只留类型：`NodeProjection.Describe` 对每个节点只返回类型名（`移动`、`旋转`、`匹配`、`case`、`设置变量`、`如果`、`当` 等），不再拼接参数值。
- [x] 参数输入化：为每个参数生成中文参数名的值输入端口，`DefaultText` 保存源码参数表达式/值；行文本只显示参数名/类型名，参数值作为该行独立的输入节点（端口 + 值框）呈现。
- [x] 行模型：抽出共享的节点行构建/计数（含 case guard 独立行、参数值输入行），布局、绘制、命中测试共用；标题区与行区使用同一套行数据。
- [x] case guard：`control.case` 的 pattern、guard、case 体输出分属行；`guard` 只作为端口名/行标签，条件表达式（如 `speed > 0`）作为独立的布尔输入节点显示；不把 `guard` 与表达式拼成一个标签。可复用 `NodePort.DefaultText` 保存/显示表达式值。
- [x] 布局宽度：按“类型标题宽度 + 行标签宽度 + 输入值槽宽度”计算节点宽度；不再用 `DisplayName.Length` 估算。
- [x] 注释合并：`NodeProjection.LinkComments` 遇到“注释 → 注释”时合并文本并移除被指向的注释节点，继续沿用该注释的目标；只处理注释指向注释的链，不做跨空行/跨作用域的通用合并。
- [x] 注释默认收起：增加 `IsCollapsedExplicit`；投影默认收起，布局/合并只在显式状态时覆盖；保存/读取布局字段。
- [x] 注释高度：统一“有效行数”（最多 5 行滚动窗口）；测量与绘制使用同一行数；展开时垂直居中，超出 5 行保持滚动。
- [x] 注释字号：正文 13pt，与标题一致；行内注释去掉 `#`，宽度参与节点测量，过长时按最大宽度截断或撑宽。
- [x] 行内注释：布局宽度增加标题 + 行内注释所需宽度；绘制从标题右侧开始，不越过节点右边界。
- [x] `+` hover：移除 workspace 全局 ToolTip；记录 hover 的折叠按钮，绘制背景高亮。
- [x] 右键菜单：改为 `ContextRequested` + `MenuFlyout.ShowAt(this, true)`；空白处和节点上都可弹出，保留当前选中/焦点。
- [x] 预览导线：开始重连不改变 `selectedId`；释放和 `PointerCaptureLost` 都清空 `rewiring`/`snapTarget`/`rewirePoint`。
- [x] 贝塞尔：删除 `DrawDetour`/`SegmentIntersects` 路由；所有导线固定三次贝塞尔，只调整两个中间控制点。
- [x] 标题字重：标题现在只包含类型名，使用支持中文的字体 + `FontWeight.Bold` 整体绘制；参数值不再进入标题。
- [x] 重连位置：重连前快照稳定键→坐标/折叠状态；重投影后按稳定键恢复。源码顺序和连接关系改变，但节点坐标不变。
- [x] 注释绑定：`BlueprintSourceReorder.ExpandLines` 跨空行收集与移动目标关联的前置注释；必要时按旧注释→旧目标指纹重新绑定。
- [x] 测试：补充 helper 注释合并、C# 端口行、注释高度/宽度、reconciler 重排位置、布局显式折叠、重连注释绑定测试。

## Verification

1. 启动应用，打开对象脚本，切到图形化视图。
2. 写 `# 注释` 后接 `self.move(10)`，确认 start 有连线到第一条语句；注释虚线指向该语句。
3. 写两条“注释指向注释”的节点，确认合并为一个注释节点，文本依次合并，并继续指向原注释的目标；普通连续注释不应被泛化合并。
4. 写 `self.move(10)`，确认标题只显示 `移动`，参数 `10` 出现在 `步数` 输入行的输入槽中，不进入标题。
5. 写行内注释 `self.move(10)  # 很长的说明`，确认节点宽度增加、开头 `#` 不显示，标题仍只有 `移动`。
6. 写 `match x:` + `case "a" if speed > 0:`，确认 `匹配` 标题下 `匹配值` 行接收 `x`；`case` 标题下 `模式` 行接收 `"a"`、`guard` 行接收布尔表达式 `speed > 0`；case 体输出单独一行。
7. 鼠标悬停 `+`，确认只有加号背景高亮，没有文字 ToolTip。
8. 右键节点和空白处，确认弹出菜单且编辑器保持焦点/选中。
9. 从输出端拖动预览到输入端口，确认松开后预览立即消失且不选中节点；中途切换窗口/右键也不残留。
10. 观察导线：固定三次贝塞尔，无折线绕行；只避开起止节点，允许穿过其他节点。
11. 重连一条语句，确认目标语句移动后节点坐标不变，注释仍跟随原目标节点。
12. 运行 `dotnet test tests/VisualizedAgentCoding.App.Tests/VisualizedAgentCoding.App.Tests.csproj` 和 `cd tools/python-helper && python -m unittest discover -s tests`。
13. 关闭应用后再运行全量测试，避免 DLL 被占用。

## Decisions Confirmed

1. **case guard**：保留 guard 行；条件表达式作为该行独立的布尔输入节点。
2. **注释合并**：只合并“注释节点指向注释节点”的链；不因空行或作用域自动合并。
3. **重连语义**：改变源码顺序和连接关系，但保持节点画布位置不变。
4. **注释展开状态**：显式保存用户展开/收起状态，重开项目后保持。
5. **标题与参数**：节点标题和行只显示类型/参数名；参数作为对应行的独立输入节点出现，不拼进标题或行文本。标题整体是类型名，因此整体 Bold。
