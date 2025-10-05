//! Python and Subliminal installation and management utilities
//! 
//! This module handles Python installation, Subliminal setup, and environment
//! configuration for the subtitle downloading functionality.

use std::env;
use std::io;
use std::io::Write;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use log::{info, warn, error};

// Use the logging macros directly from the crate root
use crate::debug;

// Windows-specific imports
#[cfg(windows)]
use crate::PYTHON_INSTALLER_URL;

// Windows-specific imports
#[cfg(windows)]
use std::fs::File;
#[cfg(windows)]
use std::ptr::null_mut;
#[cfg(windows)]
use winreg::enums::*;
#[cfg(windows)]
use winreg::RegKey;
#[cfg(windows)]
use windows::Win32::Foundation::{WPARAM, LPARAM};
#[cfg(windows)]
use windows::Win32::UI::WindowsAndMessaging::{SendMessageTimeoutW, HWND_BROADCAST, WM_SETTINGCHANGE, SMTO_ABORTIFHUNG};

// Unix-specific imports (Linux and macOS)
#[cfg(any(target_os = "linux", target_os = "macos"))]
use dirs;

/// Python and Subliminal installation and management utilities
pub struct PythonManager;

impl PythonManager {
    /// Check if Python is installed and return its version
    pub fn get_version() -> Option<String> {
        // On macOS, check Homebrew paths first, then system python3
        #[cfg(target_os = "macos")]
        let commands = vec![
            "/opt/homebrew/bin/python3",  // Apple Silicon Homebrew
            "/usr/local/bin/python3",     // Intel Mac Homebrew
            "python3",
            "python",
            "py"
        ];
        
        // On Linux, check python3 first, then python, then py
        #[cfg(target_os = "linux")]
        let commands = vec!["python3", "python", "py"];
        
        // On Windows
        #[cfg(windows)]
        let commands = vec!["python", "py", "python3"];
        
        for cmd in &commands {
            if let Ok(output) = Self::run_command_hidden(cmd, &["--version"], &std::collections::HashMap::new()) {
                if output.status.success() {
                    let stdout = String::from_utf8_lossy(&output.stdout).trim().to_string();
                    let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
                    let version = if !stdout.is_empty() { stdout } else { stderr };
                    debug!("Python version output for {}: {}", cmd, version);
                    // Only accept Python 3.x.y
                    if version.starts_with("Python 3.") {
                        debug!("Found valid Python 3 version: {} using command: {}", version, cmd);
                        return Some(version);
                    }
                }
            }
        }
        debug!("No valid Python 3 installation found");
        None
    }

    /// Check if Subliminal is installed
    pub fn is_subliminal_installed() -> bool {
        // First check if subliminal command is directly available (works for both pip and pipx installations)
        if let Ok(output) = Self::run_command_hidden("subliminal", &["--version"], &std::collections::HashMap::new()) {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let stderr = String::from_utf8_lossy(&output.stderr);
            debug!("subliminal --version stdout: {} | stderr: {}", stdout, stderr);
            if output.status.success() && (stdout.contains("subliminal") || stderr.contains("subliminal")) {
                debug!("Subliminal found as direct command");
                return true;
            }
        }
        
        // Check if installed via pipx
        if let Ok(output) = Self::run_command_hidden("pipx", &["list"], &std::collections::HashMap::new()) {
            let stdout = String::from_utf8_lossy(&output.stdout);
            debug!("pipx list output: {}", stdout);
            if output.status.success() && stdout.to_lowercase().contains("subliminal") {
                debug!("Subliminal found via pipx list");
                return true;
            }
        }
        
        // Then check as Python module with multiple Python commands (for pip installations)
        for cmd in &["python3", "python", "py"] {
            if let Ok(output) = Self::run_command_hidden(cmd, &["-m", "pip", "show", "subliminal"], &std::collections::HashMap::new()) {
                let stdout = String::from_utf8_lossy(&output.stdout);
                debug!("{} -m pip show subliminal output: {}", cmd, stdout);
                if output.status.success() && stdout.contains("Name: subliminal") {
                    debug!("Subliminal found via pip show using {}", cmd);
                    return true;
                }
            }
            // Also try direct module import
            if let Ok(output) = Self::run_command_hidden(cmd, &["-c", "import subliminal; print('subliminal available')"], &std::collections::HashMap::new()) {
                let stdout = String::from_utf8_lossy(&output.stdout);
                debug!("{} -c import subliminal output: {}", cmd, stdout);
                if output.status.success() && stdout.contains("subliminal available") {
                    debug!("Subliminal found via direct import using {}", cmd);
                    return true;
                }
            }
        }
        debug!("Subliminal not found");
        false
    }

