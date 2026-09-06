# Region review

This Debug-only tool lets Sam mark visual figures while talking to an agent.
It freezes the current app view and records numbered rectangles, optional notes, and page routes locally.

## Use

1. Open the page you want to discuss.
2. Press Ctrl+Shift+A.
3. Drag a rectangle to create figure 1.
4. Drag another rectangle to create figure 2.
5. Press Ctrl+Shift+A to return to the live app.
6. Navigate to another page.
7. Press Ctrl+Shift+A and draw more figures.
8. Refer to any figure by number in voice or chat.

Each new rectangle adds a figure. It does not replace the earlier figures.
Click a figure's numbered badge to select it.
Click the pencil icon on a highlight to write or edit its text note.
Click Save to keep the note or Cancel to discard the edit. N also opens the selected figure's note.
Typing is optional; spoken instructions can refer directly to figure numbers.
Press Delete to remove the selected figure when the note editor is closed.
Remaining figures keep their numbers. Deleted numbers are not reused, including after an app restart.
Press Escape to cancel an open note editor. Otherwise, Escape returns to navigation and keeps the figures.
Ctrl+Shift+A toggles between annotation and navigation. Saved figures remain available in both modes.
Toggling out saves an open note. If saving fails, annotation stays open so you can retry.
Click Resolve in the note editor to finish a note. It moves to history.
Click Notes, or press H, to list notes from every page and window size.
Enable Show resolved notes to read history, view its captures, or reopen a note.
Escape closes the Notes panel before returning to navigation.
Press Ctrl+Shift+Alt+A to delete all open notes. Resolved history and the next figure number remain.

The frozen view stays unchanged while the underlying app updates.
Switching to the conversation keeps completed figures available.
Selection input does not activate controls in the underlying app.
Returning to annotation restores the saved view for the same page, window dimensions, display scale, and sidebar state.
You can select, edit, or delete those figures. Visiting another page retains the earlier page's figures.
Resizing pauses annotation. Reopening at new dimensions captures the live layout as a separate view.
Returning to earlier dimensions, display scale, and sidebar state restores that view's figures and notes.
Figure numbers remain unique across all views in the session. Dimensions describe the client area in logical pixels.
A saved view stays frozen even if live content changes at the same size.
Resolve or delete its open notes, then toggle annotation to capture the updated page.
Separate windows and native popups are outside this prototype.

## Agent access

When Sam says a figure is selected or refers to figure numbers, retrieve the current record:

```powershell
.\tools\read-region-selection.ps1
```

Check `active` before using the record. Inspect the PNG at `imagePath` using the agent's image tool.
Schema 4 includes a `figures` array with each figure's number, identifier, bounds, note, and `pageRoute`.
Each figure also includes `captureId`, `capturedAtUtc`, and `imagePath` for its exact frozen view.
Group figures by `captureId` and inspect every referenced image when reviewing multiple pages.
The `captures` array includes `logicalWidth`, `logicalHeight`, PNG pixel dimensions, and `renderScale` for each capture.
Use these dimensions when a figure describes behavior at a particular window size.
Each capture also records `layoutState` to distinguish expanded and collapsed sidebar views.
`active` means figures are available; `suspended` means the user can navigate the live app.
Use `selectedFigureNumber` for the current selection. Figure numbers belong to the recorded review session.
The top-level single-selection fields remain available for compatibility.
The reader accepts schemas 1 through 4. Schema 4 keeps open notes in `figures` and history in `resolvedFigures`.
History records `resolvedAtUtc` and `resolutionNote`. `nextFigureNumber` preserves numbering after deletion.
Use the capture time and session identity to avoid confusing figures from different reviews.
Schema 4 notes remain valid after the app exits. `processActive` reports whether the recorded process still runs.
Missing PNGs appear in `missingImages`; text feedback remains available. Legacy records still require a running process.
The reader rejects image paths outside the review directory. It does not execute anything from the record.

Captures live under `.region-review/` at the repository root and are gitignored.
Build-output cleanup does not remove this directory. It is local data and does not sync through Git.
The current record is `latest.json`. Each exported image has a unique name.
A separate `rawImagePath` keeps a clean frame so resolved or deleted boxes cannot reappear inside a restored image.
Completed figures and saved notes persist immediately. Closing the app saves an open note editor when storage is available.
If storage fails, the last successful record remains. Unsaved editor text cannot survive a forced process exit.

The first updated run imports the previous `artifacts/diagnostics/region-review/latest.json` when no durable record exists.
Legacy captures lack a clean frame. Their notes remain in Notes as reference captures, with Resolve and View capture actions.
Missing or corrupt clean frames do not erase text notes. The tool reports the problem or provides a reference capture.
Notes already erased by an older build cannot be recovered automatically.

After implementing and verifying feedback, resolve the exact figure:

```powershell
.\tools\set-region-review-status.ps1 -SessionId <recorded-session-id> -FigureNumber 1 -ResolutionNote "Implemented and verified"
```

Use `-Status open` to reopen it. Check `applied` before reporting success.
The script queues commands while the app owns the review, then waits briefly for a receipt.
An open text editor defers commands until editing finishes. `pending: true` is not a confirmed resolution.
With the app closed, the script locks and updates the durable record directly.
Session and figure identity checks prevent changes to another review. A second app cannot overwrite an owned review.
Both scripts accept `-Directory` for an explicit review directory.
No image upload, network listener, or automatic conversation trigger is included.
Sam marks the view and then speaks or writes here; the agent retrieves the figures during that turn.
The record and PNG form the boundary for a later MCP adapter.

## Display scaling

Bitmap source rectangles use physical pixels. Destination rectangles and figure bounds use logical coordinates.
Use the full Bitmap.PixelSize as the image source; Bitmap.Size crops high-DPI captures.
Regression tests check far-edge pixels in displayed and exported frames at 100%, 125%, 150%, 175%, and 200% capture DPI.
The headless window remains at 100%, so changing between live monitors still needs native verification.

## Page routes

Routes identify internal app pages for agents. They are diagnostic identifiers, not clickable deep links.
Examples include `/home`, `/settings`, `/presets`, `/gallery`, and `/modules/explorer/tab/0`.
The tab suffix records the selected tab index, starting at zero.
Each capture keeps its route even after the user navigates elsewhere.

## Validation

Regression tests cover restart restoration, notes across routes and dimensions, stable numbers, resolve/reopen, clean frames,
failed writes, a second instance, missing frames, corrupt records, and deferred agent commands.
Headless screenshots cover Notes in both themes. Native voice interaction and live monitor changes still need manual verification.
