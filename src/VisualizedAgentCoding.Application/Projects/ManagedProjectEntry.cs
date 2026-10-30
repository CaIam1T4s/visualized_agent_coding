using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>The project entry is product-owned, independently versioned from object templates.</summary>
public static class ManagedProjectEntry
{
    public const string LegacySource = "def main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n";
    public const string Source = "# vac:entry-version=1\n# Managed by Visualized Agent Coding. Do not edit.\nfrom vac_runtime._entry import run_project\n\n\ndef main():\n    run_project()\n\n\nif __name__ == \"__main__\":\n    main()\n";

    public static bool IsCurrent(string source) => Normalize(source) == Source;
    public static bool IsLegacy(string source) => Normalize(source) == LegacySource;
    private static string Normalize(string source) => source.Replace("\r\n", "\n");

    /// <summary>Only the exact original empty entry may be upgraded; never overwrite custom code.</summary>
    public static void Ensure(IProjectStore store, ProjectWorkspace project)
    {
        var source = store.ReadMainScript(project.RootPath);
        if (IsCurrent(source)) return;
        if (!IsLegacy(source))
            throw new ProjectOperationException("系统入口 main.py 已被修改或版本不受支持，无法运行。请恢复当前版本的系统入口；原文件未被覆盖。");
        // Recheck immediately before the write, just like other ordinary file updates.
        if (store.ReadMainScript(project.RootPath) != source)
            throw new ProjectOperationException("升级期间系统入口已变化，请重新运行。");
        store.WriteMainScript(project.RootPath, Source);
    }
}