    /// Install Subliminal via pipx (Linux) or pip (Windows/macOS)
    pub fn install_subliminal() -> bool {
        #[cfg(windows)]
        {
            info!("Installing Subliminal via pip on Windows");
            for cmd in &["python", "py", "python3"] {
                if let Ok(output) = Self::run_command_hidden(cmd, &["-m", "pip", "install", "subliminal"], &std::collections::HashMap::new()) {
                    if output.status.success() {
                        info!("Subliminal installed successfully using {}", cmd);
                        return true;
                    } else {
                        let stderr = String::from_utf8_lossy(&output.stderr);
                        warn!("Failed to install Subliminal using {}: {}", cmd, stderr);
                    }
                }
            }
            error!("Failed to install Subliminal with all Python commands");
            false
        }
        
        #[cfg(target_os = "macos")]
        {
            info!("Installing Subliminal via pip on macOS");
            
            // Try Homebrew Python paths first, then system python3
            let python_commands = vec![
                "/opt/homebrew/bin/python3",
                "/usr/local/bin/python3",
                "python3",
                "python"
            ];
            
            for cmd in &python_commands {
                if let Ok(output) = Self::run_command_hidden(cmd, &["-m", "pip", "install", "--user", "subliminal"], &std::collections::HashMap::new()) {
                    if output.status.success() {
                        info!("Subliminal installed successfully using {}", cmd);
                        return true;
                    } else {
                        let stderr = String::from_utf8_lossy(&output.stderr);
                        warn!("Failed to install Subliminal using {}: {}", cmd, stderr);
                    }
                }
            }
            error!("Failed to install Subliminal with all Python commands");
            false
        }
        
        #[cfg(target_os = "linux")]
        {
            info!("Installing Subliminal via pipx on Linux");
            
            // First, try to install pipx if it's not available
            if let Ok(output) = Self::run_command_hidden("pipx", &["--version"], &std::collections::HashMap::new()) {
                if !output.status.success() {
                    info!("pipx not found, attempting to install pipx first");
                    // Try to install pipx using different methods
                    let pipx_install_attempts = [
                        ("python3", vec!["-m", "pip", "install", "--user", "pipx"]),
                        ("python", vec!["-m", "pip", "install", "--user", "pipx"]),
                        ("apt", vec!["install", "-y", "python3-pipx"]),
                        ("dnf", vec!["install", "-y", "python3-pipx"]),
                        ("pacman", vec!["-S", "--noconfirm", "python-pipx"]),
                    ];
                    
                    for (cmd, args) in &pipx_install_attempts {
                        let args_refs: Vec<&str> = args.iter().map(|s| &**s).collect();
                        if let Ok(output) = Self::run_command_hidden(cmd, &args_refs, &std::collections::HashMap::new()) {
                            if output.status.success() {
                                info!("pipx installed successfully using {}", cmd);
                                break;
                            }
                        }
                    }
                }
            }
            
            // Now try to install subliminal using pipx
            if let Ok(output) = Self::run_command_hidden("pipx", &["install", "subliminal"], &std::collections::HashMap::new()) {
                if output.status.success() {
                    info!("Subliminal installed successfully using pipx");
                    return true;
                } else {
                    let stderr = String::from_utf8_lossy(&output.stderr);
                    warn!("Failed to install Subliminal using pipx: {}", stderr);
                }
            }
            
            // Fallback to pip install if pipx fails
            info!("pipx installation failed, trying pip install as fallback");
            for cmd in &["python3", "python"] {
                if let Ok(output) = Self::run_command_hidden(cmd, &["-m", "pip", "install", "--user", "subliminal"], &std::collections::HashMap::new()) {
                    if output.status.success() {
                        info!("Subliminal installed successfully using {} pip", cmd);
                        return true;
                    } else {
                        let stderr = String::from_utf8_lossy(&output.stderr);
                        warn!("Failed to install Subliminal using {} pip: {}", cmd, stderr);
                    }
                }
            }
            
            error!("Failed to install Subliminal with pipx and pip fallback");
            false
        }
    }

