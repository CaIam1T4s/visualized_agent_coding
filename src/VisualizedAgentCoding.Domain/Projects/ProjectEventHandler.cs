namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>
/// One event hat bound to a handler function. The entry script resolves visual events
/// through these bindings instead of guessing from file names.
/// </summary>
/// <param name="ScriptId">Stable id of the script that owns the handler.</param>
/// <param name="Event">One of <see cref="ProjectEventKinds"/>.</param>
/// <param name="Argument">Key name, broadcast message, backdrop name or loudness comparison.</param>
/// <param name="Handler">Python function name to call inside the script.</param>
public sealed record ProjectEventHandler(string ScriptId, string Event, string? Argument, string Handler);

/// <summary>A handler declaration before it is attached to a script id.</summary>
public sealed record EventHandlerRegistration(string Event, string? Argument, string Handler);
