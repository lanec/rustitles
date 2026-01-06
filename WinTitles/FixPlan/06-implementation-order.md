# Implementation Order

## Overview
A phased approach to implementing the fixes, with each phase delivering testable value.

---

## Phase 1: Foundation (Day 1)
**Goal:** Establish the new data models and state machine without breaking existing functionality.

### Tasks

#### 1.1 Create New Models
```
□ Create ScannedItem model with all metadata fields
□ Create QueueItem model for download queue
□ Create ScanResult model
□ Create QueueProgressUpdate model
□ Create WorkflowState enum
□ Create ResultFilter enum
```

#### 1.2 Create ScanService
```
□ Extract scanning logic from MainViewModel
□ Implement ScanPlexAsync with proper progress reporting
□ Implement CheckSubtitleStatusAsync
□ Add cancellation support
□ Add unit tests for ScanService
```

#### 1.3 Add WorkflowState to MainViewModel
```
□ Add WorkflowState property
□ Add state transition methods
□ Wire up state to enable/disable commands
□ Keep existing functionality working during transition
```

### Deliverable
- New services created but not yet wired to UI
- Existing app still works as before
- Tests pass

---

## Phase 2: New Download Queue (Day 2)
**Goal:** Replace the current fire-and-forget download manager with a proper queue.

### Tasks

#### 2.1 Create DownloadQueue Class
```
□ Implement ConcurrentQueue for pending items
□ Implement SemaphoreSlim for concurrency control
□ Implement ManualResetEventSlim for pause/resume
□ Add progress throttling with Timer
□ Implement StartAsync, Pause, Resume, Cancel
□ Implement RetryFailed
□ Add comprehensive logging
```

#### 2.2 Add Events
```
□ OnProgress event (throttled)
□ OnItemCompleted event
□ OnStateChanged event
□ Ensure events are raised on UI thread or handled properly
```

#### 2.3 Database Integration
```
□ Save queue state on app exit
□ Restore queue state on app start
□ Record each download result
```

#### 2.4 Testing
```
□ Unit tests for concurrency limits
□ Unit tests for pause/resume
□ Unit tests for retry logic
□ Integration test with mock subtitle service
```

### Deliverable
- New DownloadQueue class fully functional
- Old DownloadManager deprecated but still available
- Tests prove concurrency is respected

---

## Phase 3: Scan Workflow UI (Day 3)
**Goal:** Implement the new scanning experience with review state.

### Tasks

#### 3.1 Update MainWindow.xaml
```
□ Add StatusBar component (state-aware)
□ Add ActionBar component (state-aware)
□ Add FilterBar component
□ Update item list to use new ScannedItemViewModel
□ Add selection checkboxes
□ Implement virtualization for large lists
```

#### 3.2 Wire Up Scan Flow
```
□ ScanPlex command triggers ScanService
□ Progress updates shown in StatusBar
□ Cancel button works
□ Results populate ScannedItems collection
□ State transitions to Review when complete
```

#### 3.3 Review State Features
```
□ Filter dropdown (All, Missing, Has Subs)
□ Search box filters by title
□ Select All / Deselect All buttons
□ Selection count display
□ "Download Selected" button enabled when items selected
```

### Deliverable
- User can scan Plex and see all results
- User can review and select/deselect items
- No downloads happen until user clicks "Download Selected"

---

## Phase 4: Download Workflow UI (Day 4)
**Goal:** Implement the downloading and complete states with full visibility.

### Tasks

#### 4.1 Download State UI
```
□ Progress bar with percentage
□ Stats display (completed, failed, active, ETA)
□ "Currently downloading" section showing active items
□ Pause/Resume button toggle
□ Cancel button with confirmation
```

#### 4.2 Complete State UI
```
□ Final statistics display
□ Filter to show only failed items
□ "Retry Failed" button
□ Per-item retry button
□ "New Scan" button to start over
□ "Clear All" button
```

#### 4.3 Wire Up Download Flow
```
□ "Download Selected" creates queue and starts
□ Progress updates bound to UI
□ Item status updates in list
□ State transitions properly on completion
```

### Deliverable
- Full download workflow functional
- User can pause, resume, cancel
- User can see all results and retry failures

---

## Phase 5: Manual Search (Day 5)
**Goal:** Allow users to manually search for subtitles when automatic fails.

### Tasks

#### 5.1 Create ManualSearchDialog
```
□ Design dialog layout
□ Search input field
□ Results list with download counts
□ Download button for selected result
```

