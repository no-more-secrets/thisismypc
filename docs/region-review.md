# Region review prototype

This development tool lets Sam point to a visual region while talking to an agent.
It is available in Debug builds only. It does not change Windows settings.

## Use

1. Open the page you want to discuss.
2. Press Ctrl+Shift+A.
3. Drag a rectangle around the region.
4. Describe the change by voice or in chat.
5. Press Escape to clear the selection and return to the app.

The review surface freezes the captured view. The rectangle identifies that frame, even if underlying data changes.
Selection input does not activate controls behind the review surface.
The agent reads the capture automatically; Sam does not need to copy or attach an image.
This prototype covers the main window. Separate native windows and popup surfaces need separate review.

## Agent access

Run from the repository root:

```powershell
.\tools\read-region-selection.ps1
```

The script returns a JSON selection record. Check `active` before using it.
If active, inspect the PNG at `imagePath` with the agent's image tool.
Use the record's capture time and bounds when interpreting the user's instruction.
The script rejects a selection whose originating process has ended or whose image is missing.
It does not open the image or execute anything from the record.

Captures live under `artifacts/diagnostics/region-review/` and are gitignored.
The current record is `latest.json`. Selection images have unique names.
Images stay on disk after clearing; normal build-output cleanup can remove them.
The capture contains the visible app content. No screenshot is sent over a network by this prototype.

## Scope

There is no MCP server, background watcher, or automatic voice-turn trigger in this version.
The agent reads the current record during a review turn using local tools.
The JSON record and PNG form the boundary for a later MCP adapter.
Source mapping, comments, and multiple simultaneous annotations are deferred.

## Validation

Release build passed. All 1,606 CI-safe Debug tests passed.
Four targeted region tests also passed, including the MainWindow diagnostic and keyboard shortcut.
Rendered screenshots were inspected. A separate review checked state and capture failure paths.
Native interaction in the elevated executable, voice coordination, and mixed-monitor scaling remain untested.
The prototype's fixed hint can cover a selection near the top edge.
