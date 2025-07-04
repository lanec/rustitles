#[cfg(windows)]
extern crate winres;

#[cfg(windows)]
fn main() {
    let mut res = winres::WindowsResource::new();
    res.set_icon("resources/rustitles_icon.ico");
    res.set("SubSystem", "Windows");
    res.compile().unwrap();
    
    // Explicitly set the Windows subsystem to prevent console window
    println!("cargo:rustc-link-arg=/SUBSYSTEM:WINDOWS");
    println!("cargo:rustc-link-arg=/ENTRY:mainCRTStartup");
}

#[cfg(not(windows))]
fn main() {
    // Linux build - no special configuration needed
    println!("cargo:rerun-if-changed=build.rs");
}