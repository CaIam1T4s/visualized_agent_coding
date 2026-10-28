using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace happy_agent_coding.Composition;

internal static class AnalysisComposition
{
    public static IProjectStore CreateStore() => new JsonProjectStore();
}
