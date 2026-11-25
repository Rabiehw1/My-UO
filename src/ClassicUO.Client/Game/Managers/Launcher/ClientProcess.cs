#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ClassicUO.Game.Managers.Launcher;

/// <summary>Builds the command that starts another copy of this client.</summary>
internal static class ClientProcess
{
    /// <summary>Start info for this client's executable, run from the install folder, with no arguments yet.</summary>
    /// <returns>A start info the caller adds arguments to.</returns>
    /// <exception cref="InvalidOperationException">The executable path is unknown.</exception>
    public static ProcessStartInfo CreateStartInfo()
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown.");

        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = CUOEnviroment.ExecutablePath
        };

        // Started as "dotnet LordBritishKW.dll": the host needs the dll path back as its first argument.
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        return startInfo;
    }
}
