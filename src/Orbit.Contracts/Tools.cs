using System.Text.Json.Serialization;

namespace Orbit.Contracts;

public enum ToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentTaskState
{
    AwaitingApproval,
    Completed,
    Failed,
    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentTaskFailure
{
    NoAuthorizedApplication
}

public sealed record ToolDescriptor(
    string Name,
    string Description,
    ToolRiskLevel RiskLevel,
    TimeSpan Timeout,
    bool RequiresApproval);

public sealed record ApplicationCandidate(
    string Id,
    string DisplayName,
    string LaunchTarget,
    bool IsAuthorized);

public sealed record ApplicationLaunchRequest(string ApplicationId, string ApprovalToken);

public sealed record ToolExecutionResult(
    bool Succeeded,
    string Message,
    string? VerificationDetail = null);

public sealed record AgentTask(
    Guid Id,
    string Input,
    string? ApplicationId,
    AgentTaskState State,
    string Message,
    string? ApprovalToken = null,
    DateTimeOffset? ApprovalExpiresAt = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentTaskFailure? Failure { get; init; }
}
