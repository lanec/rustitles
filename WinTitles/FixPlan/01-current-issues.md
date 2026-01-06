# Current Issues Analysis

## Overview
The WinTitles application has significant usability problems in its Plex scanning and subtitle downloading workflow. This document analyzes the root causes and symptoms.

---

## Issue 1: Race Condition - Scan and Download Happen Simultaneously

### Symptom
- Files appear and disappear from the UI before user can see them
- Progress counter moves too fast to track
- Items seem "lost" - never showing completion status

### Root Cause
The current implementation starts downloading immediately as files are discovered during the Plex scan. There's no separation between:
1. **Discovery phase** (finding files)
2. **Review phase** (user sees what was found)
3. **Download phase** (processing the queue)

### Code Location
- `MainViewModel.cs` - `ScanPlexAsync()` method
- `PlexService.cs` - Returns items that immediately get queued

### Impact
- User has no opportunity to review what was found
- No chance to exclude items
- Cannot see the full picture before processing begins

---

## Issue 2: Concurrent Downloads Limit Not Respected

### Symptom
- Setting says "25 concurrent downloads" but behavior suggests all items process at once
- API rate limiting errors likely occurring
- System overwhelmed

### Root Cause
The `DownloadManager` uses `SemaphoreSlim` but:
1. The semaphore may not be correctly limiting parallel tasks
2. The `ProcessJobAsync` fires-and-forgets without proper awaiting
3. No backpressure mechanism exists

### Code Location
- `DownloadManager.cs` - `_semaphore` and `ProcessJobAsync()`
- `MainViewModel.cs` - How jobs are enqueued

### Impact
- OpenSubtitles API rate limits exceeded
- UI thread overwhelmed with updates
- Items processed faster than UI can display

---

## Issue 3: UI Updates Lost Due to Threading

### Symptom
- Items flash in the list then disappear
- Final status never shown for many items
- Count shows "0 of 445" but items scroll by

### Root Cause
- `ObservableCollection` updates from background threads
- No debouncing/batching of UI updates
- Items removed or replaced before rendering completes

### Code Location
- `MainViewModel.cs` - `OnJobUpdated` event handler
- `RecentActivity` collection updates

### Impact
- User cannot track what's happening
- Lost visibility into success/failure
- Impossible to know which items need attention

---

## Issue 4: No Distinct Workflow States

### Symptom
- App jumps from "Scan Plex" directly to downloading
- No intermediate "review" state
- No way to pause before downloads begin

### Root Cause
The application lacks a proper state machine:
```
Current:  IDLE → SCANNING+DOWNLOADING → IDLE

Should be: IDLE → SCANNING → REVIEW → DOWNLOADING → COMPLETE
```

### Impact
- User feels out of control
- No opportunity to make decisions
- Workflow feels chaotic

---

## Issue 5: No Filtering or Item Management

### Symptom
- Cannot filter to see only failed items
- Cannot select items to retry
- Cannot exclude items from download
- Cannot manually search for specific items

### Root Cause
- UI designed as a simple list, not a management interface
- No filter state in ViewModel
- No selection model for batch operations

### Impact
- Failed items require app restart to retry
- No way to handle edge cases
- User cannot intervene when automation fails

---

## Issue 6: Progress Information Inadequate

### Symptom
- "Downloading: 0 of 445" doesn't update meaningfully
- No ETA or speed information
- No per-item progress

### Root Cause
- `CompletedJobs` counter not updated correctly
- No tracking of in-flight vs completed items
- Progress bar calculation wrong

### Impact
- User doesn't know how long to wait
- Cannot tell if app is working or stuck
- Anxiety about whether to intervene

---

## Summary Table

| Issue | Severity | Effort to Fix |
|-------|----------|---------------|
| Race condition (scan+download) | **Critical** | Medium |
| Concurrency limit broken | **Critical** | Low |
| UI updates lost | **High** | Medium |
| No workflow states | **High** | High |
| No filtering | **Medium** | Medium |
| Progress inadequate | **Medium** | Low |

---

## Next Steps

See the following documents:
- `02-new-architecture.md` - Proposed solution architecture
- `03-scan-workflow.md` - New scanning workflow
- `04-download-queue.md` - Proper queue implementation
- `05-ui-redesign.md` - UI changes needed
- `06-implementation-order.md` - Step-by-step fix plan
