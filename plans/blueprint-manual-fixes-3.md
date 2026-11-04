# 蓝图编辑器第三轮手动验收修复

## Context

第三轮手动验收集中在以下问题：

1. **重连后节点位置互换**
   - 把循环节点输出连到匹配节点时，循环/匹配（以及中间节点）的视觉位置会交换。
2. **循环标题**
   - `while` 节点标题应为“循环”，不是“当”。
3. **注释绑定**
   - 注释节点可能变成没有绑定目标的悬浮节点。
   - 注释需要输出端口，可拖到指定节点标题栏建立绑定。
   - 绑定的直虚线应改为普通节点同款贝塞尔曲线，但保持虚线样式和颜色。
4. **折叠图标**
   - 当前 `+` / `−` 应改成 Avalonia 风格的向下/向上箭头：收起时向下，展开时向上。
5. **预览端点和行输入**
   - 预览高亮圆圈与折叠按钮重叠，输出端三角应在圆圈中心。
   - 具体行的输入应是独立输入节点，通过值导线连接到行输入端口，而不是行内直接显示值框。
   - 只有该行没有节点输入、且类型是基础变量类型时，才显示可编辑值框。
6. **起始节点重连**
   - 拖动起始节点输出到另一条语句时，不能改变第一条语句。
7. **注释间距**
   - 注释文字与上下边距过大。
8. **多行注释**
   - 多行注释上下多出空行。
   - 超过 5 行时应显示一个不可展开的滚动条指示器。
9. **切换 Python 后注释绑定变化**
   - 改变执行顺序后切到 Python 再切回，注释代码没有随绑定节点移动，绑定目标改变。
10. **case 体**
    - 删除 `case 体` 行；case 体输出直接连到 case 节点标题栏的执行输出端口。
11. **贝塞尔曲线**
    - 曲线穿过其端点所在节点。
    - 曲线不应因节点相对位置改变而翻转控制点方向。

## Root Cause Notes

### 1. 重连后节点位置互换

`MainViewModel.ReconnectBlueprintNodes` 已经通过 `BlueprintDocumentReconciler` 按稳定指纹恢复坐标，但刷新后 `BlueprintLayoutMapper.Apply` 仍先按 **节点 Id** 匹配：

```csharp
if (region.Nodes.TryGetValue(node.Id, out var item)
    || byFingerprint.TryGetValue(Fingerprint(node), out item))
```

节点 Id 是路径型的 `region:index`。源码重排后，同一个 Id 会指向另一条语句；旧布局文件里 `region:2` 是循环，刷新后 `region:2` 可能变成匹配，于是位置被按 Id 错误地套到新节点上，视觉上就是交换。应该优先按稳定指纹匹配，Id 只在指纹一致时作为后备。

### 2. 循环标题

`NodeProjection.Describe` 中 `control.while => "当"`，改为 `"循环"`。

### 3. 注释未绑定 / 直虚线

- `NodeProjection.LinkComments` 只按顺序给注释设置 `AnnotationTarget = 下一个节点`。
- 注释没有端口；`AddPorts` 对 `type == "comment"` 直接 return。
- 注释位于作用域末尾或没有后继节点时 `AnnotationTarget` 为 null，于是成为没有连线的悬浮节点。
- `BlueprintWorkspace.DrawAnnotation` 使用 `DrawLine`，所以是直虚线。
- 没有接收注释拖拽的输出端口，也没有手动改变绑定的入口。

### 4. 折叠图标

`DrawNode` 当前绘制 `node.IsCollapsed ? "+" : "−"`，不是 Avalonia 风格的 chevron。折叠状态应映射为“向下箭头”，展开状态映射为“向上箭头”。

### 5. 预览圆圈与行输入

- `ExecOutputPoint` 返回 `source.Right - 12`，而输出三角实际以 `source.Right` 为中心；所以预览圆圈落在节点内部，容易和右侧折叠按钮重叠。
- `DrawNode` 对每个输入行直接把 `row.Value` 画成行内值框；`BlueprintNodeRow.Value` 来自 `NodePort.DefaultText`。
- 当前没有“值输入节点”模型，也没有值导线、值输入端口连接、基础值编辑写回。

