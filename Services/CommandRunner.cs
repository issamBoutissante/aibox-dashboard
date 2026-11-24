using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AiBoxDashboard.Services;

/// <summary>
/// Runs a command on this machine, or on another one through the system <c>ssh</c> client.
/// Remote runs rely on the user's own SSH setup (keys, ~/.ssh/config); no passwords are handled here.
/// </summary>
public sealed partial class CommandRunner
{
    private readonly string _sshHost;

    public CommandRunner(string? sshHost)
    {
        _sshHost = (sshHost ?? "").Trim();
        if (_sshHost.Length > 0 && !HostPattern().IsMatch(_sshHost))
            throw new ArgumentException("SSH host may only contain letters, digits, '.', '_', '-' and '@'.");
    }

    public bool IsRemote => _sshHost.Length > 0;

    public async Task<string> RunAsync(string program, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (IsRemote)
        {
            psi.FileName = "ssh";
            foreach (var option in SshOptions())
                psi.ArgumentList.Add(option);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(_sshHost);
            psi.ArgumentList.Add(string.Join(' ', args.Prepend(program).Select(ShellQuote)));
        }
        else
        {
            psi.FileName = program;
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {psi.FileName}.");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"{psi.FileName} is not installed or not on the PATH.");
        }

        using (process)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(timeout);
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderr = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                var output = await stdout;
                var error = await stderr;

                if (process.ExitCode != 0)
                {
                    var firstLine = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    throw new InvalidOperationException(firstLine ?? $"{program} exited with code {process.ExitCode}.");
                }

                return output;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"{program} did not answer within {timeout.TotalSeconds:0} s.");
            }
        }
    }

    private static IEnumerable<string> SshOptions()
    {
        yield return "-o"; yield return "BatchMode=yes";
        yield return "-o"; yield return "ConnectTimeout=5";

        // Reuse one connection between polls instead of a new handshake every few seconds.
        if (!OperatingSystem.IsWindows())
        {
            yield return "-o"; yield return "ControlMaster=auto";
            yield return "-o"; yield return "ControlPath=~/.ssh/aibox-%C";
            yield return "-o"; yield return "ControlPersist=60";
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._@-]*$")]
    private static partial Regex HostPattern();
}
