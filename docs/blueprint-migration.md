# Blueprint 命名迁移与回滚说明

本次迁移把图形编辑器的实现从 `Block*` 命名迁到 `Node*` / `Blueprint*`，并保留少量过渡兼容入口。

## 兼容入口

- `python.blocks` RPC 仍可用，并转发到 `nodes_service.build_blocks`；新代码使用 `python.nodes`。
- 旧项目布局 `blocks/layout.json` 仍可读取；首次加载会迁移写入 `blueprint/layout.json`。
- `blocks_service.py` 仅作为转发模块保留，不再包含实现。

## 已删除的旧实现文件

- `src/VisualizedAgentCoding.Application/Python/BlockProtocol.cs`
- `src/VisualizedAgentCoding.App/Controls/ScratchBlockControl.cs`
- `src/VisualizedAgentCoding.App/Views/BlockEditorView.axaml(.cs)`
- `src/VisualizedAgentCoding.App/ViewModels/BlockEditorModel.cs`

删除前已确认 C# 代码和 XAML 不再引用这些类型；旧入口由 `BlueprintEditorView`、`BlueprintEditorModel`、`BlueprintNodeControl` 和 `NodeProtocol` 替代。

## 回滚

如果需要回滚到迁移前状态：

1. 从版本控制恢复上述旧文件。
2. 将 `PythonEditorView.axaml` 的 `BlueprintEditorView` 改回 `BlockEditorView`。
3. 将 `MainViewModel` 的 `BlueprintEditor` 属性改回 `BlockEditor`，并恢复 `BlockEditorModel` 类型。
4. 恢复 `BuildBlockTreeAsync` 接口和 `BlockProtocol` 类型，或让调用方继续使用 `BuildNodeTreeAsync`。
5. 布局回滚时保留 `blueprint/layout.json` 的副本，再恢复 `blocks/layout.json`。

`python.nodes` 与 `python.blocks` 在过渡期都可用，因此 Helper 回滚不会立即破坏编辑器。