    /// Add Python Scripts directory to PATH
    pub fn add_scripts_to_path() -> Result<(), String> {
        #[cfg(windows)]
        {
            let mut base_path = None;

            for cmd in &["python", "py"] {
                let output = Self::run_command_hidden(cmd, &["-m", "site", "--user-base"], &std::collections::HashMap::new());

                match output {
                    Ok(output) if output.status.success() => {
                        let path = String::from_utf8_lossy(&output.stdout).trim().to_string();
                        if !path.is_empty() {
                            base_path = Some(path);
                            break;
                        }
                    }
                    Ok(output) => {
                        let err = String::from_utf8_lossy(&output.stderr);
                        eprintln!("Failed to get user base with {}: {}", cmd, err);
                    }
                    Err(e) => {
                        eprintln!("Failed to execute {}: {}", cmd, e);
                    }
                }
            }

            let base_path = base_path.ok_or_else(|| "Failed to get user base path from python/py".to_string())?;
            let scripts_path = format!("{}\\Scripts", base_path);

            let hkcu = RegKey::predef(HKEY_CURRENT_USER);
            let env = hkcu.open_subkey_with_flags("Environment", KEY_READ | KEY_WRITE)
                .map_err(|e| format!("Failed to open registry: {}", e))?;

            let current_path: String = env.get_value("Path").unwrap_or_else(|_| "".into());

            if !current_path.to_lowercase().contains(&scripts_path.to_lowercase()) {
                let new_path = if current_path.trim().is_empty() {
                    scripts_path.clone()
                } else {
                    format!("{current_path};{scripts_path}")
                };

                env.set_value("Path", &new_path)
                    .map_err(|e| format!("Failed to set PATH: {}", e))?;

                unsafe {
                    let param = "Environment\0"
                        .encode_utf16()
                        .collect::<Vec<u16>>();

                    SendMessageTimeoutW(
                        HWND_BROADCAST,
                        WM_SETTINGCHANGE,
                        WPARAM(0),
                        LPARAM(param.as_ptr() as isize),
                        SMTO_ABORTIFHUNG,
                        5000,
                        Some(null_mut()),
                    );
                }
            }

            Ok(())
        }
        
        #[cfg(target_os = "macos")]
        {
            // On macOS, add Homebrew and Python user paths
            let home_dir = dirs::home_dir().ok_or_else(|| "Failed to get home directory".to_string())?;
            let mut paths_to_add = Vec::new();
            
            // Homebrew paths
            if std::path::Path::new("/opt/homebrew/bin").exists() {
                paths_to_add.push("/opt/homebrew/bin".to_string());
            }
            if std::path::Path::new("/usr/local/bin").exists() {
                paths_to_add.push("/usr/local/bin".to_string());
            }
            
            // Python user scripts directory
            let local_bin = home_dir.join("Library").join("Python");
            if local_bin.exists() {
                if let Ok(entries) = std::fs::read_dir(&local_bin) {
                    for entry in entries.flatten() {
                        let bin_path = entry.path().join("bin");
                        if bin_path.exists() {
                            paths_to_add.push(bin_path.to_string_lossy().to_string());
                        }
                    }
                }
            }
            
            let current_path = env::var("PATH").unwrap_or_default();
            for path in paths_to_add {
                if !current_path.contains(&path) {
                    let new_path = format!("{}:{}", path, current_path);
                    env::set_var("PATH", new_path);
                }
            }
            
            Ok(())
        }
        
        #[cfg(target_os = "linux")]
        {
            // On Linux, Python scripts are typically already in PATH via pip
            // Just ensure the user's local bin directory is in PATH
            let home_dir = dirs::home_dir().ok_or_else(|| "Failed to get home directory".to_string())?;
            let local_bin = home_dir.join(".local").join("bin");
            
            if local_bin.exists() {
                // Add to current process PATH
                let current_path = env::var("PATH").unwrap_or_default();
                if !current_path.contains(local_bin.to_string_lossy().as_ref()) {
                    let new_path = format!("{}:{}", local_bin.display(), current_path);
                    env::set_var("PATH", new_path);
                }
            }
            
            Ok(())
        }
    }

