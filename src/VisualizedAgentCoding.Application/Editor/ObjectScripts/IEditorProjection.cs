namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public interface IEditorProjection
{
    ObjectScriptDocument Document { get; }
    string ViewSource { get; }
    string FullSource { get; }
    (int Line, int Column) ToFullPosition(int line, int column);
    bool TryToViewPosition(int line, int column, out int viewLine, out int viewColumn);
}

public static class EditorProjection
{
    public static IEditorProjection Create(ObjectScriptDocument document) => document.TemplateVersion == 2
        ? new MultiRegionProjection(document) : new ObjectScriptProjection(document);
}
