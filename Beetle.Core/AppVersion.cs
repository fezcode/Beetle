using System.Reflection;

namespace Beetle.Core;

/// <summary>The running version, read from the assembly stamp so it is set in one place
/// (<c>Directory.Build.props</c>) and never hardcoded in the UI or the CLI banner.</summary>
public static class AppVersion
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static string Display => $"Beetle v{Current}";
}
