//! System tray and startup integration for Rustitles
//!
//! Provides Windows startup registration
//! Note: System tray icon is not implemented due to dependency conflicts

/// Placeholder for tray creation - returns None
/// System tray requires tray-icon crate which conflicts with rfd's GTK dependency
pub fn create_tray() -> Option<()> {
    log::info!("System tray not available (dependency conflict)");
    None
}

/// Windows startup registration
#[cfg(windows)]
pub mod startup {
    use std::env;
    
    const APP_NAME: &str = "Rustitles";
    
    /// Check if app is set to run at Windows startup
    pub fn is_enabled() -> bool {
        if let Ok(auto) = auto_launch::AutoLaunchBuilder::new()
            .set_app_name(APP_NAME)
            .set_app_path(&get_exe_path())
            .set_use_launch_agent(false)
            .build()
        {
            auto.is_enabled().unwrap_or(false)
        } else {
            false
        }
    }
    
    /// Enable running at Windows startup
    pub fn enable() -> Result<(), String> {
        let auto = auto_launch::AutoLaunchBuilder::new()
            .set_app_name(APP_NAME)
            .set_app_path(&get_exe_path())
            .set_use_launch_agent(false)
            .build()
            .map_err(|e| format!("Failed to create auto-launch: {}", e))?;
        
        auto.enable().map_err(|e| format!("Failed to enable startup: {}", e))
    }
    
    /// Disable running at Windows startup
    pub fn disable() -> Result<(), String> {
        let auto = auto_launch::AutoLaunchBuilder::new()
            .set_app_name(APP_NAME)
            .set_app_path(&get_exe_path())
            .set_use_launch_agent(false)
            .build()
            .map_err(|e| format!("Failed to create auto-launch: {}", e))?;
        
        auto.disable().map_err(|e| format!("Failed to disable startup: {}", e))
    }
    
    fn get_exe_path() -> String {
        env::current_exe()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_else(|_| "rustitles.exe".to_string())
    }
}

#[cfg(not(windows))]
pub mod startup {
    pub fn is_enabled() -> bool { false }
    pub fn enable() -> Result<(), String> { Ok(()) }
    pub fn disable() -> Result<(), String> { Ok(()) }
}
