using System;
using VisualizedAgentCoding.Application.Python;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class PythonEditorView : UserControl
{
    private MainViewModel? viewModel;
    private bool updatingFromViewModel;
    private MenuFlyout? editorMenu;
    private CancellationTokenSource? protectionCancellation;
    private long protectionGeneration;
    private IEditorProjection? projection;
    private VisualizedAgentCoding.Application.Editor.UserScriptImportProjection? userImportProjection;
    private VisualizedAgentCoding.Application.Editor.ISourceProjection? CurrentProjection =>
        (VisualizedAgentCoding.Application.Editor.ISourceProjection?)userImportProjection ?? projection;
    private string? displayedFullSource;
    private string? displayedScriptId;
    private (int Line, int Column)? pendingNavigation;

    public PythonEditorView()
    {
        InitializeComponent();
        EditorTextBox.Options.ConvertTabsToSpaces = true;
        EditorTextBox.Options.IndentationSize = PythonWrapperIndentation.EditorIndentationSize;
        ConfigureIndentationPaste();
        using var stream = AssetLoader.Open(new Uri("avares://VisualizedAgentCoding.App/Assets/Python-DarkPlus.xshd"));
        using var reader = XmlReader.Create(stream);
        EditorTextBox.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => { protectionCancellation?.Cancel(); protectionGeneration++; };
        EditorTextBox.TextChanged += OnEditorTextChanged;
        // Listen before the text area's input handlers, including handled events.
        // The editor's inner controls need not bubble a ContextRequested event.
        EditorTextBox.AddHandler(InputElement.PointerReleasedEvent, OnEditorPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        EditorTextBox.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!EditorTextBox.IsReadOnly && EditorTextBox.SelectionLength == 0 && e.KeyModifiers == KeyModifiers.None
                && EditorTextBox.TextArea.ReadOnlySectionProvider is MultiRegionReadOnlyProvider regions)
            {
                var caret = EditorTextBox.CaretOffset;
                if ((e.Key == Key.Enter && regions.TryEnterFromTitle(caret, out caret))
                    || (e.Key == Key.Back && regions.TryBackspaceFirstBlankLine(caret, out caret)))
                {
                    EditorTextBox.CaretOffset = caret;
                    e.Handled = true;
                    return;
                }
            }
            if ((e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) || e.Key == Key.Apps)
            {
                e.Handled = true;
                Dispatcher.UIThread.Post(() => ShowEditorMenu(atPointer: false));
                return;
            }
            if (e.Key == Avalonia.Input.Key.F12 && e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift)
            {
                e.Handled = true;
                OnFindReferencesClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (EditorTextBox.SelectionLength == 0 && EditorTextBox.GetPositionFromPoint(e.GetPosition(EditorTextBox)) is { } position)
            EditorTextBox.TextArea.Caret.Position = position;
        e.Handled = true;
        Dispatcher.UIThread.Post(() => ShowEditorMenu(atPointer: true));
    }

    private void OnReloadScriptClick(object? sender, RoutedEventArgs e) => viewModel?.RequestReloadScriptCommand.Execute(null);

    private void OnEditorMenuClick(object? sender, RoutedEventArgs e) => ShowEditorMenu(atPointer: false, sender as Control);

    private void ShowEditorMenu(bool atPointer, Control? anchor = null)
    {
        if (viewModel?.OpenScript is null || viewModel.IsScriptActionDialogOpen) return;
        editorMenu?.Hide();
        var references = new MenuItem { Header = "查找引用", InputGesture = new KeyGesture(Key.F12, KeyModifiers.Shift) };
        references.Click += OnFindReferencesClick;
        editorMenu = new MenuFlyout();
        var add = new MenuItem { Header = "添加" };
        var method = new MenuItem { Header = "自定义方法…", IsEnabled = !EditorTextBox.IsReadOnly
            && (viewModel.OpenScript.Kind == ProjectScriptKind.User || projection is MultiRegionProjection) };
        method.Click += OnAddMethodClick;
        add.Items.Add(method);
        var initialize = new MenuItem { Header = "初始化方法", IsEnabled = !EditorTextBox.IsReadOnly
            && projection is MultiRegionProjection };
        initialize.Click += (_, _) => AddInitializeWithHistory();
        add.Items.Add(initialize);
        var canManageImports = !EditorTextBox.IsReadOnly && (viewModel.OpenScript.Kind == ProjectScriptKind.User || projection?.Document.TemplateVersion == 3);
        var imports = new MenuItem { Header = "导入脚本…", IsEnabled = canManageImports };
        ToolTip.SetTip(imports, canManageImports ? "导入其他用户脚本或标准库" : "旧版角色或背景脚本需先升级模板");
        imports.Click += OnManageImportsClick;
        add.Items.Add(imports);
        editorMenu.Items.Add(add);
        editorMenu.Items.Add(new Separator());
        editorMenu.Items.Add(references);
        editorMenu.Items.Add(new MenuItem { Header = "重新加载磁盘文件…", Command = viewModel.RequestReloadScriptCommand });
        var upgrade = new MenuItem { Header = "升级脚本模板…", IsEnabled = !EditorTextBox.IsReadOnly
            && projection is { Document.TemplateVersion: 1 or 2 } };
        ToolTip.SetTip(upgrade, projection?.Document.TemplateVersion == ObjectScriptTemplateVersion.Current
            ? "当前脚本已是最新版" : "支持区域结构已确认的旧版角色和背景脚本");
        upgrade.Click += OnUpgradeScriptClick;
        editorMenu.Items.Add(upgrade);
        var manageImports = new MenuItem { Header = "管理导入…", IsEnabled = canManageImports };
        manageImports.Click += OnManageImportsClick;
        editorMenu.Items.Add(manageImports);
        var convertImport = new MenuItem { Header = "将当前行导入转为受管理导入", IsEnabled = canManageImports };
        convertImport.Click += OnConvertImportClick;
        editorMenu.Items.Add(convertImport);
        var indentation = new MenuItem { Header = "转换缩进为空格", IsEnabled = !EditorTextBox.IsReadOnly };
        indentation.Click += (_, _) => NormalizeIndentation();
        editorMenu.Items.Add(indentation);
        EditorTextBox.Focus();
        editorMenu.ShowAt(anchor ?? EditorTextBox, atPointer);
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel currentViewModel)
        {
            viewModel = currentViewModel;
            UpdateEditorText(viewModel.PythonCode);
            QueueProtectionRefresh();
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.DeleteReferenceOpened -= NavigateDeleteReference;
            viewModel.RuntimeLocationOpened -= NavigateDeleteReference;
            viewModel.EditorHistoryResetRequested -= ResetEditorHistory;
        }

        viewModel = DataContext as MainViewModel;
        QueueProtectionRefresh();
        if (viewModel is null)
            return;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.DeleteReferenceOpened += NavigateDeleteReference;
        viewModel.RuntimeLocationOpened += NavigateDeleteReference;
        viewModel.EditorHistoryResetRequested += ResetEditorHistory;
        UpdateEditorText(viewModel.PythonCode);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.EditorSourceRevision)) QueueProtectionRefresh();
        if (e.PropertyName == nameof(MainViewModel.PythonCode) && viewModel is not null)
            UpdateEditorText(viewModel.PythonCode);
    }

    private void OnEditorTextChanged(object? sender, System.EventArgs e)
    {
        if (!updatingFromViewModel && viewModel is not null)
        {
            if (userImportProjection is { } user && EditorTextBox.TextArea.ReadOnlySectionProvider is MultiRegionReadOnlyProvider userProvider)
                user.UpdateRegions(userProvider.ReadBodies());
            else if (projection is MultiRegionProjection multi
                && EditorTextBox.TextArea.ReadOnlySectionProvider is MultiRegionReadOnlyProvider provider)
                multi.UpdateRegions(provider.ReadBodies());
            else if (projection is ObjectScriptProjection single) single.Update(EditorTextBox.Text);
            displayedFullSource = CurrentProjection?.FullSource ?? EditorTextBox.Text;
            viewModel.PythonCode = displayedFullSource;
        }
    }

    private void OnDiagnosticClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PythonDiagnostic diagnostic } || EditorTextBox.Document is not { } document)
            return;
        if (diagnostic.Navigation == "imports") { OnManageImportsClick(this, e); return; }
        if (diagnostic.Line < 1) { EditorTextBox.TextArea.Focus(); SetProtectionStatus(diagnostic.Message); return; }
        var selection = DiagnosticSelection.Locate(document, diagnostic);
        EditorTextBox.Select(selection.Offset, selection.Length);
        EditorTextBox.TextArea.Focus();
        var position = document.GetLocation(selection.Offset);
        EditorTextBox.ScrollTo(position.Line, position.Column);
    }

    private async void OnFindReferencesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel is not null)
        {
            var position = CurrentProjection?.ToFullPosition(EditorTextBox.TextArea.Caret.Line, EditorTextBox.TextArea.Caret.Column)
                ?? (EditorTextBox.TextArea.Caret.Line, EditorTextBox.TextArea.Caret.Column);
            if (position.Item1 < 1) { SetProtectionStatus("请选择区域内的用户代码查询引用"); return; }
            await viewModel.FindReferencesAtAsync(position.Item1, position.Item2);
        }
    }

    private async void OnReferenceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PythonReferenceItem reference } || viewModel is null || !await viewModel.OpenReference(reference)) return;
        NavigateDeleteReference(reference);
    }

    private void NavigateDeleteReference(PythonReferenceItem reference)
    {
        var range = reference.Location.Range;
        pendingNavigation = (range.Line, range.Column);
        if (EditorTextBox.IsReadOnly) return;
        ApplyPendingNavigation();
    }

    private void ApplyPendingNavigation()
    {
        if (pendingNavigation is not { } target) return;
        pendingNavigation = null;
        var targetLine = target.Line;
        var targetColumn = target.Column;
        if (CurrentProjection is { } currentProjection && !currentProjection.TryToViewPosition(target.Line, target.Column, out targetLine, out targetColumn))
        {
            SetProtectionStatus("该位置位于系统代码中，未在用户视图中显示");
            return;
        }
        var line = EditorTextBox.Document.GetLineByNumber(Math.Clamp(targetLine, 1, EditorTextBox.Document.LineCount));
        var column = Math.Clamp(targetColumn, 1, line.Length + 1);
        EditorTextBox.CaretOffset = line.Offset + column - 1;
        EditorTextBox.ScrollTo(line.LineNumber, column);
        EditorTextBox.Focus();
    }

    private void UpdateEditorText(string text)
    {
        if (displayedFullSource == text)
            return;
        QueueProtectionRefresh();
    }

    private void SetViewText(string text)
    {
        var scriptId = viewModel?.OpenScript?.Id;
        if (displayedScriptId != scriptId)
        {
            EditorTextBox.Document.UndoStack.ClearAll();
            displayedScriptId = scriptId;
        }
        if (EditorTextBox.Text == text) return;
        updatingFromViewModel = true;
        try { EditorTextBox.Text = text; }
        finally { updatingFromViewModel = false; }
    }

    private void QueueProtectionRefresh()
    {
        protectionCancellation?.Cancel();
        var generation = ++protectionGeneration;
        // Lock immediately, including the interval before asynchronous inspection completes.
        EditorTextBox.IsReadOnly = true;
        EditorTextBox.TextArea.ReadOnlySectionProvider = LockedEditorProtection.Instance;
        Dispatcher.UIThread.Post(() => _ = RefreshProtectionAsync(generation));
    }

    private async Task RefreshProtectionAsync(long generation)
    {
        if (generation != protectionGeneration) return;
        var current = viewModel;
        var source = current?.PythonCode;
        protectionCancellation?.Dispose();
        var cancellation = protectionCancellation = new CancellationTokenSource();
        try
        {
            if (current?.OpenScript is null)
            {
                userImportProjection = null;
                projection = null;
                current?.SetEditorProjection(null);
                displayedFullSource = source;
                SetViewText("");
                SetProtectionStatus("未打开脚本");
                return;
            }
            if (current.OpenScript.Kind == ProjectScriptKind.User)
            {
                projection = null;
                current.SetEditorProjection(null);
                VisualizedAgentCoding.Application.Editor.ScriptImportsResult? imports = null;
                try { imports = await current.InspectScriptImportsAsync(token: cancellation.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (source is not null && !source.Contains("# vac:imports-begin", StringComparison.Ordinal)
                    && !source.Contains("# vac:imports-end", StringComparison.Ordinal))
                { /* Plain user editing remains available without a Python helper. */ }
                if (generation != protectionGeneration || current != viewModel || source != current.PythonCode) return;
                if (imports?.Problem is not null) { displayedFullSource = source; SetViewText(source!); SetProtectionStatus(imports.Problem); return; }
                userImportProjection = new(new(current.OpenScript.Id, imports?.Uri ?? current.CurrentScriptUri!, source!, imports?.Block));
                current.SetUserImportProjection(userImportProjection);
                displayedFullSource = source;
                SetViewText(userImportProjection.ViewSource);
                EditorTextBox.IsReadOnly = false;
                EditorTextBox.TextArea.ReadOnlySectionProvider = new MultiRegionReadOnlyProvider(EditorTextBox.Document, userImportProjection.EditableRegions);
                SetProtectionStatus(imports is null ? "用户脚本可编辑；导入分析暂不可用" : "用户脚本：用户代码可编辑");
                ApplyPendingNavigation();
                return;
            }
            SetProtectionStatus("正在识别用户区域，编辑暂时锁定");
            var document = await current.InspectEditorProtectionAsync(cancellation.Token);
            if (generation != protectionGeneration || current != viewModel || source != current.PythonCode) return;
            userImportProjection = null;
            if (document is { TemplateVersion: 2 or 3, HasRegions: true } && document.FullSource == source)
            {
                var multi = new MultiRegionProjection(document);
                projection = multi;
                current.SetEditorProjection(multi);
                displayedFullSource = source;
                SetViewText(multi.ViewSource);
                EditorTextBox.IsReadOnly = false;
                EditorTextBox.TextArea.ReadOnlySectionProvider = new MultiRegionReadOnlyProvider(EditorTextBox.Document, multi.EditableRegions);
                SetProtectionStatus("用户代码");
                ApplyPendingNavigation();
            }
            else if (document is { HasUserRegion: true } && document.FullSource == source)
            {
                projection = new ObjectScriptProjection(document);
                current.SetEditorProjection(projection);
                displayedFullSource = source;
                SetViewText(projection.ViewSource);
                EditorTextBox.IsReadOnly = false;
                SetProtectionStatus("用户代码");
                ApplyPendingNavigation();
            }
            else if (document?.Status == ObjectScriptStatus.LegacyUnmarked)
            {
                projection = null;
                current.SetEditorProjection(null);
                displayedFullSource = source;
                SetViewText(source!);
                EditorTextBox.IsReadOnly = false;
                SetProtectionStatus("旧版未标记脚本：全文可编辑");
                ApplyPendingNavigation();
            }
            else
            {
                projection = null;
                current.SetEditorProjection(null);
                displayedFullSource = source;
                SetViewText(source!);
                SetProtectionStatus("区域结构未确认，全文只读；请修复后重新加载");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generation == protectionGeneration)
            {
                projection = null;
                current?.SetEditorProjection(null);
                displayedFullSource = source;
                SetViewText(source ?? "");
                SetProtectionStatus("区域识别失败，全文只读；可重新加载磁盘文件以重试");
            }
        }
    }

    private void SetProtectionStatus(string text) => ProtectionStatus.Text = text;

    private void ResetEditorHistory() => EditorTextBox.Document.UndoStack.ClearAll();
}
