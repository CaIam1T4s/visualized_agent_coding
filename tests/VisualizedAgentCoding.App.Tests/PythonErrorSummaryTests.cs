using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class PythonErrorSummaryTests
{
    private static MainViewModel Create() => TestMainViewModelFactory.Create(new Settings());

    [Fact]
    public void CompletedAnalysisCountsErrorsAndExcludesWarnings()
    {
        var vm = Create();
        vm.PythonAnalysisStatus = "语法正常";
        Assert.Equal("0 个错误", vm.PythonErrorSummary);
        vm.PythonDiagnostics.Add(new PythonDiagnostic("error", "first", 1, 1));
        vm.PythonDiagnostics.Add(new PythonDiagnostic("error", "second", 3, 1));
        vm.PythonDiagnostics.Add(new PythonDiagnostic("warning", "hint", 4, 1));
        vm.PythonAnalysisStatus = "2 个语法错误";
        Assert.Equal(2, vm.PythonErrorCount);
        Assert.Equal("2 个错误", vm.PythonErrorSummary);
    }

    [Fact]
    public void PendingAndUnavailableAnalysisDoNotClaimZeroErrors()
    {
        var vm = Create();
        Assert.Equal("未检查", vm.PythonErrorSummary);
        vm.PythonAnalysisStatus = "语法检查中…";
        Assert.Equal("检查中…", vm.PythonErrorSummary);
        vm.PythonAnalysisStatus = "语法检查不可用：missing python";
        Assert.Equal("检查不可用", vm.PythonErrorSummary);
    }

    [Fact]
    public void ReadyIsHiddenButSaveFeedbackRemainsAvailable()
    {
        var vm = Create();
        Assert.Empty(vm.EditorOperationStatus);
        vm.EditorState = "已保存";
        Assert.Equal("已保存", vm.EditorOperationStatus);
    }

    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
