# Rustitles .NET Frontend Plan

## Summary

This plan outlines a complete reimagining of the Rustitles user interface using a .NET WPF frontend that communicates with the existing Rust backend. The new design prioritizes:

1. **System Tray First** - True background operation with always-visible tray icon
2. **Activity-Centric** - Focus on what's happening, not configuration
3. **Progressive Disclosure** - Settings hidden until needed
4. **Windows Native** - Toast notifications, jump lists, proper startup integration

## Feasibility Assessment

### ✅ Highly Feasible

| Aspect | Assessment |
|--------|------------|
| **Technical** | .NET WPF is mature, well-documented, excellent tray support |
| **Communication** | HTTP + SSE is simple, reliable, debuggable |
| **Effort** | ~6-8 weeks for full implementation |
| **Maintenance** | Two codebases (Rust + .NET) but clear separation |
| **User Benefit** | Significantly improved UX, true background operation |

### Trade-offs

| Pro | Con |
|-----|-----|
| Native Windows feel | Windows-only (Linux/macOS keep egui UI) |
| Full tray support | Larger download size (+~50MB for .NET runtime) |
| Modern UI frameworks | Two languages to maintain |
| Better notifications | Requires .NET 8 runtime |

## Documents

| Document | Description |
|----------|-------------|
| [01-architecture-overview.md](01-architecture-overview.md) | System architecture, tech stack, deployment model |
| [02-ui-design-philosophy.md](02-ui-design-philosophy.md) | Activity-centric design principles, visual language |
| [03-system-tray-design.md](03-system-tray-design.md) | Tray icon states, context menu, notifications |
| [04-backend-api-spec.md](04-backend-api-spec.md) | Full HTTP API specification for Rust backend |
| [05-main-window-wireframes.md](05-main-window-wireframes.md) | ASCII wireframes for all UI states |
| [06-implementation-roadmap.md](06-implementation-roadmap.md) | Phased implementation plan with milestones |

## Quick Start (If Approved)

### Phase 1: Proof of Concept (3-4 days)
1. Create basic .NET WPF project with tray icon
2. Add HTTP API to Rust backend (`/health`, `/status`, `/activity/stream`)
3. Connect .NET to Rust, show activity in tray tooltip
4. Demonstrate: Tray icon updates when Plex detects new media

### Decision Point
After Phase 1 PoC, evaluate:
- Does the tray integration work smoothly?
- Is the HTTP communication reliable?
- Is the development velocity acceptable?

If yes → Continue to full implementation
If no → Identify issues and reassess

## Alternatives Considered

| Option | Rejected Because |
|--------|------------------|
| Keep egui + tray-icon crate | Dependency conflict with rfd (file dialog) |
| Electron frontend | Heavy runtime, not native feeling |
| MAUI | Less mature tray support, more complexity |
| Pure Win32 tray | Too low-level, poor UI toolkit |

## Recommendation

**Proceed with .NET WPF frontend.** The benefits significantly outweigh the costs:

- Solves the system tray problem definitively
- Enables a much better user experience
- Clear architecture with HTTP API separation
- Future-proof (API can support other frontends later)

The Rust backend remains the core - handling all subtitle logic, Plex integration, and downloads. The .NET frontend is purely UI and system integration.
