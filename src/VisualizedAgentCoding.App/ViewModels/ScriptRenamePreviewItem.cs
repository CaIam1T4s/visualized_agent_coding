using System;
using System.Linq;
using VisualizedAgentCoding.Application.Projects;

namespace happy_agent_coding.ViewModels;

public sealed record ScriptRenamePreviewItem(ScriptRenameDocument Document)
{
    public string Path => Document.Path;
    public string Before => WithLineNumbers(Document.Before);
    public string After => WithLineNumbers(Document.After);
    private static string WithLineNumbers(string source) => string.Join(Environment.NewLine,
        source.Split('\n').Select((line, index) => $"{index + 1,4}  {line.TrimEnd('\r')}"));
}

