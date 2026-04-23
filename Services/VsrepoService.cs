using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using VSRepo_Gui.Models;

namespace VSRepo_Gui.Services;

public sealed class VsrepoService
{
    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => string.Join(Environment.NewLine, new[] { StdOut, StdErr }.Where(static x => !string.IsNullOrWhiteSpace(x)));
    }

    public sealed record ProbeResult(
        bool Success,
        string Message,
        string ResolvedPython,
        bool HasVapourSynth,
        bool HasVsrepo,
        string? VapourSynthPath,
        string? VsrepoPath,
        string? PluginDir);

    public sealed record InstalledPackageInfo(string Identifier, string InstalledVersion, PackageInstallState State);

    public sealed record VsrepoPaths(string Definitions, string Binaries, string Scripts, string? DistInfos);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<string>> DetectPythonCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var probe in EnumerateCommonPythonCandidates())
        {
            if (File.Exists(probe) && seen.Add(probe))
            {
                candidates.Add(probe);
            }
        }

        var whereResult = await RunProcessAsync("where.exe", ["python"], cancellationToken);
        foreach (var line in whereResult.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(line))
            {
                candidates.Add(line.Trim());
            }
        }

        return candidates;
    }

    private static IEnumerable<string> EnumerateCommonPythonCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
        }
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            IEnumerable<string> directories;
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                directories = Directory.EnumerateDirectories(root, "Python*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                yield return Path.Combine(directory, "python.exe");
            }
        }
    }

    public async Task<ProbeResult> ProbeAsync(string pythonExe, CancellationToken cancellationToken = default)
    {
        var code = string.Join("\n", new[]
        {
            "import importlib.util, json, sys",
            "result = {'python': sys.executable, 'has_vapoursynth': False, 'has_vsrepo': False, 'vapoursynth_path': None, 'vsrepo_path': None, 'plugin_dir': None, 'vapoursynth_error': None}",
            "try:",
            "    import vapoursynth as vs",
            "    result['has_vapoursynth'] = True",
            "    result['vapoursynth_path'] = getattr(vs, '__file__', None)",
            "    result['plugin_dir'] = vs.get_plugin_dir()",
            "except Exception as exc:",
            "    result['vapoursynth_error'] = str(exc)",
            "spec = importlib.util.find_spec('vsrepo.vsrepo')",
            "result['has_vsrepo'] = spec is not None",
            "result['vsrepo_path'] = None if spec is None else spec.origin",
            "print(json.dumps(result))",
        });

        var command = await RunProcessAsync(pythonExe, ["-c", code], cancellationToken);
        if (command.ExitCode != 0)
        {
            return new ProbeResult(false, command.CombinedOutput, pythonExe, false, false, null, null, null);
        }

        using var document = JsonDocument.Parse(command.StdOut);
        var root = document.RootElement;
        var hasVs = root.GetProperty("has_vapoursynth").GetBoolean();
        var hasVsrepo = root.GetProperty("has_vsrepo").GetBoolean();
        var resolvedPython = root.GetProperty("python").GetString() ?? pythonExe;
        var vapoursynthPath = root.GetProperty("vapoursynth_path").GetString();
        var vsrepoPath = root.GetProperty("vsrepo_path").GetString();
        var pluginDir = root.GetProperty("plugin_dir").GetString();
        var vapoursynthError = root.GetProperty("vapoursynth_error").GetString();

        var success = hasVs && hasVsrepo;
        var message = success
            ? $"Python OK | VapourSynth OK | VSRepo OK"
            : $"Python OK | VapourSynth {(hasVs ? "OK" : "Missing")} | VSRepo {(hasVsrepo ? "OK" : "Missing")}";

        if (!string.IsNullOrWhiteSpace(vapoursynthError))
        {
            message += Environment.NewLine + vapoursynthError;
        }

        return new ProbeResult(success, message, resolvedPython, hasVs, hasVsrepo, vapoursynthPath, vsrepoPath, pluginDir);
    }

    public async Task<CommandResult> RunVsrepoAsync(string pythonExe, string target, string operation, IEnumerable<string>? packages = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var args = BuildVsrepoArguments(target, operation, packages, force);
        return await RunProcessAsync(pythonExe, args, cancellationToken);
    }

    public async Task<CommandResult> RunVsrepoElevatedAsync(string pythonExe, string target, string operation, IEnumerable<string>? packages = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var args = BuildVsrepoArguments(target, operation, packages, force);
        var workDir = Path.Combine(Path.GetTempPath(), "VSRepo_Gui");
        Directory.CreateDirectory(workDir);

        var token = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(workDir, $"run_{token}.ps1");
        var outputPath = Path.Combine(workDir, $"out_{token}.log");
        var exitCodePath = Path.Combine(workDir, $"code_{token}.txt");

        try
        {
            var escapedArgs = string.Join(", ", args.Select(static a => "'" + a.Replace("'", "''") + "'"));
            var script = $$"""
$ErrorActionPreference = 'Continue'
$python = '{{pythonExe.Replace("'", "''")}}'
$arguments = @({{escapedArgs}})
& $python @arguments *>'{{outputPath.Replace("'", "''")}}'
Set-Content -Path '{{exitCodePath.Replace("'", "''")}}' -Value $LASTEXITCODE -Encoding UTF8
""";
            await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8, cancellationToken);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            var std = File.Exists(outputPath)
                ? await File.ReadAllTextAsync(outputPath, cancellationToken)
                : string.Empty;
            var exitCode = 1;
            if (File.Exists(exitCodePath))
            {
                var rawExitCode = await File.ReadAllTextAsync(exitCodePath, cancellationToken);
                _ = int.TryParse(rawExitCode.Trim(), out exitCode);
            }

            return new CommandResult(exitCode, std, string.Empty);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new CommandResult(1223, string.Empty, "Elevation canceled by user.");
        }
        finally
        {
            TryDelete(scriptPath);
            TryDelete(outputPath);
            TryDelete(exitCodePath);
        }
    }

    public async Task<VsrepoPaths> GetPathsAsync(string pythonExe, string target, CancellationToken cancellationToken = default)
    {
        var result = await RunVsrepoAsync(pythonExe, target, "paths", cancellationToken: cancellationToken);
        EnsureSuccess(result, "vsrepo paths");

        string definitions = string.Empty;
        string binaries = string.Empty;
        string scripts = string.Empty;
        string? distInfos = null;

        foreach (var rawLine in result.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Definitions:", StringComparison.OrdinalIgnoreCase))
            {
                definitions = line["Definitions:".Length..].Trim();
            }
            else if (line.StartsWith("Binaries:", StringComparison.OrdinalIgnoreCase))
            {
                binaries = line["Binaries:".Length..].Trim();
            }
            else if (line.StartsWith("Scripts:", StringComparison.OrdinalIgnoreCase))
            {
                scripts = line["Scripts:".Length..].Trim();
            }
            else if (line.StartsWith("Dist-Infos:", StringComparison.OrdinalIgnoreCase))
            {
                distInfos = line["Dist-Infos:".Length..].Trim();
            }
        }

        return new VsrepoPaths(definitions, binaries, scripts, distInfos);
    }

    public async Task<Dictionary<string, InstalledPackageInfo>> GetInstalledAsync(string pythonExe, string target, CancellationToken cancellationToken = default)
    {
        var result = await RunVsrepoAsync(pythonExe, target, "installed", cancellationToken: cancellationToken);
        EnsureSuccess(result, "vsrepo installed");

        var installed = new Dictionary<string, InstalledPackageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in result.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) || line.StartsWith("-"))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 5)
            {
                continue;
            }

            var status = PackageInstallState.Installed;
            var nameToken = tokens[0];
            if (nameToken.StartsWith('*'))
            {
                status = PackageInstallState.UpdateAvailable;
            }
            else if (nameToken.StartsWith('+'))
            {
                status = PackageInstallState.InstalledUnknown;
            }

            var identifier = tokens[^1];
            var installedVersion = tokens[^3];
            installed[identifier] = new InstalledPackageInfo(identifier, installedVersion, status);
        }

        return installed;
    }

    public VsPackageRoot LoadDefinitions(string definitionsPath)
    {
        var json = File.ReadAllText(definitionsPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<VsPackageRoot>(json, JsonOptions)
               ?? new VsPackageRoot();
    }

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool CanWriteToPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var targetDirectory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return false;
            }

            var probe = Path.Combine(targetDirectory, $".vsrepo_gui_write_{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            if (File.Exists(probe))
            {
                File.Delete(probe);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsPermissionDenied(CommandResult result)
    {
        return result.CombinedOutput.Contains("PermissionError", StringComparison.OrdinalIgnoreCase)
               || result.CombinedOutput.Contains("WinError 5", StringComparison.OrdinalIgnoreCase)
               || result.CombinedOutput.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
               || result.CombinedOutput.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSuccess(CommandResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{operation} failed:{Environment.NewLine}{result.CombinedOutput}");
        }
    }

    private static List<string> BuildVsrepoArguments(string target, string operation, IEnumerable<string>? packages, bool force)
    {
        var args = new List<string> { "-m", "vsrepo.vsrepo" };
        if (force)
        {
            args.Add("-f");
        }

        args.Add("-t");
        args.Add(target);
        args.Add(operation);

        if (packages is not null)
        {
            args.AddRange(packages.Where(static x => !string.IsNullOrWhiteSpace(x)));
        }

        return args;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static async Task<CommandResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        return new CommandResult(process.ExitCode, stdOut, stdErr);
    }
}



