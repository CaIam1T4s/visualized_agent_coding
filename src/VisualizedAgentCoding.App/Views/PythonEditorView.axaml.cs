using System;
using System.ComponentModel;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class PythonEditorView : UserControl
{
    private MainViewModel? viewModel;
    private bool updatingFromViewModel;

    public PythonEditorView()
    {
        InitializeComponent();
        using var stream = AssetLoader.Open(new Uri("avares://VisualizedAgentCoding.App/Assets/Python-DarkPlus.xshd"));
        using var reader = XmlReader.Create(stream);
        EditorTextBox.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        EditorTextBox.TextChanged += OnEditorTextChanged;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel currentViewModel)
        {
            viewModel = currentViewModel;
            UpdateEditorText(viewModel.PythonCode);
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (viewModel is not null)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        viewModel = DataContext as MainViewModel;
        if (viewModel is null)
            return;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateEditorText(viewModel.PythonCode);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PythonCode) && viewModel is not null)
            UpdateEditorText(viewModel.PythonCode);
    }

    private void OnEditorTextChanged(object? sender, System.EventArgs e)
    {
        if (!updatingFromViewModel && viewModel is not null)
            viewModel.PythonCode = EditorTextBox.Text;
    }

    private void UpdateEditorText(string text)
    {
        if (EditorTextBox.Text == text)
            return;

        updatingFromViewModel = true;
        EditorTextBox.Text = text;
        updatingFromViewModel = false;
    }
}
