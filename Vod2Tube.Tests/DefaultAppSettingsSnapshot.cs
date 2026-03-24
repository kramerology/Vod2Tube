using Microsoft.Extensions.Options;
using Vod2Tube.Application;

namespace Vod2Tube.Tests;

/// <summary>
/// Simple <see cref="IOptionsSnapshot{TOptions}"/> stub that returns default
/// <see cref="AppSettings"/> values.  Used to satisfy constructor requirements
/// in tests that do not exercise the settings-dependent code paths.
/// </summary>
internal sealed class DefaultAppSettingsSnapshot : IOptionsSnapshot<AppSettings>
{
    public static readonly DefaultAppSettingsSnapshot Instance = new();

    private readonly AppSettings _value = new();

    public AppSettings Value => _value;
    public AppSettings Get(string? name) => _value;
}
