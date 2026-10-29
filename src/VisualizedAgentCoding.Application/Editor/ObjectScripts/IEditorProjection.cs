namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public interface IEditorProjection : VisualizedAgentCoding.Application.Editor.ISourceProjection
{
    ObjectScriptDocument Document { get; }
}

public static class EditorProjection
{
    public static IEditorProjection Create(ObjectScriptDocument document) => document.TemplateVersion is 2 or 3
        ? new MultiRegionProjection(document) : new ObjectScriptProjection(document);
}