### 6. 起始节点不能改变第一条语句

`ReconnectBlueprintNodes` 统一调用：

```csharp
BlueprintSourceReorder.MoveAfter(currentBlueprintBody, target.Source, source.Source);
```

起始节点的 `Source` 是 `SourceSpan.None`，`MoveAfter` 开头 `target.Length <= 0` 直接返回原 body，因此拖起始节点输出不会有任何源码变化。需要单独处理 start → target：把 target 移到当前区域中第一个非注释语句的位置（`MoveBefore` 或专门的 `MoveToStart`）。

### 7. 注释上下边距过大

- `BlueprintLayoutEngine.Measure` 的注释高度是 `Math.Min(5, CommentLines) * 18 + Padding`，`Padding = 12`。
- `DrawNode` 又用 `Math.Max(6, (bodyHeight - contentHeight) / 2)` 居中。
- 注释文字行高与标题行高一致，但行内还叠加了 Padding 和居中下限，实际上下留白偏大。

### 8. 多行注释空行与滚动条

- Python `_comments` 会把空注释行（例如只有一个 `#`）追加为空字符串，形成上下空行。
- `NodeProjection.JoinCommentText` 只去掉首尾空白，不清理中间/首尾空行。
- `BlueprintWorkspace` 只有滚轮滚动逻辑，没有绘制滚动条轨道/滑块，用户不知道注释可以滚动。

### 9. 切换 Python 后注释绑定变化

当前注释绑定完全依赖投影顺序：

- 重排时 `BlueprintSourceReorder.ExpandLines` 只能把目标节点前面紧邻的注释一起移动。
- 注释和目标之间有空行、跨作用域，或注释是通过注释链合并出来的，移动范围可能不包括全部注释源码。
- 切到 Python 再切回会重新执行 `NodeProjection.LinkComments`，按新的语句顺序重新绑定，而不是恢复原来的注释目标。
- `AnnotationTarget` 是视图对象，没有保存“注释源码 → 目标节点稳定指纹”的显式绑定。

### 10. case 体行

`NodeProjection.AddPorts` 对 `control.case` 添加了 `branch-0` 执行输出，`BlueprintNodeRows` 把它渲染成 `case 体` 行。`AddExecNodePorts` 已经有标题栏的 `exec-out`，因此需要删除 `branch-0`，并修改 `DrawWires` 让 case 体从标题栏输出直接连到第一个子节点。

### 11. 贝塞尔曲线翻转/穿节点

`DrawBezier` 当前：

```csharp
var direction = end.X >= start.X ? 1 : -1;
var c1 = new Point(start.X + distance * direction, start.Y);
var c2 = new Point(end.X - distance * direction, end.Y);
```

当目标在左侧时 `direction = -1`，控制点会反向进入源节点和目标节点，曲线因此穿过端点节点。且源/终点还用了 `Right - 12`、`X - 4`，起点本身就在节点内部。

## Approach

### A. 先修稳定视图状态和端口语义

- `BlueprintLayoutMapper.Apply` 改为 **指纹优先、Id 后备**：先按 `Fingerprint` 唯一匹配；只有 Id 对应项指纹一致时才接受 Id 坐标。
- `BlueprintNodeRows` 增加 `IsBasicValue`/`CanUseInlineValue` 判断：基础类型（`boolean`、`number`、`string`、`any` 的字面量）才显示值框。
- 值输入节点模型：复杂表达式/参数值在行左侧生成独立的 `NodeKind.Expression` 值输入节点；该节点只有值输出端，通过值导线连到行输入端口。行内不再直接显示值框。
- 基础字面量（数字、字符串、`True`/`False`/`None` 等）不生成节点，使用行内可编辑值框。
- 基础值框编辑后直接改写对应 Python 参数表达式，而不是只改视图模型。需要为参数表达式保留源码范围（由 Python Helper 输出 `argumentSpans`，C# `NodeProtocol`/`BlueprintNode` 映射），避免按文本搜索替换。

### B. 注释绑定与注释节点

