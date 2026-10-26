using System;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Raised when a project operation is well-formed but refused by project rules.</summary>
public sealed class ProjectOperationException : Exception
{
    public ProjectOperationException(string message) : base(message)
    {
    }

    public ProjectOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
