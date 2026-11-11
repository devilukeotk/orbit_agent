using System.Security.Cryptography;
using System.Text;
using Orbit.Contracts;

namespace Orbit.Core;

public interface IApplicationCatalog
{
    Task<IReadOnlyCollection<ApplicationCandidate>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<ApplicationCandidate?> FindAuthorizedAsync(string applicationId, CancellationToken cancellationToken);
}

public interface IApplicationLauncher
{
    Task<ToolExecutionResult> LaunchAsync(ApplicationCandidate application, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task WriteAsync(string eventType, string outcome, string detail, Guid? taskId, CancellationToken cancellationToken);
}

public interface IAgentOrchestrator
{
    Task<AgentTask> PlanAsync(string input, CancellationToken cancellationToken);
    Task<AgentTask?> RunAsync(string input, CancellationToken cancellationToken);
    Task<AgentTask> ExecuteLaunchAsync(ApplicationLaunchRequest request, CancellationToken cancellationToken);
}

public sealed class AgentOrchestrator(
    IApplicationCatalog applications,
    IApplicationLauncher launcher,
    IAuditLog auditLog) : IAgentOrchestrator
{
    private readonly Dictionary<Guid, PendingLaunch> _pending = [];
    private readonly object _gate = new();

    public async Task<AgentTask?> RunAsync(string input, CancellationToken cancellationToken)
    {
        var query = AppLaunchIntent.TryParse(input);
        if (query is null)
            return null;

        var id = Guid.NewGuid();
        var candidates = (await applications.SearchAsync(query, cancellationToken))
            .Where(candidate => candidate.IsAuthorized).ToArray();
        var exact = candidates.Where(candidate =>
            candidate.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            candidate.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches = exact.Length > 0 ? exact : candidates;
        var thai = input.StartsWith("เปิด", StringComparison.Ordinal);
        if (matches.Length != 1)
        {
            await auditLog.WriteAsync("windows.app.launch", "denied",
                "No unique authorized application matched.", id, cancellationToken);
            var message = matches.Length == 0
                ? (thai ? "ไม่พบโปรแกรมที่อนุญาตให้เปิด" : "No authorized application matched.")
                : (thai ? "กรุณาระบุชื่อโปรแกรมให้ชัดเจนขึ้น" : "Please specify the application name.");
            if (matches.Length > 1)
                message += " " + string.Join(", ", matches.Take(5).Select(match => match.DisplayName));
            return new AgentTask(id, input, null, AgentTaskState.Failed, message)
            {
                Failure = matches.Length == 0 ? AgentTaskFailure.NoAuthorizedApplication : null
            };
        }

        var application = matches[0];
        await auditLog.WriteAsync("windows.app.launch", "authorized", application.Id, id, cancellationToken);
        var result = await launcher.LaunchAsync(application, cancellationToken);
        await auditLog.WriteAsync("windows.app.launch", result.Succeeded ? "completed" : "failed",
            application.Id, id, CancellationToken.None);
        var reply = thai
            ? (result.Succeeded ? $"เปิด {application.DisplayName} แล้ว" : $"เปิด {application.DisplayName} ไม่สำเร็จ")
            : result.Message;
        return new AgentTask(id, input, application.Id,
            result.Succeeded ? AgentTaskState.Completed : AgentTaskState.Failed, reply);
    }

    public async Task<AgentTask> PlanAsync(string input, CancellationToken cancellationToken)
    {
        var requestedApplication = AppLaunchIntent.TryParse(input);
        if (requestedApplication is null)
        {
            await auditLog.WriteAsync("agent.plan", "completed", "No supported tool intent was detected.", null, cancellationToken);
            return new AgentTask(Guid.NewGuid(), input, null, AgentTaskState.Completed,
                "I can chat, and I can prepare an authorized application launch when you ask me to open an app.");
        }

        var candidates = await applications.SearchAsync(requestedApplication, cancellationToken);
        var candidate = candidates.FirstOrDefault(item => item.IsAuthorized);
        if (candidate is null)
        {
            await auditLog.WriteAsync("agent.plan", "failed", $"No authorized application matched '{requestedApplication}'.", null, cancellationToken);
            return new AgentTask(Guid.NewGuid(), input, null, AgentTaskState.Failed,
                "I could not find an authorized application matching that request.");
        }

        var taskId = Guid.NewGuid();
        var approvalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        lock (_gate)
            _pending.Add(taskId, new PendingLaunch(candidate, approvalToken, expiresAt, input));

        await auditLog.WriteAsync("agent.plan", "awaiting_approval", $"Launch prepared for '{candidate.Id}'.", taskId, cancellationToken);
        return new AgentTask(taskId, input, candidate.Id, AgentTaskState.AwaitingApproval,
            $"Ready to open {candidate.DisplayName}. Confirm the launch in the desktop app.", approvalToken, expiresAt);
    }

    public async Task<AgentTask> ExecuteLaunchAsync(ApplicationLaunchRequest request, CancellationToken cancellationToken)
    {
        var pending = TakePending(request.ApplicationId, request.ApprovalToken);
        if (pending is null)
        {
            await auditLog.WriteAsync("windows.app.launch", "denied", "An invalid, expired, or reused approval token was submitted.", null, cancellationToken);
            return new AgentTask(Guid.NewGuid(), "", request.ApplicationId, AgentTaskState.Failed,
                "The approval is invalid, expired, or has already been used.");
        }

        var application = await applications.FindAuthorizedAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            await auditLog.WriteAsync("windows.app.launch", "denied", $"'{request.ApplicationId}' is no longer authorized.", pending.TaskId, cancellationToken);
            return new AgentTask(pending.TaskId, pending.Input, request.ApplicationId, AgentTaskState.Failed,
                "The application is no longer authorized to launch.");
        }

        var result = await launcher.LaunchAsync(application, cancellationToken);
        await auditLog.WriteAsync("windows.app.launch", result.Succeeded ? "completed" : "failed", result.Message, pending.TaskId, cancellationToken);
        return new AgentTask(pending.TaskId, pending.Input, application.Id,
            result.Succeeded ? AgentTaskState.Completed : AgentTaskState.Failed, result.Message);
    }

    private PendingLaunch? TakePending(string applicationId, string approvalToken)
    {
        lock (_gate)
        {
            var suppliedToken = Encoding.UTF8.GetBytes(approvalToken);
            var entry = _pending.FirstOrDefault(item =>
            {
                var storedToken = Encoding.UTF8.GetBytes(item.Value.ApprovalToken);
                return item.Value.Application.Id.Equals(applicationId, StringComparison.Ordinal) &&
                    item.Value.ExpiresAt > DateTimeOffset.UtcNow &&
                    storedToken.Length == suppliedToken.Length &&
                    CryptographicOperations.FixedTimeEquals(storedToken, suppliedToken);
            });
            if (entry.Equals(default(KeyValuePair<Guid, PendingLaunch>)))
                return null;

            _pending.Remove(entry.Key);
            return entry.Value with { TaskId = entry.Key };
        }
    }

    private sealed record PendingLaunch(ApplicationCandidate Application, string ApprovalToken, DateTimeOffset ExpiresAt, string Input)
    {
        public Guid TaskId { get; init; }
    }
}

public static class AppLaunchIntent
{
    public static string? TryParse(string input)
    {
        var normalized = input.Trim().TrimEnd('.', '!', '?', '。').TrimEnd();
        if (normalized.StartsWith("Orbit,", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[6..].TrimStart();
        const string englishPrefix = "open ";
        const string thaiPrefix = "เปิด";
        if (normalized.StartsWith(englishPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized[englishPrefix.Length..].Trim();
        if (normalized.StartsWith(thaiPrefix, StringComparison.Ordinal))
            return normalized.Length == thaiPrefix.Length ? null : normalized[thaiPrefix.Length..].Trim();
        return null;
    }
}
