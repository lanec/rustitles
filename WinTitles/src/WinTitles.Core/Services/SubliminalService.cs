using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WinTitles.Core.Services;

/// <summary>
/// Service for interacting with the Subliminal Python CLI to download subtitles.
/// </summary>
public partial class SubliminalService
{
    private readonly ILogger<SubliminalService> _logger;
    private string? _subliminalPath;

    public SubliminalService(ILogger<SubliminalService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if Subliminal is installed and accessible.
    /// </summary>
    public async Task<bool> IsInstalledAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await RunCommandAsync("subliminal", "--version", ct);
            if (result.ExitCode == 0)
            {
                _subliminalPath = "subliminal";
                return true;
            }
        }
        catch { }

        // Try pipx path
        var pipxPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "pipx", "venvs", "subliminal", "Scripts", "subliminal.exe");
        
        if (File.Exists(pipxPath))
        {
            _subliminalPath = pipxPath;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the installed Subliminal version.
    /// </summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var cmd = _subliminalPath ?? "subliminal";
        var result = await RunCommandAsync(cmd, "--version", ct);
        if (result.ExitCode == 0)
        {
            var match = VersionRegex().Match(result.Output);
            return match.Success ? match.Groups[1].Value : result.Output.Trim();
        }
        return null;
    }

    /// <summary>
    /// Download subtitles for a video file.
    /// </summary>
    public async Task<SubtitleResult> DownloadSubtitleAsync(
        string videoPath, 
        string language,
        bool force = false,
        CancellationToken ct = default)
    {
        var cmd = _subliminalPath ?? "subliminal";
        var args = $"download -l {language}";
        if (force) args += " -f";
        args += $" \"{videoPath}\"";

        _logger.LogInformation("Downloading subtitle for {Path} [{Lang}]", 
            Path.GetFileName(videoPath), language);

        var result = await RunCommandAsync(cmd, args, ct);

        // Parse output to determine result
        var output = result.Output + result.Error;
        
        if (result.ExitCode == 0 && output.Contains("Downloaded 1 subtitle"))
        {
            // Find the subtitle file
            var dir = Path.GetDirectoryName(videoPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(videoPath);
            var subtitlePath = Directory.GetFiles(dir, $"{baseName}*.srt")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            _logger.LogInformation("Successfully downloaded subtitle: {Path}", subtitlePath);
            
            return new SubtitleResult
            {
                Success = true,
                SubtitlePath = subtitlePath,
                Language = language
            };
        }
        else if (output.Contains("Downloaded 0 subtitle"))
        {
            _logger.LogWarning("No subtitles found for {Path}", Path.GetFileName(videoPath));
            return new SubtitleResult
            {
                Success = false,
                Error = "No subtitles found",
                Language = language
            };
        }
        else
        {
            _logger.LogError("Subtitle download failed: {Error}", output);
            return new SubtitleResult
            {
                Success = false,
                Error = output.Length > 200 ? output[..200] : output,
                Language = language
            };
        }
    }

    private static async Task<ProcessResult> RunCommandAsync(
        string command, 
        string arguments, 
        CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        
        await process.WaitForExitAsync(ct);
        
        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            Output = await outputTask,
            Error = await errorTask
        };
    }

    [GeneratedRegex(@"subliminal[,\s]+version\s+([\d.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}

public class SubtitleResult
{
    public bool Success { get; init; }
    public string? SubtitlePath { get; init; }
    public string? Error { get; init; }
    public required string Language { get; init; }
}

internal class ProcessResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
}