#### 5.2 Implement Search
```
□ Call OpenSubtitles search API
□ Display results with relevance info
□ Allow user to download selected subtitle
□ Update item status on success
```

#### 5.3 Integration
```
□ Add "Manual Search" button to failed items
□ Open dialog pre-populated with item title
□ Close dialog and update list on success
```

### Deliverable
- Users can manually search and download subtitles
- Failed items can be resolved without restart

---

## Phase 6: Polish & Testing (Day 6)
**Goal:** Fix edge cases, improve performance, comprehensive testing.

### Tasks

#### 6.1 Edge Cases
```
□ Handle empty Plex libraries
□ Handle network disconnection mid-scan
□ Handle API rate limiting gracefully
□ Handle very long file paths
□ Handle special characters in titles
```

#### 6.2 Performance
```
□ Profile with 5000+ items
□ Optimize virtualization
□ Reduce memory usage for large scans
□ Ensure UI remains responsive
```

#### 6.3 Testing
```
□ End-to-end test: scan → review → download → retry
□ Test with real Plex server
□ Test pause/resume during active downloads
□ Test app restart during downloads (state restoration)
```

#### 6.4 Documentation
```
□ Update README with new workflow
□ Add inline code comments
□ Document API contracts
```

### Deliverable
- Production-ready application
- All edge cases handled
- Performance acceptable for large libraries

---

## Migration Strategy

### Keeping Old Code During Transition
```csharp
// In MainViewModel
#if USE_NEW_WORKFLOW
    private readonly ScanService _scanService;
    private readonly DownloadQueue _downloadQueue;
#else
    private readonly DownloadManager _downloadManager;
#endif
```

### Feature Flag
Add to settings:
```json
{
  "UseNewWorkflow": true
}
```

Allow users to fall back if issues found.

### Deprecation Timeline
- **Week 1:** New workflow default, old available via setting
- **Week 2:** Remove old code if no issues reported
- **Week 3:** Clean up feature flag

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Large refactor breaks existing users | Feature flag allows fallback |
| Performance regression with virtualization | Profile early, optimize if needed |
| OpenSubtitles API changes | Abstract API behind interface |
| Database schema changes | Use migrations, version schema |
| UI complexity increases | Keep state machine simple, add logging |

---

## Success Criteria

### Functional
- [ ] Scan completes and shows ALL items (not just missing)
- [ ] User can review items before download starts
- [ ] Exactly N concurrent downloads (verifiable in logs)
- [ ] Pause stops new downloads, active ones complete
- [ ] Resume continues from where paused
- [ ] Failed items can be retried individually or in bulk
- [ ] Manual search works for failed items
- [ ] App restart preserves queue state

### Performance
- [ ] Scan of 2000 items completes in < 2 minutes
- [ ] UI remains responsive during scan
- [ ] UI remains responsive during downloads
- [ ] Memory usage < 500MB for 5000 items

### Usability
- [ ] User always knows what state app is in
- [ ] User can see progress at all times
- [ ] No items "disappear" from view
- [ ] Clear path to resolve failures

---

## File Changes Summary

### New Files
```
Services/ScanService.cs
Services/DownloadQueue.cs
Models/ScannedItem.cs
Models/QueueItem.cs
Models/ScanResult.cs
Models/QueueProgressUpdate.cs
ViewModels/ScannedItemViewModel.cs
Views/ManualSearchDialog.xaml
Views/ManualSearchDialog.xaml.cs
```

### Modified Files
```
ViewModels/MainViewModel.cs (major refactor)
Views/MainWindow.xaml (UI redesign)
Models/AppSettings.cs (add UseNewWorkflow flag)
Services/DatabaseService.cs (add queue persistence)
App.xaml.cs (register new services)
```

### Deprecated Files
```
Services/DownloadManager.cs (remove after migration)
```

---

## Estimated Effort

| Phase | Effort | Complexity |
|-------|--------|------------|
| Phase 1: Foundation | 4-6 hours | Low |
| Phase 2: Download Queue | 6-8 hours | High |
| Phase 3: Scan UI | 4-6 hours | Medium |
| Phase 4: Download UI | 4-6 hours | Medium |
| Phase 5: Manual Search | 3-4 hours | Low |
| Phase 6: Polish | 4-6 hours | Medium |

**Total: 25-36 hours** (3-5 days focused work)

---

## Next Steps

1. Review this plan with stakeholder
2. Set up feature branch: `feature/workflow-redesign`
3. Begin Phase 1 implementation
4. Daily check-ins on progress
5. Demo after each phase completion
