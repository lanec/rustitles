#[cfg(windows)]
extern crate winres;

#[cfg(windows)]
fn main() {
    let mut res = winres::WindowsResource::new();
    res.set_icon("resources/rustitles_icon.ico");
    res.set("SubSystem", "Windows");
    res.compile().unwrap();
}

#[cfg(not(windows))]
fn main() {}