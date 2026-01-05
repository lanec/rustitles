# Phase 8: Build and Test

## Overview
Verify that the Plex integration compiles correctly and all dependencies are resolved.

## Prerequisites
- Rust toolchain installed (rustc, cargo)
- Internet connection for downloading dependencies

## Steps

### 1. Build the Project
```bash
cd d:\Dev\rustitles
cargo build
```

### 2. Fix Any Compilation Errors
Common issues to watch for:
- Missing imports
- Type mismatches in async code
- Lifetime issues with database connections

### 3. Run Tests
```bash
cargo test
```

### 4. Verify Feature Flags
Ensure all new dependencies are properly configured:
```toml
# Cargo.toml should include:
rusqlite = { version = "0.31", features = ["bundled"] }
uuid = { version = "1.0", features = ["v4"] }
axum = "0.7"
tower-http = { version = "0.5", features = ["cors"] }
multer = "3.0"
chrono = { version = "0.4", features = ["serde"] }
```

### 5. Test Plex Connection (CLI)
Add a command-line flag to test Plex connectivity:
```bash
cargo run -- --test-plex
```

## Expected Outcome
- Project compiles without errors
- All tests pass
- Application launches successfully

## Troubleshooting

### Missing chrono dependency
```toml
chrono = { version = "0.4", features = ["serde"] }
```

### SQLite linking issues on Windows
Ensure `rusqlite` uses the `bundled` feature to include SQLite.

### Async runtime conflicts
The project uses tokio. Ensure all async code is compatible.
