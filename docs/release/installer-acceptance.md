# Installer acceptance

Use signed test packages in a disposable Windows test machine. Keep the regular development installation separate.
Record the package version, Windows version, display scale, result, and screenshot for each check.
Automated tests cover fake installation outcomes. They do not prove that the signed package installs correctly.

## Prepare the packages

The release maintainer builds two successive test versions under the exact environment in `tools/reproducible-build-environment.json`.
Both packages must pass signature, malware, and binary-hardening checks. Do not bypass the release gate.
Use [the release instructions](packaging.md#building-a-release) and the secure signing prompt.
Keep a snapshot of the clean test machine before installing.

## Fresh installation

1. Open the older test installer.
2. Check that Windows names No More Secrets, LLC as the verified publisher.
3. Approve the Windows permission prompt.
4. Read and accept the license.
5. Clear the desktop shortcut and startup choices.
6. Finish the installation.
7. Open ThisIsMyPC from Start.
8. Check that it opens without an administrator prompt.
9. Check that no desktop shortcut appeared.
10. Check that Settings shows the choices you made during installation.

Expected: one installed application, working launch, and matching choices.

## Upgrade, reinstall, and older versions

1. Change the app theme so you can recognize the saved preference.
2. Close ThisIsMyPC.
3. Open the newer test installer.
4. Check that it identifies the existing installation and offers Update.
5. Complete the update.
6. Open ThisIsMyPC and check that the saved theme remains.
7. Open Windows Settings, then Apps, then Installed apps.
8. Check that ThisIsMyPC appears once.
9. Run the newer installer again and complete its reinstall option.
10. Check that the app still opens with its saved theme.
11. Run the older installer.
12. Check that it explains the newer installation and prevents an older version from replacing it.

## Removal and retained data

1. Run the newer installer.
2. Select Uninstall.
3. Cancel at the confirmation page.
4. Check that ThisIsMyPC still opens.
5. Run the installer again and confirm removal.
6. Check that ThisIsMyPC disappears from Installed apps and Start.
7. Install the newer test version again.
8. Check that the saved theme remains.

Expected: cancellation keeps the installation; removal keeps user settings and history.

## Keyboard, screen reader, and display size

1. Turn on Narrator from Windows Accessibility settings.
2. Open the installer.
3. Use Tab and Shift+Tab to move through each page.
4. Check that Narrator names each button, field, and checkbox.
5. Check that Narrator announces whether checkboxes are selected.
6. Use Space to change a checkbox.
7. Type a folder name in the installation field.
8. Check that spaces remain normal text input.
9. Check that the license can be read without a mouse.
10. Repeat with display scaling at 150% on a small test display.
11. Check that every required control remains reachable and focused controls scroll into view.
12. Check that the installed version, folder, and update explanation are readable.

## Evidence still required

- Signed installation, upgrade, reinstall, older-version refusal, and removal with the new Win32 UI.
- Narrator output from the signed elevated installer.
- In-app update from the unelevated application. This is a separate release check.

The host release preflight on 2026-09-21 stops at the Visual Studio version mismatch.
These checks remain pending until a package built under the pinned environment is available.
