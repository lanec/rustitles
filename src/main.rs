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
use rfd::FileDialog;
use reqwest::blocking::get;
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
                    return Some(version);
                }
            }
        }
    }
    None
}

fn check_subliminal_installed() -> bool {
    // First check if subliminal command is directly available
    if let Ok(output) = run_command_hidden("subliminal", &["--version"], &std::collections::HashMap::new()) {
        if output.status.success() {
            return true;
        }
    }
    
    // Then check as Python module with multiple Python commands
    for cmd in &["python", "py", "python3"] {
        if let Ok(output) = run_command_hidden(cmd, &["-m", "pip", "show", "subliminal"], &std::collections::HashMap::new()) {
            if output.status.success() {
                return true;
            }
        }
        
        // Also try direct module import
        if let Ok(output) = run_command_hidden(cmd, &["-c", "import subliminal; print('subliminal available')"], &std::collections::HashMap::new()) {
            if output.status.success() {
                return true;
            }
        }
    }
    false
}

fn install_subliminal() -> bool {
    for cmd in &["python", "py", "python3"] {
        if let Ok(output) = run_command_hidden(cmd, &["-m", "pip", "install", "subliminal"], &std::collections::HashMap::new()) {
            if output.status.success() {
                return true;
            }
        }
    }
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
// === App State Structs & Enums
// =========================
#[derive(Clone, PartialEq)]
enum JobStatus {
    Pending,
    Running,
    Success,
    Failed(String),
}

struct DownloadJob {
    video_path: PathBuf,
    status: JobStatus,
}

struct SubtitleApp {
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
}

impl Default for SubtitleApp {
    fn default() -> Self {
        let python_version = python_version();
        let python_installed = python_version.is_some();
        let subliminal_installed = if python_installed {
            check_subliminal_installed()
        } else {
            false
        };

        let installing_subliminal = python_installed && !subliminal_installed;
        let subliminal_install_result = Arc::new(Mutex::new(None));

        if python_installed && !subliminal_installed {
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
            selected_languages: vec![],
            folder_path: String::new(),
            scanned_videos: Arc::new(Mutex::new(Vec::new())),
            videos_missing_subs: Arc::new(Mutex::new(Vec::new())),
            scanning: false,
            scan_done_receiver: None,
            download_jobs: Arc::new(Mutex::new(Vec::new())),
            downloading: false,
            download_thread_handle: None,
            cancel_flag: Arc::new(AtomicBool::new(false)),
            concurrent_downloads: 25,
        }
    }
}

impl SubtitleApp {
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

        self.status = "Scanning...".to_string();
        self.scanning = true;
        let (tx, rx) = mpsc::channel();
        self.scan_done_receiver = Some(rx);

        let scanned_videos = Arc::clone(&self.scanned_videos);
        let videos_missing_subs = Arc::clone(&self.videos_missing_subs);
        let folder_path = self.folder_path.clone();
        let selected_languages = self.selected_languages.clone();

        // Clear download jobs since folder changed
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

            for video in &found_videos {
                if SubtitleApp::video_missing_subtitle(video, &selected_languages) {
                    missing_subtitles.push(video.clone());
                }
            }

            *scanned_videos.lock().unwrap() = found_videos;
            *videos_missing_subs.lock().unwrap() = missing_subtitles;

            let _ = tx.send(());
        });
    }

    fn start_downloads(&mut self) {
        if self.downloading || self.selected_languages.is_empty() {
            self.status = "Select at least one language and ensure no downloads are in progress.".to_string();
            return;
        }

        let videos_missing = self.videos_missing_subs.lock().unwrap().clone();
        if videos_missing.is_empty() {
            self.status = "No videos missing subtitles.".to_string();
            return;
        }

        self.status = "Starting subtitle downloads...".to_string();
        self.downloads_completed = 0;
        self.total_downloads = 0;
        self.is_downloading = true;

        let langs = self.selected_languages.clone();
        let jobs: Vec<_> = videos_missing.into_iter()
            .map(|video_path| DownloadJob { video_path, status: JobStatus::Pending })
            .collect();

        self.total_downloads = jobs.len();
        *self.download_jobs.lock().unwrap() = jobs;
        self.downloading = true;

        self.cancel_flag.store(false, Ordering::SeqCst);

        let cancel_flag = Arc::clone(&self.cancel_flag);
        let jobs_arc = Arc::clone(&self.download_jobs);
        let max_concurrent = self.concurrent_downloads;

        self.download_thread_handle = Some(thread::spawn(move || {
            let mut pending_indexes: VecDeque<usize> = (0..jobs_arc.lock().unwrap().len()).collect();
            let mut running_threads = Vec::new();

            while !pending_indexes.is_empty() || !running_threads.is_empty() {
                running_threads.retain(|handle: &thread::JoinHandle<()>| !handle.is_finished());

                while running_threads.len() < max_concurrent && !pending_indexes.is_empty() {
                    if cancel_flag.load(Ordering::SeqCst) {
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

                        // Create cache directory and set environment variables to fix DBM cache issues on Windows
                        let cache_dir = ensure_subliminal_cache_dir().unwrap_or_else(|_| env::temp_dir().join("subliminal_cache"));
                        let mut env_vars = std::collections::HashMap::<String, String>::new();
                        env_vars.insert("PYTHONIOENCODING".to_string(), "utf-8".to_string());
                        env_vars.insert("SUBLIMINAL_CACHE_DIR".to_string(), cache_dir.to_string_lossy().to_string());
                        env_vars.insert("PYTHONHASHSEED".to_string(), "0".to_string());
                        
                        // Build command arguments with multiple -l flags for each language
                        let mut args = vec!["download"];
                        for lang in &langs_clone {
                            args.push("-l");
                            args.push(lang);
                        }
                        
                        // Try multiple ways to run subliminal
                        let mut all_args = args.clone();
                        all_args.push(job_path.to_str().unwrap());
                        
                        let output = run_command_hidden("subliminal", &all_args, &env_vars)
                            .or_else(|_| {
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("python", &python_args, &env_vars)
                            })
                            .or_else(|_| {
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("py", &python_args, &env_vars)
                            })
                            .or_else(|_| {
                                let mut python_args = vec!["-m", "subliminal"];
                                python_args.extend(&all_args);
                                run_command_hidden("python3", &python_args, &env_vars)
                            });

                        let mut jobs_lock = jobs_clone.lock().unwrap();
                        let job_opt = jobs_lock.iter_mut().find(|j| j.video_path == job_path);

                        match output {
                            Ok(out) if out.status.success() => {
                                if let Some(job) = job_opt {
                                    job.status = JobStatus::Success;
                                }
                            }
                            Ok(out) => {
                                let err_str = String::from_utf8_lossy(&out.stderr).to_string();
                                if let Some(job) = job_opt {
                                    job.status = JobStatus::Failed(err_str);
                                }
                            }
                            Err(e) => {
                                if let Some(job) = job_opt {
                                    job.status = JobStatus::Failed(format!("Failed to run subliminal: {}", e));
                                }
                            }
                        }
                    });

                    running_threads.push(handle);
                }

                if cancel_flag.load(Ordering::SeqCst) {
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
        }));
    }

    fn check_download_completion(&mut self) {
        if !self.downloading {
            return;
        }

        // Update progress in real-time
        let jobs = self.download_jobs.lock().unwrap();
        let success_count = jobs.iter().filter(|j| j.status == JobStatus::Success).count();
        let running_count = jobs.iter().filter(|j| j.status == JobStatus::Running).count();
        self.downloads_completed = success_count;

        // Check if download thread is finished
        if let Some(handle) = &self.download_thread_handle {
            if handle.is_finished() {
                self.downloading = false;
                self.download_thread_handle = None;
                
                // Count completed jobs
                let failed_count = jobs.iter().filter(|j| matches!(j.status, JobStatus::Failed(_))).count();
                
                self.status = format!("Downloads completed: {} successful, {} failed", success_count, failed_count);
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

// =========================
// === eframe::App Implementation
// =========================
impl eframe::App for SubtitleApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // Check download completion
        self.check_download_completion();

        if self.installing_python {
            if let Some(result) = self.python_install_result.lock().unwrap().take() {
                self.installing_python = false;
                match result {
                    Ok(_) => {
                        // Refresh environment to pick up new Python installation
                        if let Err(e) = refresh_environment() {
                            eprintln!("Failed to refresh environment: {}", e);
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
                        // Refresh environment to pick up new subliminal installation
                        if let Err(e) = refresh_environment() {
                            eprintln!("Failed to refresh environment: {}", e);
                        }
                        
                        self.subliminal_installed = true;
                        self.status = "✅ Subliminal installed.".to_string();
                    }
                    Err(e) => {
                        self.status = format!("❌ Subliminal install failed: {}", e);
                    }
                }
            }
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            // Custom Dracula heading - larger text
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

                egui::ComboBox::from_label("Select Languages")
                    .selected_text(self.selected_languages.join(", "))
                    .show_ui(ui, |ui| {
                        for (code, name) in &language_list {
                            let mut selected = self.selected_languages.contains(&code.to_string());
                            if ui.checkbox(&mut selected, *name).changed() {
                                if selected {
                                    self.selected_languages.push(code.to_string());
                                } else {
                                    self.selected_languages.retain(|c| c != code);
                                }
                                
                                // Re-scan for missing subtitles when languages change
                                if !self.folder_path.is_empty() {
                                    self.scan_folder();
                                }
                            }
                        }
                    });

                ui.separator();

                ui.horizontal(|ui| {
                    ui.label("Concurrent Downloads:");
                    let mut concurrent_text = self.concurrent_downloads.to_string();
                    if ui.add_sized([25.0, ui.spacing().interact_size.y], egui::TextEdit::singleline(&mut concurrent_text)).changed() {
                        if let Ok(value) = concurrent_text.parse::<usize>() {
                            if value > 0 {
                                self.concurrent_downloads = value.min(100);
                            }
                        }
                    }
                });

                ui.separator();

                ui.horizontal(|ui| {
                    ui.label("Folder to scan:");
                    if ui.button("Select Folder").clicked() {
                        if let Some(folder) = FileDialog::new().pick_folder() {
                            let new_folder = folder.display().to_string();
                            if self.folder_path != new_folder {
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
                    ui.label(format!("Videos missing subtitles: {}", missing_count));
                }

                // Show download jobs status
                let jobs = self.download_jobs.lock().unwrap();
                if !jobs.is_empty() {
                    ui.label("Download Jobs:");
                    ui.separator();
                    egui::ScrollArea::vertical()
                        .max_height(200.0)
                        .show(ui, |ui| {
                            // Set a fixed width to ensure consistent scroll bar positioning
                            let available_width = ui.available_width();
                            ui.set_width(available_width - 20.0); // Reserve space for scroll bar
                            
                            for job in jobs.iter() {
                                let status_text = match &job.status {
                                    JobStatus::Pending => "Pending".to_string(),
                                    JobStatus::Running => "Running".to_string(),
                                    JobStatus::Success => "Success".to_string(),
                                    JobStatus::Failed(err) => format!("Failed: {}", err),
                                };
                                ui.horizontal(|ui| {
                                    ui.label(job.video_path.file_name().unwrap_or_default().to_string_lossy());
                                    ui.label(format!(" - {}", status_text));
                                });
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
                        let progress = self.downloads_completed as f32 / self.total_downloads as f32;
                        ui.add_space(10.0);
                        ui.label(format!("Progress: {} / {} downloads", self.downloads_completed, self.total_downloads));
                        
                        // Custom darker progress bar
                        let progress_bar = egui::ProgressBar::new(progress)
                            .show_percentage()
                            .fill(egui::Color32::from_rgb(124, 99, 160)) // #7c63a0
                            .desired_width(ui.available_width());
                        ui.add(progress_bar);
                    }
                }
            } else {
                // Show message when subliminal is not installed
                ui.label("Please install Python and Subliminal above to start downloading subtitles.");
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
                    self.start_downloads();
                }
            }
        }

        ctx.request_repaint_after(std::time::Duration::from_millis(200));
    }
}

// =========================
// === Main Entry Point
// =========================
fn main() {
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
        .with_resizable(true);
    
    if let Some(icon) = icon_data {
        viewport_builder = viewport_builder.with_icon(icon);
    }

    let native_options = eframe::NativeOptions {
        viewport: viewport_builder,
        ..Default::default()
    };
    eframe::run_native(
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
            
            Box::new(SubtitleApp::default())
        }),
    ).expect("Failed to start eframe");
}