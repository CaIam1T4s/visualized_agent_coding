using Avalonia.Controls;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

internal static class ScriptActionMenu
{
    public static void Show(Control source, MainViewModel viewModel, ProjectScriptItem item)
    {
        var menu = new MenuFlyout();
        var rename = new MenuItem
        {
            Header = "重命名",
            IsEnabled = item.Kind is ProjectScriptKind.User or ProjectScriptKind.Object or ProjectScriptKind.Backdrop,
            Command = viewModel.StartRenameScriptCommand,
            CommandParameter = item,
        };
        menu.Items.Add(rename);
        menu.Items.Add(new MenuItem
        {
            Header = item.Kind switch
            {
                ProjectScriptKind.Object => "删除角色及脚本…",
                ProjectScriptKind.Backdrop => "删除背景及脚本…",
                _ => "删除…",
            },
            Command = viewModel.RequestDeleteScriptCommand,
            CommandParameter = item,
        });
        menu.ShowAt(source, true);
    }
}