    /// Refresh environment variables to pick up PATH changes
    pub fn refresh_environment() -> Result<(), String> {
        #[cfg(windows)]
        {
            // Get the updated PATH from registry
            let hkcu = RegKey::predef(HKEY_CURRENT_USER);
            let env = hkcu.open_subkey_with_flags("Environment", KEY_READ)
                .map_err(|e| format!("Failed to open registry: {}", e))?;

            let user_path: String = env.get_value("Path").unwrap_or_else(|_| "".into());
            
            // Get system PATH
            let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
            let sys_env = hklm.open_subkey_with_flags("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment", KEY_READ)
                .map_err(|e| format!("Failed to open system registry: {}", e))?;
            
            let system_path: String = sys_env.get_value("Path").unwrap_or_else(|_| "".into());
            
            // Combine system and user paths
            let combined_path = if system_path.trim().is_empty() {
                user_path
            } else if user_path.trim().is_empty() {
                system_path
            } else {
                format!("{system_path};{user_path}")
            };
            
            // Update current process environment
            std::env::set_var("PATH", combined_path);
            
            Ok(())
        }
        
        #[cfg(target_os = "macos")]
        {
            // On macOS, add both Homebrew and user local paths
            let home_dir = dirs::home_dir().ok_or_else(|| "Failed to get home directory".to_string())?;
            let mut paths_to_add = Vec::new();
            
            // Add Homebrew paths
            let homebrew_paths = vec![
                "/opt/homebrew/bin",      // Apple Silicon
                "/usr/local/bin",         // Intel Mac
            ];
            
            for path in homebrew_paths {
                if std::path::Path::new(path).exists() {
                    paths_to_add.push(path.to_string());
                }
            }
            
            // Add user local bin
            let local_bin = home_dir.join("Library").join("Python");
            if local_bin.exists() {
                // Python on macOS installs to ~/Library/Python/3.x/bin
                if let Ok(entries) = std::fs::read_dir(&local_bin) {
                    for entry in entries.flatten() {
                        let bin_path = entry.path().join("bin");
                        if bin_path.exists() {
                            paths_to_add.push(bin_path.to_string_lossy().to_string());
                        }
                    }
                }
            }
            
            let current_path = env::var("PATH").unwrap_or_default();
            let mut new_path_parts = paths_to_add;
            new_path_parts.push(current_path);
            let new_path = new_path_parts.join(":");
            env::set_var("PATH", new_path);
            
            Ok(())
        }
        
        #[cfg(target_os = "linux")]
        {
            // On Linux, reload environment from shell profile
            let home_dir = dirs::home_dir().ok_or_else(|| "Failed to get home directory".to_string())?;
            let local_bin = home_dir.join(".local").join("bin");
            
            if local_bin.exists() {
                let current_path = env::var("PATH").unwrap_or_default();
                if !current_path.contains(local_bin.to_string_lossy().as_ref()) {
                    let new_path = format!("{}:{}", local_bin.display(), current_path);
                    env::set_var("PATH", new_path);
                }
            }
            
            Ok(())
        }
    }

    #[cfg(windows)]
    /// Download Python installer from official website
    pub fn download_installer() -> io::Result<PathBuf> {
        let url = PYTHON_INSTALLER_URL;
        let response = reqwest::blocking::get(url).map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;

        let temp_dir = env::temp_dir();
        let installer_path = temp_dir.join("python-installer.exe");
        let mut file = File::create(&installer_path)?;
        let bytes = response.bytes().map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;
        file.write_all(&bytes)?;
        Ok(installer_path)
    }

