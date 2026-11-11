using Orbit.Contracts;
using Orbit.Core;
using Xunit;

namespace Orbit.Core.Tests;

public sealed class AgentOrchestratorTests
{
    [Theory]
    [InlineData("open Notepad", "Notepad")]
    [InlineData("เปิด Visual Studio Code", "Visual Studio Code")]
    public void TryParseRecognizesSupportedLaunchRequests(string input, string expected)
    {
        Assert.Equal(expected, AppLaunchIntent.TryParse(input));
    }

    [Fact]
    public async Task DirectCommandLaunchesAuthorizedApplicationWithoutApproval()
    {
        var launcher = new FakeLauncher();
        var orchestrator = new AgentOrchestrator(new FakeCatalog(), launcher, new FakeAuditLog());

        Assert.Null(await orchestrator.RunAsync("Hello", CancellationToken.None));
        var result = await orchestrator.RunAsync("Orbit, เปิด Notepad.", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(AgentTaskState.Completed, result.State);
        Assert.Null(result.ApprovalToken);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task LaunchRequiresOneTimeApprovalToken()
    {
        var catalog = new FakeCatalog();
        var launcher = new FakeLauncher();
        var orchestrator = new AgentOrchestrator(catalog, launcher, new FakeAuditLog());

        var planned = await orchestrator.PlanAsync("open Notepad", CancellationToken.None);
        Assert.Equal(AgentTaskState.AwaitingApproval, planned.State);
        Assert.NotNull(planned.ApprovalToken);

        var first = await orchestrator.ExecuteLaunchAsync(
            new ApplicationLaunchRequest("notepad", planned.ApprovalToken!), CancellationToken.None);
        var second = await orchestrator.ExecuteLaunchAsync(
            new ApplicationLaunchRequest("notepad", planned.ApprovalToken!), CancellationToken.None);

        Assert.Equal(AgentTaskState.Completed, first.State);
        Assert.Equal(AgentTaskState.Failed, second.State);
        Assert.Equal(1, launcher.LaunchCount);
    }

    private sealed class FakeCatalog : IApplicationCatalog
    {
        private static readonly ApplicationCandidate Candidate = new("notepad", "Notepad", "notepad.exe", true);

        public Task<IReadOnlyCollection<ApplicationCandidate>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ApplicationCandidate>>([Candidate]);

        public Task<ApplicationCandidate?> FindAuthorizedAsync(string applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationCandidate?>(Candidate);
    }

    private sealed class FakeLauncher : IApplicationLauncher
    {
        public int LaunchCount { get; private set; }

        public Task<ToolExecutionResult> LaunchAsync(ApplicationCandidate application, CancellationToken cancellationToken)
        {
            LaunchCount++;
            return Task.FromResult(new ToolExecutionResult(true, "Opened Notepad.", "A process was created."));
        }
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public Task WriteAsync(string eventType, string outcome, string detail, Guid? taskId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
