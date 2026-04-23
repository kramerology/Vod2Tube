namespace Vod2Tube.Application.Models;

public sealed class ExecutableReadinessStatus
{
    public bool IsReady { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public IReadOnlyList<ExecutableRequirementStatus> RequiredExecutables { get; init; } = [];
    public string Message => IsReady
        ? "All required third-party executables are available."
        : "One or more required third-party executables are missing. Update the configured tool paths in Settings before running jobs.";
}

public sealed class ExecutableRequirementStatus
{
    public required string SettingName { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public bool Exists { get; init; }
}
