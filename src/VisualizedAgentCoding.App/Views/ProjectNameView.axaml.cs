using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class ProjectNameView : UserControl
{
    private readonly TaskCompletionSource<string?> completion = new();
    private readonly string parentDirectory;
    public ProjectNameView() : this("") { }
    public ProjectNameView(string parentDirectory)
    {
        this.parentDirectory = parentDirectory;
        InitializeComponent();
        ParentDirectory.Text = parentDirectory;
        Loaded += (_, _) => ProjectName.Focus();
        Unloaded += (_, _) => completion.TrySetResult(null);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { completion.TrySetResult(null); e.Handled = true; }
            else if (e.Key == Key.Enter && ProjectName.IsKeyboardFocusWithin)
            { OnAccept(this, new RoutedEventArgs()); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    public Task<string?> Result => completion.Task;
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(null);
    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (RootPreview is null) return;
        try { RootPreview.Text = Path.Combine(parentDirectory, (ProjectName.Text ?? "").Trim()); }
        catch (ArgumentException) { RootPreview.Text = "请输入有效的项目名称"; }
        ValidationError.IsVisible = false;
    }
    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        try
        {
            var target = ProjectCreationTarget.Resolve(parentDirectory, ProjectName.Text ?? "");
            completion.TrySetResult(target.Name);
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            ValidationError.Text = ex.Message;
            ValidationError.IsVisible = true;
        }
    }
}
