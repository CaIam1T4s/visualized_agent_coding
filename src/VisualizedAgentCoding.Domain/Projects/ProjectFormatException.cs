using System;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Raised when a folder does not hold a valid project workspace.</summary>
public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException(string message) : base(message)
    {
    }

    public ProjectFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
