//! Rustitles - Subtitle Downloader Tool
#![windows_subsystem = "windows"]

// =========================
// === Imports
// =========================
// --- Standard Library ---
use std::collections::VecDeque;
use std::env;
use std::fs::File;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::ptr::null_mut;
use std::sync::{mpsc::{self, Receiver}, Arc, Mutex};
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;
// --- Third-Party Crates ---
use eframe::egui;
use image;
use log::{info, warn, error, debug};
use rfd::FileDialog;
use reqwest::blocking::get;
use serde::{Deserialize, Serialize};
use serde_json;
use winreg::enums::*;
use winreg::RegKey;
// --- Windows API ---
use windows::Win32::Foundation::{POINT, WPARAM, LPARAM};
use windows::Win32::Graphics::Gdi::{MonitorFromPoint, GetMonitorInfoW, MONITORINFO, MONITOR_DEFAULTTONEAREST};
use windows::Win32::UI::WindowsAndMessaging::{
    GetCursorPos, SendMessageTimeoutW, HWND_BROADCAST, WM_SETTINGCHANGE, SMTO_ABORTIFHUNG
};

// =========================
// === Constants
// =========================
static VIDEO_EXTENSIONS: &[&str] = &[
    "mp4", "mkv", "avi", "mov", "wmv", "flv", "mpeg", "mpg", "webm", "m4v",
    "3gp", "3g2", "asf", "mts", "m2ts", "ts", "vob", "ogv", "rm", "rmvb", "divx", "f4v", "mxf",
    "mp2", "mpv", "dat", "tod", "vro", "drc", "mng", "qt", "yuv", "viv", "amv", "nsv", "svi",
    "mpe", "mpv2", "m2v", "m1v", "m2p", "trp", "tp", "ps", "evo", "ogm", "ogx", "mod", "rec",
    "dvr-ms", "pva", "wtv", "m4p", "m4b", "m4r", "m4a", "3gpp", "3gpp2"
];

// =========================
// === Settings Management
// =========================
#[derive(Serialize, Deserialize, Clone)]
struct Settings {
    selected_languages: Vec<String>,
    force_download: bool,
    overwrite_existing: bool,
    concurrent_downloads: usize,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            selected_languages: Vec::new(),
            force_download: false,
            overwrite_existing: false,
            concurrent_downloads: 25,
        }
    }
}

fn get_settings_path() -> std::io::Result<PathBuf> {
    let exe_path = env::current_exe()?;
    let exe_dir = exe_path.parent().ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::NotFound, "Failed to get executable directory")
    })?;
    Ok(exe_dir.join("rustitles_settings.json"))
}

fn load_settings() -> Settings {
    match get_settings_path() {
        Ok(path) => {
            match std::fs::read_to_string(&path) {
                Ok(content) => {
                    match serde_json::from_str(&content) {
                        Ok(settings) => {
                            info!("Settings loaded from {}", path.display());
                            settings
                        }
                        Err(e) => {
                            warn!("Failed to parse settings file: {}. Using defaults.", e);
                            Settings::default()
                        }
                    }
                }
                Err(e) => {
                    debug!("Settings file not found or unreadable: {}. Using defaults.", e);
                    Settings::default()
                }
            }
        }
        Err(e) => {
            warn!("Failed to get settings path: {}. Using defaults.", e);
            Settings::default()
        }
    }
}

fn save_settings(settings: &Settings) -> Result<(), String> {
    let path = get_settings_path().map_err(|e| format!("Failed to get settings path: {}", e))?;
    let json = serde_json::to_string_pretty(settings)
        .map_err(|e| format!("Failed to serialize settings: {}", e))?;
    std::fs::write(&path, json)
        .map_err(|e| format!("Failed to write settings file: {}", e))?;
    debug!("Settings saved to {}", path.display());
    Ok(())
}

// =========================
// === Utility Functions (Python, Subliminal, Env)
// =========================
fn python_version() -> Option<String> {
    for cmd in &["python", "py"] {
        if let Ok(output) = run_command_hidden(cmd, &["--version"], &std::collections::HashMap::new()) {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout).trim().to_string();
                let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
                let version = if !stdout.is_empty() { stdout } else { stderr };
                if version.to_lowercase().contains("python") {
                    debug!("Found Python version: {} using command: {}", version, cmd);
                    return Some(version);
                }
            }
        }
    }
    debug!("No Python installation found");
    None
}

fn check_subliminal_installed() -> bool {
    // First check if subliminal command is directly available
    if let Ok(output) = run_command_hidden("subliminal", &["--version"], &std::collections::HashMap::new()) {
        if output.status.success() {
            debug!("Subliminal found as direct command");
            return true;
        }
    }
    
    // Then check as Python module with multiple Python commands
    for cmd in &["python", "py", "python3"] {
        if let Ok(output) = run_command_hidden(cmd, &["-m", "pip", "show", "subliminal"], &std::collections::HashMap::new()) {
            if output.status.success() {
                debug!("Subliminal found via pip show using {}", cmd);
                return true;
            }
        }
        
        // Also try direct module import
        if let Ok(output) = run_command_hidden(cmd, &["-c", "import subliminal; print('subliminal available')"], &std::collections::HashMap::new()) {
            if output.status.success() {
                debug!("Subliminal found via direct import using {}", cmd);
                return true;
            }
        }
    }
    debug!("Subliminal not found");
    false
}

