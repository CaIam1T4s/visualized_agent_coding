using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace happy_agent_coding.ViewModels;

/// <summary>One inline rename session, retained across explorer refreshes.</summary>
public partial class ScriptRenameState : ObservableObject
{
    public ScriptRenameState(string scriptId, string name, Func<Task> confirm, Action cancel)
    {
        ScriptId = scriptId;
        this.name = name;
        ConfirmCommand = new AsyncRelayCommand(confirm);
        CancelCommand = new RelayCommand(cancel);
    }

    public string ScriptId { get; }
    public IAsyncRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? error;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
}
