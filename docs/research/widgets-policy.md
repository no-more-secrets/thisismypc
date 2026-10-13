# Widgets in the Desktop Grand Pack

The clean Windows 11 Pro 25H2 guest (build 26200.6584) shows Widgets with
`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa` absent.
The first fix corrected that default. A real Apply then stopped with AccessDenied.
The 60 earlier changes completed and remained in History.

A separate native registry probe opened the account's Advanced key with WriteKey,
SetValue, and ReadPermissions. Writing TaskbarDa still failed. This does not prove
a TrustedInstaller ownership problem. No registry permissions or Windows protection
services were changed.

Writing `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests=0` succeeded.
The probe restored the original absent policy. Microsoft documents this as the
[Allow Widgets policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests).

Grand Pack 1.2.1 replaces its per-user Widgets button choice with **Allow Widgets**
set to off. This disables the Widgets board and weather button for all accounts on
the PC. It uses the pending-changes pipeline and captures the exact existing policy,
including absence, for Undo. The Explorer Taskbar page provides the same choice.
An unknown, unreadable, or incorrectly typed policy does not stage.
The control requires Pro, Enterprise, or Education. Home and unknown editions
cannot stage it.

The original **Taskbar widgets** preference remains separate. Windows can reject
that preference. Imported older sets can still request it. The separate policy is
not an API for changing only the current account's button preference.

The policy write is verified in this Pro guest. Its visible effect after an Explorer
restart and complete Grand Pack Apply/Undo need manual verification. Windows Home
and later Windows builds were not tested. This is an unsigned preview, not a signed
release or update acceptance result.
