# Update signing: GPG release manifest

Out-of-band update verification per the threat model (tm2:54). Every release
publishes a `SHA256SUMS` manifest and a detached signature `SHA256SUMS.asc`
made by either offline release YubiKey. The app embeds both public keys
(`GpgManifestUpdateVerifier.ReleasePublicKeysArmored`) and rejects any update it
cannot positively verify: no manifest, bad signature, digest mismatch, or an
unresolvable package path. There is no fallback. Even if the GitHub account and
the code-signing certificate are both compromised, an attacker cannot forge the
manifest signature.

## Release keys

The ceremony completed on 2026-09-08. Both independent RSA-4096 key sets were
generated inside separate YubiKey 5C NFC devices. No private key was created or
exported outside either YubiKey. Signing requires the user PIN and a physical
touch. Both keys expire on 2028-09-07.

- YubiKey 1: `3D25 3EF5 CB19 A049 F705 FE28 36D9 1EDA 8504 9BED`
- YubiKey 2: `91C8 A2E1 94EF FAC5 E1DD 3DB9 BD46 E205 F028 C7C0`

The combined public key ring is
[`thisismypc-release-public-keys.asc`](thisismypc-release-public-keys.asc).
`GpgManifestVerifierTests.ProductionBuild_ContainsBothCeremonyKeys` pins both
fingerprints in the production build.

GnuPG created one revocation certificate for each key. Store those two files
on protected offline media. A revocation certificate cannot sign a release,
but anyone holding it can revoke its matching public key.

To extend expiry, insert each YubiKey and run `gpg --quick-set-expire` for its
fingerprint. Export and embed the updated public key ring before the old expiry.
Adding a new key also requires an application update before that key signs a
release.

If one key is lost or compromised, use the surviving key to sign an application
release that removes the affected key and embeds a replacement. Existing clients
must install that release while they still trust the surviving key. The app does
not fetch OpenPGP revocations from a keyserver. A client that misses the update
continues to trust its embedded affected key until that key expires.

## Release day

1. Build the Velopack packages; collect every asset for the GitHub release in
   one directory.
2. `.\tools\new-release-manifest.ps1 -AssetDirectory <dir>` writes `SHA256SUMS`.
3. Move `SHA256SUMS` to the offline signing environment. Insert either release
   YubiKey, then select its full fingerprint explicitly:

   ```powershell
   gpg --local-user <fingerprint> --armor --detach-sign SHA256SUMS
   ```

   Enter the user PIN and touch the YubiKey. This produces `SHA256SUMS.asc`.
4. Upload ALL assets, `SHA256SUMS`, and `SHA256SUMS.asc` to the GitHub release.
   The release tag MUST be exactly `v` plus the package version as Velopack
   renders it (e.g. `v1.0.0`): the updater fetches
   `releases/download/v<version>/SHA256SUMS`, and any divergence (prerelease
   formatting, build metadata, a tag typo) 404s the manifest and fail-closes
   every update. Make tag-equals-version a scripted check in the release
   pipeline, not a habit.
5. Keep Velopack's default package naming (`ThisIsMyPC-<version>-full.nupkg`).
   The verifier requires the package file name to carry the version being
   installed; that binding is the downgrade-replay defense (an old, genuinely
   signed manifest replayed under a new tag cannot vouch for a new version).
6. Sanity check before announcing: `gpg --verify SHA256SUMS.asc SHA256SUMS` and
   `sha256sum -c SHA256SUMS` against the uploaded assets.

## How users verify manually

```
gpg --import thisismypc-release-public-keys.asc
gpg --verify SHA256SUMS.asc SHA256SUMS
sha256sum -c SHA256SUMS
```
