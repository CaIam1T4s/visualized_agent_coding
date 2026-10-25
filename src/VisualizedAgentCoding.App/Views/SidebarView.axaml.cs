using System;
using Avalonia.Controls;
using Avalonia.Threading;
using AtomUI.Icons.AntDesign;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class SidebarView : UserControl
{
    private const double IconOnlyButtonWidth = 44;
    private const double TextButtonWidth = 148;
    private const double IconOnlySidebarWidth = 68;
    private const double ExpandedSidebarWidth = 172;
    private bool responsiveCompact;

    public SidebarView()
    {
        InitializeComponent();
        ProjectIcon.Content = CreateIcon(AntDesignIconKind.FileOutlined);
        EditorIcon.Content = CreateIcon(AntDesignIconKind.FormOutlined);
        AgentIcon.Content = CreateIcon(AntDesignIconKind.RobotOutlined);
        SearchIcon.Content = CreateIcon(AntDesignIconKind.FileSearchOutlined);
        TutorialIcon.Content = CreateIcon(AntDesignIconKind.GlobalOutlined);
        SettingsIcon.Content = CreateIcon(AntDesignIconKind.SettingOutlined);
        SnapshotsIcon.Content = CreateIcon(AntDesignIconKind.BranchesOutlined);
        AccountIcon.Content = CreateIcon(AntDesignIconKind.UserOutlined);
        DataContextChanged += OnDataContextChanged;
    }

    private static Control CreateIcon(AntDesignIconKind kind)
    {
        var icon = (Control)new AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SidebarDisplayModeIndex))
                    Dispatcher.UIThread.Post(() => ApplyDisplayMode(viewModel.SidebarDisplayModeIndex));
            };
            ApplyDisplayMode(viewModel.SidebarDisplayModeIndex);
        }
    }

    public void SetResponsiveCompact(bool compact)
    {
        responsiveCompact = compact;
        if (DataContext is MainViewModel viewModel)
            ApplyDisplayMode(viewModel.SidebarDisplayModeIndex);
    }

    private void ApplyDisplayMode(int mode)
    {
        var iconOnly = responsiveCompact || mode == 0;
        var textOnly = !responsiveCompact && mode == 1;
        var iconSize = iconOnly ? 24 : 16;

        foreach (var icon in new[] { ProjectIcon, EditorIcon, AgentIcon, SearchIcon, TutorialIcon, SnapshotsIcon, AccountIcon, SettingsIcon })
        {
            icon.IsVisible = !textOnly;
            if (icon.Content is Control control)
            {
                control.Width = iconSize;
                control.Height = iconSize;
            }
            icon.Width = iconSize;
            icon.Height = iconSize;
        }

        foreach (var label in new[] { ProjectLabel, EditorLabel, AgentLabel, SearchLabel, TutorialLabel, SnapshotsLabel, AccountLabel, SettingsLabel })
            label.IsVisible = !iconOnly;

        foreach (var button in new[] { ProjectButton, EditorButton, AgentButton, SearchButton, TutorialButton, SnapshotsButton, AccountButton, SettingsButton })
        {
            button.HorizontalContentAlignment = iconOnly ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
            button.Width = iconOnly ? IconOnlyButtonWidth : TextButtonWidth;
            button.MinWidth = button.Width;
        }

        Width = iconOnly ? IconOnlySidebarWidth : ExpandedSidebarWidth;
    }
}
