using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Infrastructure.Python;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private bool ownsAnalysisBridge;
    private IPythonBridge? executionBridge;
    private IProjectStore runtimeStore = null!;
    private readonly IPythonEnvironment pythonEnvironment = new PythonEnvironment();
    private readonly CancellationTokenSource environmentLifetime = new();
    private Task environmentTask = Task.CompletedTask;
    [ObservableProperty] private string pythonExecutable = "";
    [ObservableProperty] private string pythonExecutableDraft = "";
    [ObservableProperty] private string pythonEnvironmentStatus = "尚未验证 Python 环境；未指定时自动检测。";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangePythonEnvironment))]
    private bool isPythonEnvironmentBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGeneralSettings)), NotifyPropertyChangedFor(nameof(IsAppearanceSettings))]
    private int settingsCategoryIndex;
    public bool IsGeneralSettings => SettingsCategoryIndex == 0;
    public bool IsAppearanceSettings => SettingsCategoryIndex == 1;
    public bool CanChangePythonEnvironment => !IsPythonActive && !IsPythonEnvironmentBusy;
    private string EffectivePythonExecutable => string.IsNullOrWhiteSpace(PythonExecutable) ? PythonEnvironment.DefaultExecutable : PythonExecutable;
    private PythonProcessBridge CreateConfiguredBridge() => new(new PythonBridgeOptions { PythonExecutable = EffectivePythonExecutable });

    [RelayCommand]
    private Task ValidatePythonEnvironment() => ApplyPythonExecutableAsync(PythonExecutableDraft);

    [RelayCommand]
    private Task DetectPythonEnvironment() => ApplyPythonExecutableAsync(PythonEnvironment.DefaultExecutable);

    public Task ApplyPythonExecutableAsync(string candidate)
    {
        if (!CanChangePythonEnvironment || environmentLifetime.IsCancellationRequested) return Task.CompletedTask;
        return environmentTask = ApplyPythonExecutableCoreAsync(candidate);
    }

    private async Task ApplyPythonExecutableCoreAsync(string candidate)
    {
        if (!CanChangePythonEnvironment) return;
        IsPythonEnvironmentBusy = true;
        PythonEnvironmentStatus = "正在验证 Python 环境…";
        try
        {
            var info = await pythonEnvironment.ValidateAsync(string.IsNullOrWhiteSpace(candidate) ? PythonEnvironment.DefaultExecutable : candidate, environmentLifetime.Token);
            // Verify the Helper before persisting or replacing the working environment.
            var replacement = new PythonProcessBridge(new PythonBridgeOptions { PythonExecutable = info.Executable });
            try { await replacement.InitializeAsync(environmentLifetime.Token); }
            catch { await replacement.DisposeAsync(); throw; }
            try
            {
                var saved = settingsStore.Load();
                settingsStore.Save(saved with { PythonExecutable = info.Executable });
            }
            catch { await replacement.DisposeAsync(); throw; }
            CancelRenameScript();
            deleteAnalysisCancellation?.Cancel();
            ResetReferenceQuery();
            await Task.WhenAll(renameTasks);
            await Task.WhenAll(deleteAnalysisTasks);
            await Task.WhenAll(referenceTasks);
            if (pythonDiagnostics is { } previous)
            {
                pythonDiagnostics = null;
                await previous.DisposeAsync();
            }
            else if (ownsAnalysisBridge && executionBridge is not null) await executionBridge.DisposeAsync();
            PythonExecutable = PythonExecutableDraft = info.Executable;
            executionBridge = replacement;
            ownsAnalysisBridge = true;
            InitializePythonDiagnostics(replacement);
            InitializeReferences(replacement, runtimeStore);
            PythonDiagnostics.Clear();
            SchedulePythonAnalysis();
            NotifyEditorSourceChanged();
            PythonEnvironmentStatus = $"Python {info.Version} · {info.Executable}（已验证并保存）";
        }
        catch (Exception ex)
        { PythonEnvironmentStatus = "验证失败，未更换配置：" + ex.Message; }
        finally { IsPythonEnvironmentBusy = false; }
    }
}