    #[cfg(windows)]
    /// Install Python silently with required options
    pub fn install_silent(_installer_path: &PathBuf) -> io::Result<bool> {
        let mut command = Command::new(_installer_path);
        command.args(&[
            "/quiet",
            "InstallAllUsers=1",
            "PrependPath=1",
            "Include_pip=1",
        ]);
        
        // On Windows, try to hide the console window
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
        
        let status = command.status()?;
        Ok(status.success())
    }

    /// Ensure Subliminal cache directory exists with proper permissions
    pub fn ensure_cache_dir() -> io::Result<PathBuf> {
        let cache_dir = env::temp_dir().join("subliminal_cache");
        
        // Create the directory if it doesn't exist
        if !cache_dir.exists() {
            std::fs::create_dir_all(&cache_dir)?;
        }
        
        // On Windows, try to set proper permissions and clean up any corrupted cache files
        #[cfg(windows)]
        {
            // Clean up any existing DBM cache files that might be corrupted
            let cache_files = ["cache.dbm", "cache.dir", "cache.pag", "cache.db"];
            for file_name in &cache_files {
                let cache_file = cache_dir.join(file_name);
                if cache_file.exists() {
                    // Try to remove corrupted cache files
                    let _ = std::fs::remove_file(&cache_file);
                }
            }
        }
        
        Ok(cache_dir)
    }

    /// Clean up corrupted cache files (call this when DBM errors persist)
    pub fn cleanup_cache() -> io::Result<()> {
        let cache_dir = env::temp_dir().join("subliminal_cache");
        if cache_dir.exists() {
            // Remove all cache files to force a fresh start
            let cache_files = ["cache.dbm", "cache.dir", "cache.pag", "cache.db", "cache"];
            for file_name in &cache_files {
                let cache_file = cache_dir.join(file_name);
                if cache_file.exists() {
                    let _ = std::fs::remove_file(&cache_file);
                }
            }
            // Also try to remove the directory and recreate it
            let _ = std::fs::remove_dir_all(&cache_dir);
            std::fs::create_dir_all(&cache_dir)?;
        }
        Ok(())
    }

    /// Run a command with hidden console window
    pub fn run_command_hidden(cmd: &str, args: &[&str], env_vars: &std::collections::HashMap<String, String>) -> io::Result<std::process::Output> {
        let mut command = Command::new(cmd);
        command.envs(env_vars);
        command.args(args);
        command.stdout(Stdio::piped());
        command.stderr(Stdio::piped());
        
        // On Windows, try to hide the console window
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000); // CREATE_NO_WINDOW
        }
        
        // On Unix systems, we redirect output
        #[cfg(any(target_os = "linux", target_os = "macos"))]
        {
            // Set environment variables to suppress some output
            #[cfg(target_os = "linux")]
            command.env("DEBIAN_FRONTEND", "noninteractive");
            command.env("PYTHONUNBUFFERED", "1");
        }
        
        command.output()
    }

    /// Check if pipx is available
    pub fn _pipx_available() -> bool {
        if let Ok(output) = Self::run_command_hidden("pipx", &["--version"], &std::collections::HashMap::new()) {
            return output.status.success();
        }
        false
    }

    /// Try to install pipx using common methods
    #[allow(dead_code)]
    pub fn try_install_pipx() -> bool {
        let install_attempts = [
            ("python3", vec!["-m", "pip", "install", "--user", "pipx"]),
            ("python", vec!["-m", "pip", "install", "--user", "pipx"]),
            ("apt", vec!["install", "-y", "python3-pipx"]),
            ("dnf", vec!["install", "-y", "python3-pipx"]),
            ("pacman", vec!["-S", "--noconfirm", "python-pipx"]),
        ];
        for (cmd, args) in &install_attempts {
            let args_refs: Vec<&str> = args.iter().map(|s| &**s).collect();
            if let Ok(output) = Self::run_command_hidden(cmd, &args_refs, &std::collections::HashMap::new()) {
                if output.status.success() {
                    return true;
                }
            }
        }
        false
    }
} 