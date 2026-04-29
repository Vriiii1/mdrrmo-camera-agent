using FluentAssertions;
using MdrrmoCameraAgent;

namespace MdrrmoCameraAgent.Tests;

public class SmokeTest
{
    [Fact]
    public void EntryPoint_PrintsAgentName()
    {
        // Sanity: program-level constants are wired.
        Program.AgentName.Should().Be("MdrrmoCameraAgent");
    }
}
