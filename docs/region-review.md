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
Press N to edit its optional note. Click Save to keep the note or Cancel to discard the edit.
Typing is optional; spoken instructions can refer directly to figure numbers.
Press Delete to remove the selected figure when the note editor is closed.
Remaining figures keep their numbers. Deleted numbers are not reused within that review.
Press Escape to cancel an open note editor. Otherwise, Escape returns to navigation and keeps the figures.
Ctrl+Shift+A toggles between annotation and navigation. Saved figures remain available in both modes.
Toggling out saves an open note. If saving fails, annotation stays open so you can retry.
Press Ctrl+Shift+Alt+A to clear all figures and start a new review session.
Figure numbers reset only when the review session is cleared.

The frozen view stays unchanged while the underlying app updates.
Switching to the conversation keeps completed figures available.
Selection input does not activate controls in the underlying app.
Each return to annotation captures a fresh view. Figures from earlier captures remain in the session record.
Only figures from the current capture appear on the frozen view. Earlier captures retain their own images.
Separate windows and native popups are outside this prototype.

## Agent access

When Sam says a figure is selected or refers to figure numbers, retrieve the current record:

```powershell
.\tools\read-region-selection.ps1
```

Check `active` before using the record. Inspect the PNG at `imagePath` using the agent's image tool.
Schema 3 includes a `figures` array with each figure's number, identifier, bounds, note, and `pageRoute`.
Each figure also includes `captureId`, `capturedAtUtc`, and `imagePath` for its exact frozen view.
Group figures by `captureId` and inspect every referenced image when reviewing multiple pages.
The `captures` array includes image dimensions and scaling for each capture.
`active` means figures are available; `suspended` means the user can navigate the live app.
Use `selectedFigureNumber` for the current selection. Figure numbers belong to the recorded review session.
The top-level single-selection fields remain available for compatibility.
The reader accepts schemas 1 and 2 from older running builds, plus schema 3.
Use the capture time and session identity to avoid confusing figures from different reviews.
The reader rejects an ended process or any missing capture image. It does not execute anything from the record.

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

## Page routes

Routes identify internal app pages for agents. They are diagnostic identifiers, not clickable deep links.
Examples include `/home`, `/settings`, `/presets`, `/gallery`, and `/modules/explorer/tab/0`.
The tab suffix records the selected tab index, starting at zero.
Each capture keeps its route even after the user navigates elsewhere.

## Validation

The CI-safe Debug suite passes all 1,607 tests. The Release solution build passes.
All seven targeted headless tests pass, including page changes, note saving on toggle, deletion, and capture DPI.
Headless screenshots were inspected. Native voice interaction and live monitor changes remain untested.
