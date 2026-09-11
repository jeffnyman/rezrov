using System.Reflection;

namespace Rezrov.Core;

/// <summary>
/// The version the programs report, taken from the assembly so that it
/// is the one Directory.Build.props gave the build and nothing else.
/// </summary>
public static class ProgramVersion
{
    /// <summary>
    /// The version as written in Directory.Build.props, without the
    /// commit suffix the build appends to the informational version.
    /// </summary>
    public static string Current
    {
        get
        {
            var informational = typeof(ProgramVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(informational))
            {
                return typeof(ProgramVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }

            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }
    }
}
