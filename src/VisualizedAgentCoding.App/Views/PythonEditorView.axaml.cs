using System;
using VisualizedAgentCoding.Application.Python;
using System.ComponentModel;
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

    public PythonEditorView()
    {
        InitializeComponent();
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
            if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None
                && !EditorTextBox.IsReadOnly && EditorTextBox.SelectionLength == 0
                && EditorTextBox.TextArea.ReadOnlySectionProvider is UserRegionProtection backspaceProtection
                && backspaceProtection.TryRemoveFirstBlankLine(EditorTextBox.CaretOffset, out var backspaceCaret))
            {
                EditorTextBox.CaretOffset = backspaceCaret;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
                && !EditorTextBox.IsReadOnly && EditorTextBox.SelectionLength == 0
                && EditorTextBox.TextArea.ReadOnlySectionProvider is UserRegionProtection protection
                && protection.TryInsertEntryLine(EditorTextBox.CaretOffset, out var caret))
            {
                EditorTextBox.CaretOffset = caret;
                e.Handled = true;
                return;
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
        editorMenu.Items.Add(references);
        editorMenu.Items.Add(new MenuItem { Header = "重新加载磁盘文件…", Command = viewModel.RequestReloadScriptCommand });
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
        }

        viewModel = DataContext as MainViewModel;
        QueueProtectionRefresh();
        if (viewModel is null)
            return;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.DeleteReferenceOpened += NavigateDeleteReference;
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
            viewModel.PythonCode = EditorTextBox.Text;
    }

    private void OnDiagnosticClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PythonDiagnostic diagnostic } || EditorTextBox.Document is not { } document)
            return;
        var line = document.GetLineByNumber(Math.Clamp(diagnostic.Line, 1, document.LineCount));
        var column = Math.Clamp(diagnostic.Column, 1, line.Length + 1);
        EditorTextBox.Focus();
        EditorTextBox.CaretOffset = line.Offset + column - 1;
        EditorTextBox.ScrollTo(line.LineNumber, column);
    }

    private async void OnFindReferencesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel is not null)
            await viewModel.FindReferencesAtAsync(EditorTextBox.TextArea.Caret.Line, EditorTextBox.TextArea.Caret.Column);
    }

    private async void OnReferenceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PythonReferenceItem reference } || viewModel is null || !await viewModel.OpenReference(reference)) return;
        NavigateDeleteReference(reference);
    }

    private void NavigateDeleteReference(PythonReferenceItem reference)
    {
        var range = reference.Location.Range;
        var line = EditorTextBox.Document.GetLineByNumber(Math.Clamp(range.Line, 1, EditorTextBox.Document.LineCount));
        var column = Math.Clamp(range.Column, 1, line.Length + 1);
        EditorTextBox.CaretOffset = line.Offset + column - 1;
        EditorTextBox.ScrollTo(line.LineNumber, column);
        EditorTextBox.Focus();
    }

    private void UpdateEditorText(string text)
    {
        if (EditorTextBox.Text == text)
            return;

        updatingFromViewModel = true;
        EditorTextBox.Text = text;
        updatingFromViewModel = false;
        QueueProtectionRefresh();
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
        var source = EditorTextBox.Text;
        protectionCancellation?.Dispose();
        var cancellation = protectionCancellation = new CancellationTokenSource();
        try
        {
            if (current?.OpenScript is null) { SetProtectionStatus("未打开脚本"); return; }
            if (current.OpenScript.Kind == ProjectScriptKind.User)
            {
                EditorTextBox.IsReadOnly = false;
                SetProtectionStatus("用户脚本：全文可编辑");
                return;
            }
            SetProtectionStatus("正在识别用户区域，编辑暂时锁定");
            var document = await current.InspectEditorProtectionAsync(cancellation.Token);
            if (generation != protectionGeneration || current != viewModel || source != EditorTextBox.Text) return;
            if (document is { HasUserRegion: true } && document.FullSource == source)
            {
                EditorTextBox.IsReadOnly = false;
                // IsReadOnly replaces the provider, so install the regional policy afterwards.
                EditorTextBox.TextArea.ReadOnlySectionProvider = new UserRegionProtection(EditorTextBox.Document, document.UserRegion!);
                SetProtectionStatus("系统区域只读，用户区域可编辑");
            }
            else if (document?.Status == ObjectScriptStatus.LegacyUnmarked)
            {
                EditorTextBox.IsReadOnly = false;
                SetProtectionStatus("旧版未标记脚本：全文可编辑");
            }
            else SetProtectionStatus("区域结构未确认，全文只读；请在外部修复后重新加载磁盘文件");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generation == protectionGeneration) SetProtectionStatus("区域识别失败，全文只读；可重新加载磁盘文件以重试");
        }
    }

    private void SetProtectionStatus(string text) => ProtectionStatus.Text = text;
}
