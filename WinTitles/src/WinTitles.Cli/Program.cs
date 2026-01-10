using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinTitles.Core.Services;
using WinTitles.Core.Services.Subtitles;
using WinTitles.Core.Models;

namespace WinTitles.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("WinTitles CLI - Test Plex scanning and subtitle downloads\n");

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLower();
        
        return command switch
        {
            "login" => await RunLogin(),
            "settings" => ShowSettings(),
            "plex-scan" => await RunPlexScan(args.Length > 1 ? args[1] : null),
            "search" => args.Length > 1 ? await RunSearch(args[1], GetOption(args, "--lang", "en")) : ShowHelp(),
            "search-podnapisi" => args.Length > 1 ? await RunSearchPodnapisi(args[1], GetOption(args, "--lang", "en")) : ShowHelp(),
            "smart-search" => args.Length > 1 ? await RunSmartSearch(args[1], GetOption(args, "--lang", "en")) : ShowHelp(),
            "download" => args.Length > 1 ? await RunDownload(args[1], GetOption(args, "--lang", "en"), GetOption(args, "--query", null)) : ShowHelp(),
            "test-download" => args.Length > 1 ? await RunTestDownload(int.Parse(args[1])) : ShowHelp(),
            "test-podnapisi-download" => args.Length > 1 ? await RunTestPodnapisiDownload(args[1]) : ShowHelp(),
            "help" or "--help" or "-h" => ShowHelp(),
            _ => ShowHelp()
        };
    }

    static string? GetOption(string[] args, string name, string? defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return defaultValue;
    }

    static int ShowHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  login                         Test OpenSubtitles login");
        Console.WriteLine("  settings                      Show current settings");
        Console.WriteLine("  plex-scan [library]           Scan Plex libraries");
        Console.WriteLine("  search <query> [--lang en]    Search OpenSubtitles");
        Console.WriteLine("  search-podnapisi <query> [--lang en]  Search Podnapisi (no auth needed)");
        Console.WriteLine("  smart-search <file> [--lang en]       Smart search with AI fallback");
        Console.WriteLine("  download <file> [--lang en] [--query text]  Download subtitles");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  WinTitles.Cli login");
        Console.WriteLine("  WinTitles.Cli search \"Breaking Bad S01E01\"");
        Console.WriteLine("  WinTitles.Cli search-podnapisi \"The Matrix\" --lang en");
        Console.WriteLine("  WinTitles.Cli download \"C:\\Movies\\movie.mkv\" --lang en");
        Console.WriteLine("  WinTitles.Cli plex-scan Movies");
        return 0;
    }

    static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        
        // Logging to console with detailed output
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole(options =>
            {
                options.FormatterName = "simple";
            });
        });

        // Core services
        services.AddSingleton<SettingsService>();
        services.AddHttpClient<OpenSubtitlesService>();
        services.AddHttpClient<PlexService>();
        services.AddHttpClient<PodnapisiService>();
        services.AddHttpClient<OpenAIService>();
        services.AddSingleton<ScanService>();
        services.AddSingleton<DatabaseService>();
        
        // Subtitle providers (multi-provider like Subliminal)
        services.AddSingleton<ISubtitleProvider, OpenSubtitlesProvider>();
        services.AddSingleton<ISubtitleProvider, PodnapisiProvider>();
        services.AddSingleton<SubtitleAggregator>();

        return services.BuildServiceProvider();
    }

    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "WinTitles", "settings.json");

    static async Task<int> RunPlexScan(string? library)
    {
        Console.WriteLine("=== Plex Library Scan ===\n");
        
        using var services = BuildServices();
        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();
        var plex = services.GetRequiredService<PlexService>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        if (string.IsNullOrEmpty(settings.Settings.Plex.ServerUrl) || 
            string.IsNullOrEmpty(settings.Settings.Plex.Token))
        {
            Console.WriteLine("ERROR: Plex is not configured. Set ServerUrl and Token in settings.");
            Console.WriteLine($"Settings file: {SettingsPath}");
            return 1;
        }

        // Configure the Plex service with settings
        plex.Configure(settings.Settings.Plex.ServerUrl, settings.Settings.Plex.Token);
        Console.WriteLine($"Plex Server: {settings.Settings.Plex.ServerUrl}");
        
        try
        {
            // Get libraries
            Console.WriteLine("\nFetching libraries...");
            var libraries = await plex.GetLibrariesAsync();
            
            if (libraries.Count == 0)
            {
                Console.WriteLine("No libraries found!");
                return 0;
            }

            Console.WriteLine($"Found {libraries.Count} libraries:");
            foreach (var lib in libraries)
            {
                var count = await plex.GetLibraryItemCountAsync(lib.Key);
                Console.WriteLine($"  [{lib.Key}] {lib.Title} ({lib.Type}) - {count} items");
            }

            // Filter to specific library if requested
            var toScan = libraries;
            if (!string.IsNullOrEmpty(library))
            {
                toScan = libraries.Where(l => 
                    l.Title.Equals(library, StringComparison.OrdinalIgnoreCase) ||
                    l.Key == library).ToList();
                
                if (toScan.Count == 0)
                {
                    Console.WriteLine($"\nLibrary '{library}' not found!");
                    return 1;
                }
            }

            // Scan each library
            Console.WriteLine("\n--- Scanning for items ---\n");
            
            foreach (var lib in toScan.Where(l => l.Type == "movie" || l.Type == "show"))
            {
                Console.WriteLine($"Scanning: {lib.Title}...");
                var items = await plex.GetAllMediaItemsAsync(lib.Key);
                
                int withSubs = 0, withoutSubs = 0;
                foreach (var item in items.Take(20)) // Sample first 20
                {
                    var hasSubtitle = CheckForSubtitle(item.FilePath);
                    if (hasSubtitle) withSubs++;
                    else withoutSubs++;
                }
                
                Console.WriteLine($"  Total items: {items.Count}");
                Console.WriteLine($"  Sample (20): {withSubs} with subs, {withoutSubs} missing");
            }

            Console.WriteLine("\nScan complete!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            logger.LogError(ex, "Plex scan failed");
            return 1;
        }
    }

    static bool CheckForSubtitle(string? videoPath)
    {
        if (string.IsNullOrEmpty(videoPath)) return false;
        
        var dir = Path.GetDirectoryName(videoPath);
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        if (dir == null || !Directory.Exists(dir)) return false;
        
        try
        {
            var patterns = new[] { ".srt", ".sub", ".ass", ".ssa", ".vtt" };
            foreach (var ext in patterns)
            {
                var files = Directory.GetFiles(dir, $"{baseName}*{ext}");
                if (files.Length > 0) return true;
            }
        }
        catch { }
        return false;
    }

    static async Task<int> RunDownload(string file, string? lang, string? query)
    {
        Console.WriteLine("=== Subtitle Download ===\n");
        lang ??= "en";
        
        if (!File.Exists(file))
        {
            Console.WriteLine($"ERROR: File not found: {file}");
            return 1;
        }

        Console.WriteLine($"File: {file}");
        Console.WriteLine($"Language: {lang}");
        
        using var services = BuildServices();
        var subtitles = services.GetRequiredService<OpenSubtitlesService>();
        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();
        var logger = services.GetRequiredService<ILogger<Program>>();

        // Check settings
        if (string.IsNullOrEmpty(settings.Settings.OpenSubtitles.ApiKey))
        {
            Console.WriteLine("\nERROR: OpenSubtitles API key not configured.");
            Console.WriteLine($"Settings file: {SettingsPath}");
            return 1;
        }

        try
        {
            // Login first
            Console.WriteLine("\nLogging in to OpenSubtitles...");
            var loggedIn = await subtitles.LoginAsync();
            if (!loggedIn)
            {
                Console.WriteLine("WARNING: Login failed. Downloads may not work.");
            }
            else
            {
                Console.WriteLine("Login successful!");
            }

            // Search
            var searchQuery = query ?? Path.GetFileNameWithoutExtension(file);
            Console.WriteLine($"\nSearching for: {searchQuery}");
            
            var results = await subtitles.SearchAsync(searchQuery, lang);
            
            if (results.Count == 0)
            {
                Console.WriteLine("No subtitles found!");
                
                // Try hash search
                Console.WriteLine("\nTrying hash-based search...");
                results = await subtitles.SearchByHashAsync(file, lang);
                
                if (results.Count == 0)
                {
                    Console.WriteLine("Still no results. Try a different query with --query");
                    return 0;
                }
            }

            Console.WriteLine($"\nFound {results.Count} subtitles:");
            for (int i = 0; i < Math.Min(5, results.Count); i++)
            {
                var r = results[i];
                Console.WriteLine($"  {i+1}. [{r.Language}] {r.FileName} ({r.Downloads} downloads)");
            }

            // Download the best one
            var best = results.OrderByDescending(r => r.Downloads).First();
            Console.WriteLine($"\nDownloading: {best.FileName}...");

            var dir = Path.GetDirectoryName(file) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(file);
            var destPath = Path.Combine(dir, $"{baseName}.{lang}.srt");

            var result = await subtitles.DownloadAsync(best.FileId, destPath);
            
            if (result.Success)
            {
                Console.WriteLine($"SUCCESS! Saved to: {result.FilePath}");
                Console.WriteLine($"Remaining downloads today: {result.RemainingDownloads}");
            }
            else
            {
                Console.WriteLine($"FAILED: {result.Error}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            logger.LogError(ex, "Download failed");
            return 1;
        }
    }

    static async Task<int> RunSearch(string query, string? lang)
    {
        Console.WriteLine("=== Subtitle Search ===\n");
        Console.WriteLine($"Query: {query}");
        Console.WriteLine($"Language: {lang}");

        using var services = BuildServices();
        var subtitles = services.GetRequiredService<OpenSubtitlesService>();
        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();
        lang ??= "en";

        if (string.IsNullOrEmpty(settings.Settings.OpenSubtitles.ApiKey))
        {
            Console.WriteLine("\nERROR: OpenSubtitles API key not configured.");
            return 1;
        }

        try
        {
            Console.WriteLine("\nSearching...");
            var results = await subtitles.SearchAsync(query, lang);

            if (results.Count == 0)
            {
                Console.WriteLine("No subtitles found!");
                return 0;
            }

            Console.WriteLine($"\nFound {results.Count} subtitles:\n");
            Console.WriteLine($"{"#",-3} {"FileId",-10} {"Lang",-5} {"Downloads",-10} {"Filename"}");
            Console.WriteLine(new string('-', 80));
            
            for (int i = 0; i < Math.Min(20, results.Count); i++)
            {
                var r = results[i];
                Console.WriteLine($"{i+1,-3} {r.FileId,-10} {r.Language,-5} {r.Downloads,-10} {r.FileName}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> RunSearchPodnapisi(string query, string? lang)
    {
        Console.WriteLine("=== Podnapisi Search (No Auth Required) ===\n");
        Console.WriteLine($"Query: {query}");
        Console.WriteLine($"Language: {lang}");

        using var services = BuildServices();
        var podnapisi = services.GetRequiredService<PodnapisiService>();
        lang ??= "en";

        try
        {
            Console.WriteLine("\nSearching Podnapisi...");
            var results = await podnapisi.SearchAsync(query, lang);

            if (results.Count == 0)
            {
                Console.WriteLine("No subtitles found!");
                return 0;
            }

            Console.WriteLine($"\nFound {results.Count} subtitles:\n");
            Console.WriteLine($"{"#",-3} {"Id",-12} {"Lang",-5} {"Year",-6} {"Release Info"}");
            Console.WriteLine(new string('-', 80));
            
            for (int i = 0; i < Math.Min(20, results.Count); i++)
            {
                var r = results[i];
                Console.WriteLine($"{i+1,-3} {r.Id,-12} {r.Language,-5} {r.Year?.ToString() ?? "",-6} {r.ReleaseInfo}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> RunSmartSearch(string fileOrQuery, string? lang)
    {
        Console.WriteLine("=== Smart Search (Multi-Provider + AI) ===\n");
        lang ??= "en";
        
        using var services = BuildServices();
        var aggregator = services.GetRequiredService<SubtitleAggregator>();
        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();

        // Check if it's a file path or a query
        bool isFile = File.Exists(fileOrQuery);
        
        if (isFile)
        {
            Console.WriteLine($"File: {fileOrQuery}");
            Console.WriteLine($"Language: {lang}");
            Console.WriteLine("\nRunning smart search (hash + text + AI suggestions)...\n");

            var result = await aggregator.SmartSearchAsync(fileOrQuery, lang);

            Console.WriteLine($"Query used: {result.UsedQuery}");
            Console.WriteLine($"AI suggested: {(result.AiSuggested ? "Yes" : "No")}");
            Console.WriteLine($"Needs user input: {(result.NeedsUserInput ? "Yes" : "No")}");

            if (result.AiSuggestions?.Count > 0)
            {
                Console.WriteLine("\nAI Suggestions:");
                foreach (var s in result.AiSuggestions)
                {
                    Console.WriteLine($"  - {s.Title} ({s.Year}) [{s.Type}]");
                }
            }

            if (result.Results.TotalCount > 0)
            {
                Console.WriteLine($"\nFound {result.Results.TotalCount} subtitles from {result.Results.ResultsByProvider.Count} providers:\n");
                
                foreach (var (provider, subs) in result.Results.ResultsByProvider)
                {
                    Console.WriteLine($"  {provider}: {subs.Count} results");
                }

                Console.WriteLine($"\n{"#",-3} {"Provider",-15} {"Score",-6} {"Downloads",-10} {"Filename"}");
                Console.WriteLine(new string('-', 90));

                var top = result.Results.Results.Take(15).ToList();
                for (int i = 0; i < top.Count; i++)
                {
                    var r = top[i];
                    Console.WriteLine($"{i+1,-3} {r.ProviderName,-15} {r.MatchScore:F2}   {r.Downloads,-10} {r.FileName}");
                }
            }
            else
            {
                Console.WriteLine("\nNo subtitles found!");
                if (result.NeedsUserInput)
                {
                    Console.WriteLine("Try specifying a different query with 'search <query>' command.");
                }
            }
        }
        else
        {
            // Treat as query
            Console.WriteLine($"Query: {fileOrQuery}");
            Console.WriteLine($"Language: {lang}");
            Console.WriteLine("\nSearching all providers...\n");

            var request = new SubtitleSearchRequest
            {
                Query = fileOrQuery,
                Language = lang
            };

            var result = await aggregator.SearchAllAsync(request);

            if (result.TotalCount > 0)
            {
                Console.WriteLine($"Found {result.TotalCount} subtitles:\n");
                
                foreach (var (provider, subs) in result.ResultsByProvider)
                {
                    Console.WriteLine($"  {provider}: {subs.Count} results");
                }

                Console.WriteLine($"\n{"#",-3} {"Provider",-15} {"Score",-6} {"Downloads",-10} {"Filename"}");
                Console.WriteLine(new string('-', 90));

                var top = result.Results.Take(15).ToList();
                for (int i = 0; i < top.Count; i++)
                {
                    var r = top[i];
                    Console.WriteLine($"{i+1,-3} {r.ProviderName,-15} {r.MatchScore:F2}   {r.Downloads,-10} {r.FileName}");
                }
            }
            else
            {
                Console.WriteLine("No subtitles found!");
            }
        }

        return 0;
    }

    static async Task<int> RunLogin()
    {
        Console.WriteLine("=== OpenSubtitles Login Test ===\n");

        using var services = BuildServices();
        var subtitles = services.GetRequiredService<OpenSubtitlesService>();
        var settings = services.GetRequiredService<SettingsService>();
        await settings.LoadAsync();

        Console.WriteLine($"API Key: {(string.IsNullOrEmpty(settings.Settings.OpenSubtitles.ApiKey) ? "NOT SET" : settings.Settings.OpenSubtitles.ApiKey[..8] + "...")}");
        Console.WriteLine($"Username: {settings.Settings.OpenSubtitles.Username}");
        Console.WriteLine($"Password: {(string.IsNullOrEmpty(settings.Settings.OpenSubtitles.Password) ? "NOT SET" : "****")}");

        if (string.IsNullOrEmpty(settings.Settings.OpenSubtitles.ApiKey) ||
            string.IsNullOrEmpty(settings.Settings.OpenSubtitles.Username) ||
            string.IsNullOrEmpty(settings.Settings.OpenSubtitles.Password))
        {
            Console.WriteLine("\nERROR: Missing credentials. Configure in settings.");
            Console.WriteLine($"Settings file: {SettingsPath}");
            return 1;
        }

        try
        {
            Console.WriteLine("\nAttempting login...");
            var success = await subtitles.LoginAsync();
            
            if (success)
            {
                Console.WriteLine("SUCCESS! Login successful.");
                Console.WriteLine($"Token obtained: {(subtitles.IsLoggedIn ? "Yes" : "No")}");
            }
            else
            {
                Console.WriteLine("FAILED! Could not log in.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            return 1;
        }
    }

    static int ShowSettings()
    {
        Console.WriteLine("=== Current Settings ===\n");
        
        Console.WriteLine($"Settings file: {SettingsPath}");
        Console.WriteLine($"File exists: {File.Exists(SettingsPath)}");

        if (!File.Exists(SettingsPath))
        {
            Console.WriteLine("\nNo settings file found. Run the main app to create one.");
            return 0;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            Console.WriteLine($"\n{json}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR reading settings: {ex.Message}");
        }
        return 0;
    }

    static async Task<int> RunTestDownload(int fileId)
    {
        Console.WriteLine("=== Test Download ===\n");
        Console.WriteLine($"FileId: {fileId}");

        using var services = BuildServices();
        var subtitles = services.GetRequiredService<OpenSubtitlesService>();
        var settings = services.GetRequiredService<SettingsService>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        await settings.LoadAsync();

        var destPath = Path.Combine(Path.GetTempPath(), $"test_{fileId}.srt");
        Console.WriteLine($"Destination: {destPath}");
        Console.WriteLine("\nDownloading...");

        var result = await subtitles.DownloadAsync(fileId, destPath);

        if (result.Success)
        {
            Console.WriteLine($"\nSUCCESS!");
            Console.WriteLine($"Saved to: {result.FilePath}");
            Console.WriteLine($"Remaining downloads: {result.RemainingDownloads}");
            
            // Show first few lines of the subtitle
            if (File.Exists(destPath))
            {
                var lines = File.ReadLines(destPath).Take(10).ToList();
                Console.WriteLine($"\nFirst 10 lines:");
                foreach (var line in lines)
                {
                    Console.WriteLine($"  {line}");
                }
            }
        }
        else
        {
            Console.WriteLine($"\nFAILED: {result.Error}");
        }

        return result.Success ? 0 : 1;
    }

    static async Task<int> RunTestPodnapisiDownload(string subtitleIdOrUrl)
    {
        Console.WriteLine("=== Test Podnapisi Download ===\n");
        Console.WriteLine($"Input: {subtitleIdOrUrl}");

        using var services = BuildServices();
        var podnapisi = services.GetRequiredService<PodnapisiService>();

        // Use a clean filename
        var safeId = subtitleIdOrUrl.Contains("/") 
            ? subtitleIdOrUrl.Split('/').Last(s => !string.IsNullOrEmpty(s) && s != "download")
            : subtitleIdOrUrl;
        var destPath = Path.Combine(Path.GetTempPath(), $"podnapisi_test_{safeId}.srt");
        Console.WriteLine($"Destination: {destPath}");
        Console.WriteLine("\nDownloading...");

        var result = await podnapisi.DownloadAsync(subtitleIdOrUrl, destPath);

        if (result.Success)
        {
            Console.WriteLine($"\nSUCCESS!");
            Console.WriteLine($"Saved to: {result.FilePath}");
            
            if (File.Exists(destPath))
            {
                var lines = File.ReadLines(destPath).Take(10).ToList();
                Console.WriteLine($"\nFirst 10 lines:");
                foreach (var line in lines)
                {
                    Console.WriteLine($"  {line}");
                }
            }
        }
        else
        {
            Console.WriteLine($"\nFAILED: {result.Error}");
        }

        return result.Success ? 0 : 1;
    }
}