- 注释增加 `annotation` 输出端口（`NodeRelation.Annotation`），位置在注释标题栏**最右侧**；折叠按钮向左让位，端口三角仍以标题栏右边缘为中心。
- 注释的 `AnnotationTarget` 从“仅顺序推断”改为显式连接状态；拖动注释输出到任意节点标题栏时设置目标。
- 将注释虚线改为普通节点同款三次贝塞尔，仍使用 `CommentPen`（虚线、注释色）。
- 没有目标的注释仍保留输出端口，显示为可手动连接的悬浮注释。
- 在布局/重投影中保存注释绑定：按注释源码指纹/首行文本→目标稳定指纹恢复，避免切 Python 后改绑。
- 重排时根据 `AnnotationTarget` 找到所有绑定注释的源码范围，与目标语句一起移动，而不只依赖紧邻行。

### C. 起始节点与 case 输出

- `ReconnectBlueprintNodes` 增加 `source.Kind == Start` 分支：把 target 移动到区域第一条非注释语句之前；若 target 已是第一条则不操作。
- `control.case` 删除 `branch-0` 行；`DrawWires` 对 case 用标题栏 `exec-out` 连到第一个子节点。

### D. 交互和绘制

- 折叠按钮画 chevron：收起向下，展开向上；保留 hover 背景。
- `ExecOutputPoint` 返回端口三角中心（header/row 的 `Right`），预览圆圈同步右移。
- `DrawBezier` 控制点固定朝外：`c1 = start.X + distance`、`c2 = end.X - distance`，不再按 `end.X >= start.X` 翻转；源/终点使用真实端口边缘坐标。
- 注释正文减少 Padding，去掉首尾空行；超过 5 行时在右侧绘制细轨道和滑块（滚轮滚动，不展开为 Windows 滚动条）。

## Files to Modify

- `src/VisualizedAgentCoding.Application/Python/NodeProtocol.cs`
  - 增加参数源码范围（`argumentSpans`）传输。
- `src/VisualizedAgentCoding.Application/Python/NodeProjection.cs`
  - `control.while` 类型名“循环”。
  - `control.case` 去掉 `branch-0`。
  - 注释添加 annotation 端口。
  - 参数/condition 的 `DefaultText`、基础字面量标记、`argumentSpans` 映射。
  - 注释文本清理和绑定元数据。
- `src/VisualizedAgentCoding.Domain/Blueprint/BlueprintModels.cs`
  - 值输入节点/绑定所需字段（如 `InputNode`, `AnnotationTarget` 持久键）和参数源码范围。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintNodeRows.cs`
  - 值输入节点识别、基础值框条件、case 行。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintLayoutEngine.cs`
  - 值输入节点布局、注释高度/间距、注释滚动条尺寸。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintLayoutMapper.cs`
  - 指纹优先匹配，避免 Id 错位。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintDocumentReconciler.cs`
  - 注释绑定和值输入节点状态迁移。
- `src/VisualizedAgentCoding.Blueprint.Core/BlueprintSourceReorder.cs`
  - 注释范围移动、`MoveToStart`/`MoveBefore` 支持 start。
- `src/VisualizedAgentCoding.App/Controls/BlueprintWorkspace.cs`
  - chevron 图标、端口中心、值节点绘制/导线、注释贝塞尔、注释滚动条、注释输出拖拽。
  - 所有节点行文字统一左对齐；注释 `annotation` 输出端口放在标题栏最右侧，折叠按钮左移。
- `src/VisualizedAgentCoding.App/ViewModels/MainViewModel.Blueprint.cs`
  - start 重连、值框编辑写回、注释重连/源码移动。
- `src/VisualizedAgentCoding.App/Views/BlueprintEditorView.axaml.cs`
  - 新增事件转发（如有）。