fn install_subliminal() -> bool {
    info!("Installing Subliminal via pip");
    for cmd in &["python", "py", "python3"] {
        if let Ok(output) = run_command_hidden(cmd, &["-m", "pip", "install", "subliminal"], &std::collections::HashMap::new()) {
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

fn add_scripts_to_path() -> Result<(), String> {
    let mut base_path = None;

    for cmd in &["python", "py"] {
        let output = run_command_hidden(cmd, &["-m", "site", "--user-base"], &std::collections::HashMap::new());

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

fn refresh_environment() -> Result<(), String> {
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

fn download_python_installer() -> io::Result<PathBuf> {
    let url = "https://www.python.org/ftp/python/3.13.5/python-3.13.5-amd64.exe";
    let response = get(url).map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;

    let temp_dir = env::temp_dir();
    let installer_path = temp_dir.join("python-installer.exe");
    let mut file = File::create(&installer_path)?;
    let bytes = response.bytes().map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;
    file.write_all(&bytes)?;
    Ok(installer_path)
}

fn install_python_silent(installer_path: &PathBuf) -> io::Result<bool> {
    let mut command = Command::new(installer_path);
    command.args(&[
        "/quiet",
        "InstallAllUsers=1",
        "PrependPath=1",
        "Include_pip=1",
    ]);
    
    // On Windows, try to hide the console window
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    
    let status = command.status()?;
    Ok(status.success())
}

fn ensure_subliminal_cache_dir() -> io::Result<PathBuf> {
    let cache_dir = env::temp_dir().join("subliminal_cache");
    std::fs::create_dir_all(&cache_dir)?;
    Ok(cache_dir)
}

fn run_command_hidden(cmd: &str, args: &[&str], env_vars: &std::collections::HashMap<String, String>) -> io::Result<std::process::Output> {
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
    
    command.output()
}

// =========================
// === Logging Setup
// =========================

struct AsyncLogger {
    sender: mpsc::Sender<LogMessage>,
    handle: Option<std::thread::JoinHandle<()>>,
}

#[derive(Clone)]
enum LogMessage {
    Info(String),
    Warn(String),
    Error(String),
    Debug(String),
    Shutdown,
}

impl AsyncLogger {
    fn new() -> Result<Self, Box<dyn std::error::Error>> {
        let (tx, rx) = mpsc::channel();
        
        // Get the directory where the executable is located
        let exe_path = env::current_exe()?;
        let exe_dir = exe_path.parent().ok_or("Failed to get executable directory")?;
        let log_path = exe_dir.join("rustitles_log.txt");
        
        // Create or open the log file
        let log_file = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_path)?;
        
        let handle = std::thread::spawn(move || {
            let mut file = std::io::BufWriter::new(log_file);
            let mut buffer = VecDeque::new();
            
            loop {
                // Process messages in batches for better performance
                while let Ok(msg) = rx.try_recv() {
                    match msg {
                        LogMessage::Shutdown => {
                            // Flush any remaining messages
                            for entry in buffer.drain(..) {
                                let _ = writeln!(file, "{}", entry);
                            }
                            let _ = file.flush();
                            return;
                        }
                        _ => {
                            let timestamp = chrono::Local::now().format("%Y-%m-%d %H:%M:%S");
                            let entry = match msg {
                                LogMessage::Info(msg) => format!("[INFO {} src\\main.rs:0] {}", timestamp, msg),
                                LogMessage::Warn(msg) => format!("[WARN {} src\\main.rs:0] {}", timestamp, msg),
                                LogMessage::Error(msg) => format!("[ERROR {} src\\main.rs:0] {}", timestamp, msg),
                                LogMessage::Debug(msg) => format!("[DEBUG {} src\\main.rs:0] {}", timestamp, msg),
                                LogMessage::Shutdown => unreachable!(),
                            };
                            buffer.push_back(entry);
                        }
                    }
                }
                
                // Flush buffer if it has enough entries or if we've been idle
                if buffer.len() >= 10 {
                    for entry in buffer.drain(..) {
                        let _ = writeln!(file, "{}", entry);
                    }
                    let _ = file.flush();
                }
                
                // Small sleep to prevent busy waiting
                std::thread::sleep(std::time::Duration::from_millis(1));
            }
        });
        
        Ok(AsyncLogger {
            sender: tx,
            handle: Some(handle),
        })
    }
    
    fn log(&self, level: &str, message: &str) {
        let msg = match level {
            "INFO" => LogMessage::Info(message.to_string()),
            "WARN" => LogMessage::Warn(message.to_string()),
            "ERROR" => LogMessage::Error(message.to_string()),
            "DEBUG" => LogMessage::Debug(message.to_string()),
            _ => LogMessage::Info(message.to_string()),
        };
        
        // Non-blocking send - if the channel is full, we just drop the message
        let _ = self.sender.send(msg);
    }
    
    fn shutdown(self) {
        let _ = self.sender.send(LogMessage::Shutdown);
        if let Some(handle) = self.handle {
            let _ = handle.join();
        }
    }
}

// Global logger instance
static LOGGER: Mutex<Option<AsyncLogger>> = Mutex::new(None);

fn setup_logging() -> Result<(), Box<dyn std::error::Error>> {
    let logger = AsyncLogger::new()?;
    let mut guard = LOGGER.lock().map_err(|e| format!("Failed to lock logger: {}", e))?;
    *guard = Some(logger);
    Ok(())
}

fn log_message(level: &str, message: &str) {
    if let Ok(guard) = LOGGER.lock() {
        if let Some(logger) = &*guard {
            logger.log(level, message);
        }
    }
}

// Custom log macros that use our async logger
macro_rules! info {
    ($($arg:tt)*) => {
        log_message("INFO", &format!($($arg)*));
    };
}

macro_rules! warn {
    ($($arg:tt)*) => {
        log_message("WARN", &format!($($arg)*));
    };
}

macro_rules! error {
    ($($arg:tt)*) => {
        log_message("ERROR", &format!($($arg)*));
    };
}

macro_rules! debug {
    ($($arg:tt)*) => {
        log_message("DEBUG", &format!($($arg)*));
    };
}

// =========================
// === App State Structs & Enums
// =========================
#[derive(Clone, PartialEq)]
enum JobStatus {
    Pending,
    Running,
    Success,
    EmbeddedExists(String), // full message
    Failed(String),
}

struct DownloadJob {
    video_path: PathBuf,
    status: JobStatus,
    subtitle_paths: Vec<PathBuf>,
}

struct SubtitleDownloader {
    downloads_completed: usize,
    total_downloads: usize,
    is_downloading: bool,
    python_installed: bool,
    python_version: Option<String>,
    subliminal_installed: bool,
    status: String,
    installing_python: bool,
    installing_subliminal: bool,
    python_install_result: Arc<Mutex<Option<Result<(), String>>>>,
    subliminal_install_result: Arc<Mutex<Option<Result<(), String>>>>,
    selected_languages: Vec<String>,
    force_download: bool,
    overwrite_existing: bool,
    folder_path: String,
    scanned_videos: Arc<Mutex<Vec<PathBuf>>>,
    videos_missing_subs: Arc<Mutex<Vec<PathBuf>>>,
    scanning: bool,
    scan_done_receiver: Option<Receiver<()>>,
    download_jobs: Arc<Mutex<Vec<DownloadJob>>>,
    downloading: bool,
    download_thread_handle: Option<thread::JoinHandle<()>>,
    cancel_flag: Arc<AtomicBool>,
    concurrent_downloads: usize,
    keep_dropdown_open: bool,
}

impl Default for SubtitleDownloader {
    fn default() -> Self {
        info!("Initializing SubtitleDownloader");
        
        // Load saved settings
        let settings = load_settings();
        info!("Loaded settings: languages={:?}, force={}, overwrite={}, concurrent={}", 
              settings.selected_languages, settings.force_download, settings.overwrite_existing, settings.concurrent_downloads);
        
        let python_version = python_version();
        let python_installed = python_version.is_some();
        let subliminal_installed = if python_installed {
            check_subliminal_installed()
        } else {
            false
        };

        info!("Python installed: {}, version: {:?}", python_installed, python_version);
        info!("Subliminal installed: {}", subliminal_installed);

        let installing_subliminal = python_installed && !subliminal_installed;
        let subliminal_install_result = Arc::new(Mutex::new(None));

        if python_installed && !subliminal_installed {
            info!("Starting automatic Subliminal installation");
            let result_ptr = Arc::clone(&subliminal_install_result);
            std::thread::spawn(move || {
                let success = install_subliminal();
                let result = if success {
                    match add_scripts_to_path() {
                        Ok(_) => Ok(()),
                        Err(e) => Err(format!("Subliminal installed, but failed to update PATH: {}", e)),
                    }
                } else {
                    Err("pip install failed".to_string())
                };
                *result_ptr.lock().unwrap() = Some(result);
            });
        }

        Self {
            downloads_completed: 0,
            total_downloads: 0,
            is_downloading: false,
            python_installed,
            python_version,
            subliminal_installed,
            status: if python_installed && !subliminal_installed {
                "Python detected. Installing Subliminal...".to_string()
            } else {
                "Ready".to_string()
            },
            installing_python: false,
            installing_subliminal,
            python_install_result: Arc::new(Mutex::new(None)),
            subliminal_install_result,
            selected_languages: settings.selected_languages,
            force_download: settings.force_download,
            overwrite_existing: settings.overwrite_existing,
            folder_path: String::new(),
            scanned_videos: Arc::new(Mutex::new(Vec::new())),
            videos_missing_subs: Arc::new(Mutex::new(Vec::new())),
            scanning: false,
            scan_done_receiver: None,
            download_jobs: Arc::new(Mutex::new(Vec::new())),
            downloading: false,
            download_thread_handle: None,
            cancel_flag: Arc::new(AtomicBool::new(false)),
            concurrent_downloads: settings.concurrent_downloads,
            keep_dropdown_open: false,
        }
    }
}

impl SubtitleDownloader {
    fn save_current_settings(&self) {
        let settings = Settings {
            selected_languages: self.selected_languages.clone(),
            force_download: self.force_download,
            overwrite_existing: self.overwrite_existing,
            concurrent_downloads: self.concurrent_downloads,
        };
        
        if let Err(e) = save_settings(&settings) {
            warn!("Failed to save settings: {}", e);
        } else {
            debug!("Settings saved successfully");
        }
    }

    fn video_missing_subtitle(video_path: &Path, selected_languages: &[String]) -> bool {
        if let Some(stem) = video_path.file_stem().and_then(|s| s.to_str()) {
            let folder = video_path.parent().unwrap_or_else(|| Path::new(""));
            
            // Check for common subtitle extensions
            let subtitle_extensions = ["srt", "sub", "ssa", "ass", "vtt"];
            
            // Check if any of the selected languages are missing
            for lang in selected_languages {
                let mut lang_found = false;
                
                // Check for language-specific patterns first (e.g., video.en.srt)
                for ext in &subtitle_extensions {
                    let subtitle_path = folder.join(format!("{}.{}.{}", stem, lang, ext));
                    if subtitle_path.exists() {
                        lang_found = true;
                        break;
                    }
                }
                
                // If language-specific not found, check basic pattern (e.g., video.srt)
                if !lang_found {
                    for ext in &subtitle_extensions {
                        let subtitle_path = folder.join(format!("{}.{}", stem, ext));
                        if subtitle_path.exists() {
                            lang_found = true;
                            break;
                        }
                    }
                }
                
                // If this language is missing, return true (missing subtitles)
                if !lang_found {
                    return true;
                }
            }
        }
        false // All selected languages have subtitles
    }

    fn scan_folder(&mut self) {
        if self.folder_path.is_empty() || self.scanning {
            return;
        }

        info!("Starting folder scan: {}", self.folder_path);
        self.status = "Scanning...".to_string();
        self.scanning = true;
        let (tx, rx) = mpsc::channel();
        self.scan_done_receiver = Some(rx);

        let scanned_videos = Arc::clone(&self.scanned_videos);
        let videos_missing_subs = Arc::clone(&self.videos_missing_subs);
        let folder_path = self.folder_path.clone();
        let selected_languages = self.selected_languages.clone();
        let overwrite_existing = self.overwrite_existing;

        // Clear download jobs when folder changes
        {
            let mut jobs = self.download_jobs.lock().unwrap();
            jobs.clear();
        }

        // Reset downloading flag when starting new scan
        self.downloading = false;

        thread::spawn(move || {
            let mut found_videos = Vec::new();
            let mut missing_subtitles = Vec::new();

            fn visit_dirs(dir: &Path, videos: &mut Vec<PathBuf>) {
                if let Ok(entries) = dir.read_dir() {
                    for entry in entries.flatten() {
                        let path = entry.path();
                        if path.is_dir() {
                            visit_dirs(&path, videos);
                        } else if let Some(ext) = path.extension().and_then(|e| e.to_str()) {
                            if VIDEO_EXTENSIONS.iter().any(|&v| v.eq_ignore_ascii_case(ext)) {
                                videos.push(path);
                            }
                        }
                    }
                }
            }

            visit_dirs(Path::new(&folder_path), &mut found_videos);

            if overwrite_existing {
                // If overwrite is enabled, include all videos regardless of existing subtitles
                missing_subtitles = found_videos.clone();
                info!("Overwrite mode enabled - including all {} videos", found_videos.len());
            } else {
                // Only include videos that are missing subtitles
                for video in &found_videos {
                    if SubtitleDownloader::video_missing_subtitle(video, &selected_languages) {
                        missing_subtitles.push(video.clone());
                    }
                }
                info!("Found {} videos, {} missing subtitles", found_videos.len(), missing_subtitles.len());
            }

            *scanned_videos.lock().unwrap() = found_videos;
            *videos_missing_subs.lock().unwrap() = missing_subtitles;

            info!("Folder scan completed");
            let _ = tx.send(());
        });
    }

    fn start_downloads(&mut self) {
        if self.downloading || self.selected_languages.is_empty() {
            self.status = "Select at least one language and ensure no downloads are in progress.".to_string();
            warn!("Cannot start downloads: downloading={}, languages={:?}", self.downloading, self.selected_languages);
            return;
        }

        let videos_missing = self.videos_missing_subs.lock().unwrap().clone();
        if videos_missing.is_empty() {
            self.status = "No videos missing subtitles.".to_string();
            info!("No videos to download subtitles for");
            return;
        }

        info!("Starting subtitle downloads for {} videos with languages: {:?}", videos_missing.len(), self.selected_languages);
        self.status = "Starting subtitle downloads...".to_string();
        self.downloads_completed = 0;
        self.total_downloads = 0;
        self.is_downloading = true;

        let langs = self.selected_languages.clone();
        let jobs: Vec<_> = videos_missing.into_iter()
            .map(|video_path| DownloadJob { video_path, status: JobStatus::Pending, subtitle_paths: Vec::new() })
            .collect();

        self.total_downloads = jobs.len();
        *self.download_jobs.lock().unwrap() = jobs;
        self.downloading = true;

        self.cancel_flag.store(false, Ordering::SeqCst);

        let cancel_flag = Arc::clone(&self.cancel_flag);
        let jobs_arc = Arc::clone(&self.download_jobs);
        let max_concurrent = self.concurrent_downloads;
        let force_download = self.force_download;
        let overwrite_existing = self.overwrite_existing;

        info!("Starting download thread with {} concurrent downloads, force={}, overwrite={}", max_concurrent, force_download, overwrite_existing);

        self.download_thread_handle = Some(thread::spawn(move || {
            let mut pending_indexes: VecDeque<usize> = (0..jobs_arc.lock().unwrap().len()).collect();
            let mut running_threads = Vec::new();

            while !pending_indexes.is_empty() || !running_threads.is_empty() {
                running_threads.retain(|handle: &thread::JoinHandle<()>| !handle.is_finished());

                while running_threads.len() < max_concurrent && !pending_indexes.is_empty() {
                    if cancel_flag.load(Ordering::SeqCst) {
                        info!("Download cancelled by user");
                        let mut jobs_lock = jobs_arc.lock().unwrap();
                        for job in jobs_lock.iter_mut() {
                            if job.status == JobStatus::Pending || job.status == JobStatus::Running {
                                job.status = JobStatus::Failed("Cancelled".to_string());
                            }
                        }
                        return;
                    }

                    let idx = pending_indexes.pop_front().unwrap();

                    {
                        let mut jobs_lock = jobs_arc.lock().unwrap();
                        if let Some(job) = jobs_lock.get_mut(idx) {
                            job.status = JobStatus::Running;
                        }
                    }

                    let job_path = {
                        let jobs_lock = jobs_arc.lock().unwrap();
                        jobs_lock[idx].video_path.clone()
                    };

                    let langs_clone = langs.clone();
                    let jobs_clone = Arc::clone(&jobs_arc);
                    let cancel_flag_clone = Arc::clone(&cancel_flag);

                    let handle = thread::spawn(move || {
                        if cancel_flag_clone.load(Ordering::SeqCst) {
                            let mut jobs_lock = jobs_clone.lock().unwrap();
                            if let Some(job) = jobs_lock.iter_mut().find(|j| j.video_path == job_path) {
                                job.status = JobStatus::Failed("Cancelled".to_string());
                            }
                            return;
                        }

                        debug!("Processing video: {}", job_path.display());

                        // Create cache directory and set environment variables to fix DBM cache issues on Windows
                        let cache_dir = ensure_subliminal_cache_dir().unwrap_or_else(|_| env::temp_dir().join("subliminal_cache"));
                        let mut env_vars = std::collections::HashMap::<String, String>::new();
                        env_vars.insert("PYTHONIOENCODING".to_string(), "utf-8".to_string());
                        env_vars.insert("SUBLIMINAL_CACHE_DIR".to_string(), cache_dir.to_string_lossy().to_string());
                        env_vars.insert("PYTHONHASHSEED".to_string(), "0".to_string());
                        
                        // Build command arguments with multiple -l flags for each language
                        let mut args = vec!["download"];
                        if force_download {
                            args.push("--force");
                        }
                        if overwrite_existing {
                            args.push("--force");
                        }
                        for lang in &langs_clone {
                            args.push("-l");
                            args.push(lang);
                        }
                        
                        // Run subliminal with multiple failsafes
                        let mut all_args = args.clone();
                        all_args.push(job_path.to_str().unwrap());
                        
                        debug!("Running subliminal command: subliminal {}", all_args.join(" "));
                        
                        let output = run_command_hidden("subliminal", &all_args, &env_vars)
                            .or_else(|_| {
                                debug!("Subliminal direct command failed, trying python -m subliminal");
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("python", &python_args, &env_vars)
                            })
                            .or_else(|_| {
                                debug!("Python command failed, trying py -m subliminal");
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("py", &python_args, &env_vars)
                            })
                            .or_else(|_| {
                                debug!("Py command failed, trying python3 -m subliminal");
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("python3", &python_args, &env_vars)
                            });

                        let mut jobs_lock = jobs_clone.lock().unwrap();
                        let job_opt = jobs_lock.iter_mut().find(|j| j.video_path == job_path);

                        let embedded_phrases = [
                            "embedded", "already exists", "no need to download", "subtitle(s) already present", "has embedded subtitles", "skipping"
                        ];
                        if let Ok(out) = output {
                            let stdout_str = String::from_utf8_lossy(&out.stdout).to_lowercase();
                            let stderr_str = String::from_utf8_lossy(&out.stderr).to_lowercase();
                            let combined_output = format!("{}\n{}", stdout_str, stderr_str).trim().to_string();
                            let subtitle_paths = find_all_subtitle_files(&job_path, &langs_clone);
                            
                            // --- LOGGING: Full Subliminal output ---
                            info!("Subliminal output for {}:\n{}", job_path.display(), combined_output);
                            info!("END subliminal output");
                            
                            if let Some(job) = job_opt {
                                // --- LOGGING: Video name and status ---
                                let video_name = job_path.file_name().unwrap_or_default().to_string_lossy();
                                let status_str = match &job.status {
                                    JobStatus::Success => "Success",
                                    JobStatus::EmbeddedExists(_) => "Embedded",
                                    JobStatus::Failed(_) => "Failed",
                                    JobStatus::Pending => "Pending",
                                    JobStatus::Running => "Running",
                                };
                                info!("SUBTITLE JOBS OUTPUT: {} - {}", video_name, status_str);
                                // --- LOGGING: Subtitle file paths ---
                                for sub_path in &subtitle_paths {
                                    info!("SUBTITLE JOBS OUTPUT: 📄 {}", sub_path.display());
                                }
                                // --- END LOGGING ---
                                
                                if combined_output.contains("downloaded 0 subtitle") {
                                    if !subtitle_paths.is_empty() {
                                        // If any subtitles were downloaded, always report Success (even if ignoring embedded)
                                        job.status = JobStatus::Success;
                                    } else if !force_download {
                                        // Only check for embedded if not forcing download
                                        if let Some(lang_name) = has_embedded_subtitle(&job_path, &langs_clone) {
                                            job.status = JobStatus::EmbeddedExists(format!("Embedded {} subtitles already exist (no external subtitles found online)", lang_name));
                                        } else if embedded_phrases.iter().any(|phrase| combined_output.contains(phrase)) {
                                            let lang_code = langs_clone.get(0).cloned().unwrap_or_else(|| "unknown".to_string());
                                            let lang_name = language_code_to_name(&lang_code).to_string();
                                            job.status = JobStatus::EmbeddedExists(format!("Embedded {} subtitles already exist (no external subtitles found online)", lang_name));
                                        } else {
                                            job.status = JobStatus::Failed("No subtitles found (no embedded or external subtitles available)".to_string());
                                        }
                                    } else {
                                        // Forced, but nothing downloaded
                                        job.status = JobStatus::Failed("No subtitles found online".to_string());
                                    }
                                } else if combined_output.contains("error") || combined_output.contains("failed") {
                                    if !subtitle_paths.is_empty() {
                                        job.status = JobStatus::Success;
                                    } else {
                                        job.status = JobStatus::Failed("Subliminal error: see log".to_string());
                                    }
                                } else {
                                    job.status = JobStatus::Success;
                                }
                                job.subtitle_paths = subtitle_paths;
                            }
                        } else {
                            error!("Failed to run subliminal for {}", job_path.display());
                            if let Some(job) = job_opt {
                                job.status = JobStatus::Failed("Failed to run subliminal".to_string());
                            }
                        }
                    });

                    running_threads.push(handle);
                }

                if cancel_flag.load(Ordering::SeqCst) {
                    info!("Download cancelled by user");
                    let mut jobs_lock = jobs_arc.lock().unwrap();
                    for job in jobs_lock.iter_mut() {
                        if job.status == JobStatus::Pending || job.status == JobStatus::Running {
                            job.status = JobStatus::Failed("Cancelled".to_string());
                        }
                    }
                    break;
                }

                thread::sleep(std::time::Duration::from_millis(200));
            }
            
            info!("Download thread completed");
        }));
    }

    fn check_download_completion(&mut self) {
        if !self.downloading {
            return;
        }

        // Update progress in real-time
        let jobs = self.download_jobs.lock().unwrap();
        let success_count = jobs.iter().filter(|j| j.status == JobStatus::Success || matches!(j.status, JobStatus::EmbeddedExists(_))).count();
        let running_count = jobs.iter().filter(|j| j.status == JobStatus::Running).count();
        let failed_count = jobs.iter().filter(|j| matches!(j.status, JobStatus::Failed(_))).count();
        
        let previous_completed = self.downloads_completed;
        self.downloads_completed = success_count;

        // Log progress changes
        if self.downloads_completed != previous_completed {
            debug!("Download progress: {}/{} completed, {} running, {} failed", 
                self.downloads_completed, self.total_downloads, running_count, failed_count);
        }

        // Check if download thread is finished
        if let Some(handle) = &self.download_thread_handle {
            if handle.is_finished() {
                self.downloading = false;
                self.download_thread_handle = None;
                
                // Count completed jobs
                let failed_count = jobs.iter().filter(|j| matches!(j.status, JobStatus::Failed(_))).count();
                let success_count = jobs.iter().filter(|j| j.status == JobStatus::Success || matches!(j.status, JobStatus::EmbeddedExists(_))).count();
                
                info!("Download session completed: {} successful, {} failed", success_count, failed_count);
                self.status = format!("Subtitle jobs completed: {} successful, {} failed", success_count, failed_count);
                self.is_downloading = false;
            } else {
                // Update status while downloading
                if running_count > 0 {
                    self.status = format!("Downloading: {} completed, {} running, {} pending", 
                        success_count, running_count, self.total_downloads - success_count - running_count);
                }
            }
        }
    }
}

// Update the function to find all subtitle files for all languages
fn find_all_subtitle_files(video_path: &Path, langs: &[String]) -> Vec<PathBuf> {
    let folder = match video_path.parent() {
        Some(f) => f,
        None => return Vec::new(),
    };
    let stem = match video_path.file_stem().and_then(|s| s.to_str()) {
        Some(s) => s,
        None => return Vec::new(),
    };
    let subtitle_extensions = ["srt", "sub", "ssa", "ass", "vtt"];
    let mut found_subtitles = Vec::new();
    
    debug!("Searching for subtitle files for {} in {}", video_path.display(), folder.display());
    
    // Try language-specific first
    for lang in langs {
        for ext in &subtitle_extensions {
            let candidate = folder.join(format!("{}.{}.{}", stem, lang, ext));
            if candidate.exists() {
                debug!("Found language-specific subtitle: {}", candidate.display());
                found_subtitles.push(candidate);
                break; // Found one for this language, move to next
            }
        }
    }
    // Then try generic
    for ext in &subtitle_extensions {
        let candidate = folder.join(format!("{}.{}", stem, ext));
        if candidate.exists() {
            debug!("Found generic subtitle: {}", candidate.display());
            found_subtitles.push(candidate);
            break; // Found one generic, stop
        }
    }
    
    if found_subtitles.is_empty() {
        debug!("No subtitle files found for {}", video_path.display());
    } else {
        debug!("Found {} subtitle files for {}", found_subtitles.len(), video_path.display());
    }
    
    found_subtitles
}

// Add this helper function for language code to name
fn language_code_to_name(code: &str) -> &str {
    match code {
        "en" => "English",
        "fr" => "French",
        "es" => "Spanish",
        "de" => "German",
        "it" => "Italian",
        "pt" => "Portuguese",
        "nl" => "Dutch",
        "pl" => "Polish",
        "ru" => "Russian",
        "sv" => "Swedish",
        "fi" => "Finnish",
        "da" => "Danish",
        "no" => "Norwegian",
        "cs" => "Czech",
        "hu" => "Hungarian",
        "ro" => "Romanian",
        "he" => "Hebrew",
        "ar" => "Arabic",
        "ja" => "Japanese",
        "ko" => "Korean",
        "zh" => "Chinese",
        "zh-cn" => "Chinese (Simplified)",
        "zh-tw" => "Chinese (Traditional)",
        _ => code,
    }
}

// === Embedded Subtitle Detection with ffprobe ===
fn has_embedded_subtitle(video_path: &std::path::Path, langs: &[String]) -> Option<String> {
    use std::process::Command;
    let mut cmd = Command::new("ffprobe");
    cmd.arg("-v")
        .arg("error")
        .arg("-select_streams")
        .arg("s")
        .arg("-show_entries")
        .arg("stream=index:stream_tags=language")
        .arg("-of")
        .arg("csv=p=0")
        .arg(video_path);
    // Hide the window on Windows
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        cmd.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    let output = cmd.output();
    if let Ok(output) = output {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            for line in stdout.lines() {
                // Each line: index,language (e.g., 0,eng)
                let parts: Vec<&str> = line.split(',').collect();
                if parts.len() >= 2 {
                    let lang = parts[1].trim().to_lowercase();
                    for req in langs {
                        // Accept both 2-letter and 3-letter codes
                        if lang == req.to_lowercase() || lang.starts_with(&req.to_lowercase()) {
                            return Some(language_code_to_name(req).to_string());
                        }
                    }
                }
            }
        }
    }
    None
}

// =========================
// === eframe::GUI Implementation
// =========================
impl eframe::App for SubtitleDownloader {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // Check download completion
        self.check_download_completion();

        if self.installing_python {
            if let Some(result) = self.python_install_result.lock().unwrap().take() {
                self.installing_python = false;
                match result {
                    Ok(_) => {
                        info!("Python installation completed successfully");
                        // Refresh environment to pick up new Python installation
                        if let Err(e) = refresh_environment() {
                            error!("Failed to refresh environment: {}", e);
                        }
                        self.python_version = python_version();
                        self.python_installed = self.python_version.is_some();
                        self.status = "✅ Python installed successfully. Installing Subliminal...".to_string();
                        self.subliminal_installed = check_subliminal_installed();

                        // Start installing subliminal automatically
                        self.installing_subliminal = true;
                        let result_ptr = self.subliminal_install_result.clone();
                        std::thread::spawn(move || {
                            let success = install_subliminal();
                            let result = if success {
                                match add_scripts_to_path() {
                                    Ok(_) => Ok(()),
                                    Err(e) => Err(format!("Subliminal installed, but failed to update PATH: {}", e)),
                                }
                            } else {
                                Err("pip install failed".to_string())
                            };
                            *result_ptr.lock().unwrap() = Some(result);
                        });
                    }
                    Err(e) => {
                        error!("Python installation failed: {}", e);
                        self.status = format!("❌ Python install failed: {}", e);
                    }
                }
            }
        }

        if self.installing_subliminal {
            if let Some(result) = self.subliminal_install_result.lock().unwrap().take() {
                self.installing_subliminal = false;
                match result {
                    Ok(_) => {
                        info!("Subliminal installation completed successfully");
                        // Refresh environment to pick up new subliminal installation
                        if let Err(e) = refresh_environment() {
                            error!("Failed to refresh environment: {}", e);
                        }
                        
                        self.subliminal_installed = true;
                        self.status = "✅ Subliminal installed.".to_string();
                    }
                    Err(e) => {
                        error!("Subliminal installation failed: {}", e);
                        self.status = format!("❌ Subliminal install failed: {}", e);
                    }
                }
            }
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            // Heading with Dracula colors
            ui.heading(egui::RichText::new("Rustitles - Subtitle Downloader Tool").color(egui::Color32::from_rgb(189, 147, 249)));
            ui.add_space(5.0);

            if self.installing_python || self.installing_subliminal {
                ui.label("⏳ Please wait...");
                ui.label(&self.status);
                return;
            }

            if self.python_installed {
                ui.label(format!(
                    "✅ Python is installed: {}",
                    self.python_version.as_deref().unwrap_or("Unknown version")
                ));
            } else {
                ui.label("❌ Python not found");
                if ui.button("Install Python").clicked() {
                    info!("User initiated Python installation");
                    self.status = "Installing Python 3.13.5... (Please check your taskbar for a UAC prompt and accept)".to_string();
                    self.installing_python = true;
                    let result_ptr = self.python_install_result.clone();

                    thread::spawn(move || {
                        let result = (|| {
                            let path = download_python_installer().map_err(|e| e.to_string())?;
                            let ok = install_python_silent(&path).map_err(|e| e.to_string())?;
                            if ok { Ok(()) } else { Err("Installer exited with failure".to_string()) }
                        })();

                        *result_ptr.lock().unwrap() = Some(result);
                    });
                }
            }

            if self.python_installed {
                if self.subliminal_installed {
                    ui.label("✅ Subliminal is installed");
                } else {
                    ui.label("❌ Subliminal not found");
                    if ui.button("Install Subliminal").clicked() {
                        info!("User initiated Subliminal installation");
                        self.status = "Installing Subliminal...".to_string();
                        self.installing_subliminal = true;
                        let result_ptr = self.subliminal_install_result.clone();

                        thread::spawn(move || {
                            let success = install_subliminal();
                            let result = if success {
                                match add_scripts_to_path() {
                                    Ok(_) => Ok(()),
                                    Err(e) => Err(format!("Subliminal installed, but failed to update PATH: {}", e)),
                                }
                            } else {
                                Err("pip install failed".to_string())
                            };
                            *result_ptr.lock().unwrap() = Some(result);
                        });
                    }
                }
            }

            ui.separator();

            // Only show language selection and folder selection after subliminal is installed
            if self.subliminal_installed {
                let language_list = vec![
                    ("en", "English"), ("fr", "French"), ("es", "Spanish"), ("de", "German"),
                    ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("pl", "Polish"),
                    ("ru", "Russian"), ("sv", "Swedish"), ("fi", "Finnish"), ("da", "Danish"),
                    ("no", "Norwegian"), ("cs", "Czech"), ("hu", "Hungarian"), ("ro", "Romanian"),
                    ("he", "Hebrew"), ("ar", "Arabic"), ("ja", "Japanese"), ("ko", "Korean"),
                    ("zh", "Chinese"), ("zh-cn", "Chinese (Simplified)"), ("zh-tw", "Chinese (Traditional)")
                ];

                ui.horizontal(|ui| {
                    // Button that looks like ComboBox (no dropdown arrow)
                    let selected_text = if self.selected_languages.is_empty() {
                        "Select Languages".to_string()
                    } else {
                        self.selected_languages.join(", ")
                    };
                    
                    let button_response = ui.add_sized([130.0, ui.spacing().interact_size.y], egui::Button::new(selected_text));
                    if button_response.clicked() {
                        debug!("Button clicked! Current state: {}", self.keep_dropdown_open);
                        self.keep_dropdown_open = !self.keep_dropdown_open;
                        debug!("New state: {}", self.keep_dropdown_open);
                    }

                    let force_checkbox_response = ui.checkbox(&mut self.force_download, "Ignore Embedded Subtitles");
                    if force_checkbox_response.changed() {
                        info!("(Ignore Embedded Subtitles) changed to: {}", self.force_download);
                        self.keep_dropdown_open = false; // Close dropdown when checkbox is clicked
                        self.save_current_settings(); // Save settings when changed
                    }
                    ui.add_space(0.0);
                    let overwrite_checkbox_response = ui.checkbox(&mut self.overwrite_existing, "Overwrite Existing Subtitles");
                    if overwrite_checkbox_response.changed() {
                        info!("(Overwrite Existing Subtitles) changed to: {}", self.overwrite_existing);
                        self.keep_dropdown_open = false; // Close dropdown when checkbox is clicked
                        self.save_current_settings(); // Save settings when changed
                        // Re-scan for missing subtitles when overwrite option changes
                        if !self.folder_path.is_empty() {
                            self.scan_folder();
                        }
                    }
                });
                
                // Simple popup that shows when button is clicked
                if self.keep_dropdown_open {
                    ui.add_space(5.0);
                    ui.group(|ui| {
                        ui.set_width(200.0);
                        
                        egui::ScrollArea::vertical()
                            .max_height(200.0)
                            .show(ui, |ui| {
                                ui.set_width(ui.available_width()); // Make scrollbar flush right
                                for (code, name) in &language_list {
                                    let mut selected = self.selected_languages.contains(&code.to_string());
                                    if ui.checkbox(&mut selected, *name).changed() {
                                        if selected {
                                            self.selected_languages.push(code.to_string());
                                            debug!("Language selected: {}", code);
                                        } else {
                                            self.selected_languages.retain(|c| c != code);
                                            debug!("Language deselected: {}", code);
                                        }
                                        
                                        self.save_current_settings(); // Save settings when languages change
                                        
                                        // Re-scan for missing subtitles when languages change
                                        if !self.folder_path.is_empty() {
                                            info!("Languages changed to {:?}, re-scanning folder", self.selected_languages);
                                            self.scan_folder();
                                        }
                                    }
                                }
                            });
                    });
                }
            } else {
                // Show message when subliminal is not installed
                ui.label("Please install Python and Subliminal above to start downloading subtitles.");
            }

            ui.separator();

            ui.horizontal(|ui| {
                ui.label("Concurrent Downloads:");
                let mut concurrent_text = self.concurrent_downloads.to_string();
                let text_response = ui.add_sized([25.0, ui.spacing().interact_size.y], egui::TextEdit::singleline(&mut concurrent_text));
                if text_response.changed() {
                    if let Ok(value) = concurrent_text.parse::<usize>() {
                        if value > 0 {
                            let old_value = self.concurrent_downloads;
                            self.concurrent_downloads = value.min(100);
                            debug!("Concurrent downloads changed from {} to {}", old_value, self.concurrent_downloads);
                            self.save_current_settings(); // Save settings when changed
                        }
                    }
                    self.keep_dropdown_open = false; // Close dropdown when text field is changed
                }
                if text_response.gained_focus() {
                    self.keep_dropdown_open = false; // Close dropdown when text field gains focus
                }
            });

            ui.separator();

            ui.horizontal(|ui| {
                ui.label("Folder to scan:");
                let folder_button_response = ui.button("Select Folder");
                if folder_button_response.clicked() {
                    self.keep_dropdown_open = false; // Close dropdown when folder button is clicked
                    if let Some(folder) = FileDialog::new().pick_folder() {
                        let new_folder = folder.display().to_string();
                        if self.folder_path != new_folder {
                            info!("Folder selected: {}", new_folder);
                            self.folder_path = new_folder;
                            self.scan_folder();
                        }
                    }
                }
                ui.label(&self.folder_path);
            });

            ui.separator();

            if !self.folder_path.is_empty() {
                let scanned_count = self.scanned_videos.lock().unwrap().len();
                let missing_count = self.videos_missing_subs.lock().unwrap().len();
                ui.label(format!("Found videos: {}", scanned_count));
                if self.overwrite_existing {
                    ui.label(format!("Overwriting {} Subtitles", missing_count));
                } else {
                    ui.label(format!("Videos missing subtitles: {}", missing_count));
                }
            }

            // Show download jobs status
            let jobs = self.download_jobs.lock().unwrap();
            if !jobs.is_empty() {
                ui.label("Subtitle Jobs:");
                ui.separator();
                
                // Calculate available height for the scroll area
                // Reserve space for: status label, progress label, progress bar, and some padding
                let reserved_height = 80.0; // Approximate space needed for bottom elements
                let available_height = ui.available_height() - reserved_height;
                let scroll_height = available_height.max(200.0); // Minimum height of 200px (previous default)
                
                egui::ScrollArea::vertical()
                    .max_height(scroll_height)
                    .show(ui, |ui| {
                        // Set a fixed width to ensure consistent scroll bar positioning
                        let available_width = ui.available_width();
                        ui.set_width(available_width - 20.0); // Reserve space for scroll bar
                        
                        for job in jobs.iter() {
                            let (status_text, status_color) = match &job.status {
                                JobStatus::Pending => ("Pending".to_string(), Some(egui::Color32::from_rgb(241, 250, 140))), // yellow
                                JobStatus::Running => ("Running".to_string(), Some(egui::Color32::from_rgb(189, 147, 249))), // lighter purple
                                JobStatus::Success => ("Success".to_string(), Some(egui::Color32::from_rgb(80, 250, 123))), // green
                                JobStatus::EmbeddedExists(msg) => (msg.clone(), Some(egui::Color32::from_rgb(255, 184, 108))), // orange
                                JobStatus::Failed(err) => (format!("Failed: {}", err), Some(egui::Color32::from_rgb(255, 85, 85))), // red
                            };
                            // Video name and status on first line
                            ui.horizontal(|ui| {
                                ui.label(job.video_path.file_name().unwrap_or_default().to_string_lossy());
                                match status_color {
                                    Some(color) => ui.label(egui::RichText::new(format!(" - {}", status_text)).color(color)),
                                    None => ui.label(format!(" - {}", status_text)),
                                };
                            });
                            
                            // Subtitle path on second line (indented)
                            for sub_path in &job.subtitle_paths {
                                ui.horizontal(|ui| {
                                    ui.add_space(20.0); // Indent the subtitle path
                                    let path_str = sub_path.display().to_string();
                                    let hyperlink_resp = ui.add(
                                        egui::Label::new(
                                            egui::RichText::new(format!("📄 {}", path_str))
                                                .color(egui::Color32::from_rgb(248, 248, 242)) // white
                                        )
                                        .sense(egui::Sense::click())
                                    );
                                    if hyperlink_resp.hovered() {
                                        let rect = hyperlink_resp.rect;
                                        let painter = ui.painter();
                                        painter.line_segment([
                                            rect.left_bottom() + egui::vec2(2.0, -2.0),
                                            rect.right_bottom() + egui::vec2(-2.0, -2.0)
                                        ],
                                        egui::Stroke::new(1.5, egui::Color32::from_rgb(139, 233, 253)) // cyan underline
                                        );
                                        ui.output_mut(|o| o.cursor_icon = egui::CursorIcon::PointingHand);
                                    }
                                    if hyperlink_resp.clicked() {
                                        debug!("User clicked subtitle path: {}", path_str);
                                        // Open the folder containing the subtitle file
                                        #[cfg(target_os = "windows")]
                                        {
                                            let _ = std::process::Command::new("explorer")
                                                .arg("/select,")
                                                .arg(sub_path)
                                                .spawn();
                                        }
                                        #[cfg(target_os = "linux")]
                                        {
                                            let _ = std::process::Command::new("xdg-open")
                                                .arg(sub_path.parent().unwrap_or_else(|| std::path::Path::new(".")))
                                                .spawn();
                                        }
                                        #[cfg(target_os = "macos")]
                                        {
                                            let _ = std::process::Command::new("open")
                                                .arg("-R")
                                                .arg(sub_path)
                                                .spawn();
                                        }
                                    }
                                });
                            }
                        }
                    });
            }

            if !self.folder_path.is_empty() {
                ui.separator();
            }

            ui.label(&self.status);

            // Show progress bar only when downloads are active or complete
            if self.is_downloading || (!self.downloading && self.total_downloads > 0) {
                if self.total_downloads > 0 {
                    ui.add_space(10.0);
                    ui.label(format!("Progress: {} / {}", self.downloads_completed, self.total_downloads));
                }
            }
            // Place the progress bar here, outside the ScrollArea. always fit the window
            if (self.is_downloading || (!self.downloading && self.total_downloads > 0)) && self.total_downloads > 0 {
                let progress = self.downloads_completed as f32 / self.total_downloads as f32;
                let window_width = ui.ctx().screen_rect().width();
                let progress_bar = egui::ProgressBar::new(progress)
                    .show_percentage()
                    .fill(egui::Color32::from_rgb(124, 99, 160)) // #7c63a0
                    .desired_width(window_width - 18.0);
                ui.add(progress_bar);
            }
        });

        // When scan finishes, start downloads automatically
        if self.scanning {
            if let Some(rx) = &self.scan_done_receiver {
                if rx.try_recv().is_ok() {
                    self.scanning = false;
                    self.status = "Scan completed.".to_string();
                    self.scan_done_receiver = None;

                    // Start downloads automatically after scan
                    info!("Scan completed, starting downloads automatically");
                    self.start_downloads();
                }
            }
        }

        ctx.request_repaint_after(std::time::Duration::from_millis(200));
    }

    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        info!("Application closed by user");
        info!("");
        info!("---------------------------------------------------------------");
        info!("");
    }
}

// =========================
// === Main Entry Point
// =========================
fn main() {
    // Initialize logging
    if let Err(e) = setup_logging() {
        eprintln!("Failed to initialize logging: {}", e);
    }
    
    info!("Starting Rustitles application");
    
    // Load icon from embedded ICO file
    let icon_data = if let Ok(image) = image::load_from_memory(include_bytes!("../resources/rustitles_icon.ico")) {
        let rgba = image.to_rgba8();
        let size = [rgba.width() as u32, rgba.height() as u32];
        Some(egui::IconData {
            rgba: rgba.into_raw(),
            width: size[0],
            height: size[1],
        })
    } else {
        warn!("Failed to load application icon");
        None
    };

    let window_size = [800.0, 530.0];

    // Use Windows API to get the monitor under the cursor and center the window there
    let center_pos = unsafe {
        let mut point = POINT { x: 0, y: 0 };
        if GetCursorPos(&mut point).is_ok() {
            let monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            let mut info = MONITORINFO {
                cbSize: std::mem::size_of::<MONITORINFO>() as u32,
                ..Default::default()
            };
            if GetMonitorInfoW(monitor, &mut info).as_bool() {
                let work_left = info.rcWork.left;
                let work_top = info.rcWork.top;
                let work_width = (info.rcWork.right - info.rcWork.left) as f32;
                let work_height = (info.rcWork.bottom - info.rcWork.top) as f32;
                let x = work_left as f32 + (work_width - window_size[0]) / 2.0;
                let y = work_top as f32 + (work_height - window_size[1]) / 2.0;
                egui::Pos2::new(x, y)
            } else {
                egui::Pos2::new(100.0, 100.0)
            }
        } else {
            egui::Pos2::new(100.0, 100.0)
        }
    };

    let mut viewport_builder = egui::ViewportBuilder::default()
        .with_inner_size(window_size)
        .with_position(center_pos)
        .with_decorations(true)
        .with_resizable(true)
        .with_min_inner_size([600.0, 461.0]); // Minimum window size to prevent UI elements from disappearing
    
    if let Some(icon) = icon_data {
        viewport_builder = viewport_builder.with_icon(icon);
    }

    let native_options = eframe::NativeOptions {
        viewport: viewport_builder,
        ..Default::default()
    };
    
    info!("Initializing GUI with window size: {}x{}", window_size[0], window_size[1]);
    
    let result = eframe::run_native(
        "Rustitles",
        native_options,
        Box::new(|cc| {
            // Set dark mode
            let mut visuals = egui::Visuals::dark();
            
            // Make text lighter for better readability
            visuals.override_text_color = Some(egui::Color32::from_rgb(248, 248, 242)); // #f8f8f2 (light gray)
            
            // Dracula theme accent colors
            visuals.widgets.active.bg_fill = egui::Color32::from_rgb(189, 147, 249); // #bd93f9 (purple)
            visuals.widgets.hovered.bg_fill = egui::Color32::from_rgb(139, 233, 253); // #8be9fd (cyan)
            visuals.widgets.inactive.bg_fill = egui::Color32::from_rgb(68, 71, 90); // #44475a (darker gray)
            visuals.selection.bg_fill = egui::Color32::from_rgb(189, 147, 249); // #bd93f9 (purple)
            visuals.hyperlink_color = egui::Color32::from_rgb(139, 233, 253); // #8be9fd (cyan)
            visuals.warn_fg_color = egui::Color32::from_rgb(255, 184, 108); // #ffb86c (orange)
            visuals.error_fg_color = egui::Color32::from_rgb(255, 85, 85); // #ff5555 (red)
            visuals.widgets.noninteractive.bg_fill = egui::Color32::from_rgb(68, 71, 90); // #44475a
            visuals.widgets.active.fg_stroke.color = egui::Color32::from_rgb(248, 248, 242); // #f8f8f2 (white text on purple)
            visuals.widgets.hovered.fg_stroke.color = egui::Color32::from_rgb(40, 42, 54); // #282a36 (dark text on cyan)
            
            cc.egui_ctx.set_visuals(visuals);
            
            info!("GUI initialized successfully");
            Box::new(SubtitleDownloader::default())
        }),
    );
    
    // Shutdown logger when app exits
    if let Ok(mut guard) = LOGGER.lock() {
        if let Some(logger) = guard.take() {
            logger.shutdown();
        }
    }
    
    result.expect("Failed to start eframe");
}