# Hardened Velopack helpers

This directory contains rebuilt Windows helpers and one patched assembly from Velopack 1.2.0.
The source commit is `f2edcbcafb81da5b3c884aaea330e225ad91d8b6`.
Velopack uses the MIT license.

The build uses Rust 1.98.1 and the x64 MSVC target. It enables these linker protections:

- Static C runtime
- Control Flow Guard
- CET shadow-stack compatibility
- Reproducible PE linking
- Stable source paths in compiler metadata

Run `tools/build-velopack-helpers.ps1` to reproduce all three files.
The script checks the source, toolchain, output hashes, and PE protections.

The packaging patch repairs PE debug-directory pointers after resource edits.
Velopack otherwise leaves stale pointers when a larger resource section moves raw file data.
That fault removes valid CET metadata from each generated execution stub.

`tools/invoke-vpk.ps1` downloads the official `vpk` 1.2.0 package.
It checks the package hash and replaces the three Windows helpers.
It also replaces `Velopack.Packaging.Windows.dll` with the patched build, then runs `vpk`.