- 测试：
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintLayoutMapperTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintSourceReorderTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/NodeProjectionTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintLayoutEngineTests.cs`
  - `tests/VisualizedAgentCoding.App.Tests/BlueprintReconnectValidatorTests.cs`

## Reuse

- `BlueprintDocumentReconciler.ByStableKey` / `Fingerprint`：已有稳定匹配逻辑，扩展用于注释绑定和值节点。
- `BlueprintNodeRows.Build`：继续作为行/端口唯一构建来源。
- `BlueprintNodeRows.CanShareRow`：保留 case/condition 行配对规则。
- `BlueprintSourceReorder.ExpandLines` / `Reindent`：保留源码移动基础，增加注释范围参数。
- `BlueprintWorkspace.DrawBezier`：普通导线和注释绑定共用同一曲线函数。
- `ExecOutputPoint` / `HitExecOutput`：统一端口中心坐标。

## Steps

- [x] 修复 `BlueprintLayoutMapper.Apply`：指纹优先，Id 只做指纹一致时的后备，解决重连后位置互换。
- [x] `Describe(control.while)` 改为“循环”。
- [x] `control.case` 删除 `branch-0` 行；`DrawWires` 让 case 体从标题栏输出连到第一个子节点。
- [x] 注释增加 annotation 输出端口，放在注释标题栏最右侧；折叠按钮左移让位；支持拖到目标节点标题栏并更新 `AnnotationTarget`。
- [x] 注释虚线改为 `DrawBezier` + `CommentPen`；无目标注释保留可连接端口。
- [x] 注释文本清理：去掉 `_comments`/投影产生的首尾空行和空注释行；减小注释 Padding。
- [x] 注释超过 5 行绘制细滚动条轨道/滑块，保留滚轮滚动。
- [x] 折叠按钮改为 chevron：收起向下、展开向上，保留 hover 背景；位置左移以避开最右侧 annotation 端口。
- [x] 节点内具体行文字统一左对齐，不再对输出行使用居中绘制。
- [x] `ExecOutputPoint` 使用端口三角中心；预览圆圈右移，消除与折叠按钮重叠。
- [x] 值输入节点模型：复杂表达式/参数值生成独立输入节点，值导线连接到行输入端口；行内不再直接显示值框。
- [x] 基础值框：仅在没有值输入节点且表达式是基础字面量时显示；编辑后按参数源码范围改写 Python 参数。
- [x] 起始节点重连：`source.Kind == Start` 时移动 target 到第一条非注释语句位置，而不是 `MoveAfter` 空源码。
- [x] 注释绑定持久化：重投影按注释指纹恢复目标；重排时把绑定注释源码范围随目标移动。
- [x] 贝塞尔控制点固定朝外，源/终点使用真实端口边缘，禁止穿透端点节点。
- [x] 补充测试：位置互换、注释绑定/重连、基础值框条件、start 重连、case 输出、贝塞尔控制点、注释空行/滚动条。

## Verification

1. 循环节点标题显示“循环”。
2. 拖动循环输出到匹配节点：循环、匹配及其他节点视觉位置不变，`#` 序号按源码顺序变化。
3. 拖动起始节点输出到另一条语句：第一条语句变为目标，节点位置不变。
4. 注释节点最右侧显示 annotation 输出端口；折叠按钮在其左侧；拖到任意节点标题栏后建立虚线贝塞尔绑定。
5. 注释位于区域末尾时仍是可连接节点，不消失。
6. 多行注释没有首尾空行，上下间距紧凑；超过 5 行右侧出现细滚动条，滚轮可滚动。
7. 折叠按钮为向下/向上 chevron；预览端输出圆圈中心与三角中心对齐，不碰折叠按钮。
8. 复杂表达式显示独立值/表达式节点；基础字面量显示可编辑值框；修改值框后 Python 源码中的参数同步改变；所有行文字左对齐。
9. case 节点没有“case 体”行，case 体从标题栏输出端点连出。
10. 所有导线/注释曲线不穿过起止节点，不因节点相对位置翻转。
11. 重排后切到 Python 再切回，注释仍绑定原目标，注释源码随目标移动。
12. 运行 `dotnet test tests/VisualizedAgentCoding.App.Tests/VisualizedAgentCoding.App.Tests.csproj` 和 `cd tools/python-helper && python -m unittest discover -s tests`。

## Decisions Confirmed

1. **值输入节点**：复杂表达式/参数值生成独立值输入节点；基础字面量使用行内可编辑值框，不生成节点。
2. **注释手动绑定**：从注释节点的 `annotation` 输出端拖到目标节点标题栏建立绑定，同时把注释源码移动到目标语句之前，保证切换 Python 后绑定不丢失。
3. **基础值写回**：值框编辑直接改写当前区域 Python 源码中的参数表达式。
