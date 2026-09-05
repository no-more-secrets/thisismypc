# Region review

This Debug-only tool lets Sam mark visual figures while talking to an agent.
It freezes the current app view and records numbered rectangles and optional notes locally.

## Use

1. Open the page you want to discuss.
2. Press Ctrl+Shift+A.
3. Drag a rectangle to create figure 1.
4. Drag another rectangle to create figure 2.
5. Refer to the figures by number in voice or chat.

Each new rectangle adds a figure. It does not replace the earlier figures.
Click a figure's numbered badge to select it.
Press N to edit its optional note. Click Save to keep the note or Cancel to discard the edit.
Typing is optional; spoken instructions can refer directly to figure numbers.
Press Delete to remove the selected figure when the note editor is closed.
Remaining figures keep their numbers. Deleted numbers are not reused within that review.
Press Escape to cancel an open note editor. Otherwise, Escape ends the review and clears its figures.
A new review starts numbering at 1 with a new session identity.

The frozen view stays unchanged while the underlying app updates.
Switching to the conversation keeps completed figures available.
Selection input does not activate controls in the underlying app.
This version reviews one main-window view at a time. Separate windows and native popups need separate review.

## Agent access

When Sam says a figure is selected or refers to figure numbers, retrieve the current record:

```powershell
.\tools\read-region-selection.ps1
```

Check `active` before using the record. Inspect the PNG at `imagePath` using the agent's image tool.
Schema 2 includes a `figures` array with each figure's number, identifier, bounds, and note.
Use `selectedFigureNumber` for the current selection. Figure numbers belong to the recorded review session.
The top-level single-selection fields remain available for compatibility.
The reader accepts schema 1 from an older running build as well as schema 2.
Use the capture time and session identity to avoid confusing figures from different reviews.
The reader rejects an ended process or a missing image. It does not execute anything from the record.

Captures live under `artifacts/diagnostics/region-review/` and are gitignored.
The current record is `latest.json`. Each exported image has a unique name.
Images remain after clearing. Normal build-output cleanup can remove them.
No image upload, network listener, or automatic conversation trigger is included.
Sam marks the view and then speaks or writes here; the agent retrieves the figures during that turn.
The record and PNG form the boundary for a later MCP adapter.

## Display scaling

Bitmap source rectangles use physical pixels. Destination rectangles and figure bounds use logical coordinates.
Use the full Bitmap.PixelSize as the image source; Bitmap.Size crops high-DPI captures.
Regression tests check far-edge pixels in displayed and exported frames at 100%, 125%, 150%, 175%, and 200% capture DPI.
The headless window remains at 100%, so changing between live monitors still needs native verification.

## Validation

The CI-safe Debug suite passes all 1,607 tests. The Release solution build passes.
Targeted headless tests cover multiple figures, notes, deletion, failure handling, and capture DPI.
Headless screenshots were inspected. Native voice interaction and live monitor changes remain untested.
