# Update signing: GPG release manifest

Out-of-band update verification per the threat model (tm2:54). Every release
publishes a `SHA256SUMS` manifest and a detached signature `SHA256SUMS.asc`
made by an offline release key. The app embeds the public key
(`GpgManifestUpdateVerifier.ReleasePublicKeyArmored`) and rejects any update it
cannot positively verify: no manifest, bad signature, digest mismatch, or an
unresolvable package path. There is no fallback. Even if the GitHub account and
the code-signing certificate are both compromised, an attacker cannot forge the
manifest signature.

## Current state

The embedded public key is EMPTY, so every update is rejected. This is
deliberate: unsigned builds must never pass. The key ceremony below is a
release blocker; `GpgManifestVerifierTests.ProductionBuild_StillHasNoEmbeddedKey_UntilTheCeremony`
pins the empty state and must be flipped in the same commit that embeds the key.

## One-time key ceremony (Sam, offline)

1. On a machine without the repo checked out (or at minimum offline), generate
   the release key. Ed25519 is fine; RSA 4096 maximizes tooling compatibility:

   ```
   gpg --quick-generate-key "ThisIsMyPC Release Signing <releases@LLC-DOMAIN>" rsa4096 sign 2y
   ```

2. Export and store:
   - `gpg --export-secret-keys --armor <keyid> > release-secret.asc`: offline
     storage only (hardware token or encrypted media in a drawer, plus one
     backup). NEVER on the dev machine, NEVER in CI secrets; the whole point is
     that CI compromise cannot sign updates.
   - `gpg --export --armor <keyid> > release-public.asc`: public.

3. Embed the public key: paste the armored block into
   `GpgManifestUpdateVerifier.ReleasePublicKeyArmored` in
   `src/ThisIsMyPC.App/Services/GpgManifestUpdateVerifier.cs`, flip the
   ceremony test, commit. Also publish the public key in the repo README and on
   the release page so users can verify manually.

4. Expiry is 2 years: extending it (`gpg --quick-set-expire`) re-signs the same
   key, so the embedded public key stays valid; rotation to a NEW key requires
   an app update embedding the new key BEFORE releases sign with it.

## Release day

1. Build the Velopack packages; collect every asset for the GitHub release in
   one directory.
2. `.\tools\new-release-manifest.ps1 -AssetDirectory <dir>` writes `SHA256SUMS`.
3. Move `SHA256SUMS` to the offline signing environment;
   `gpg --armor --detach-sign SHA256SUMS` produces `SHA256SUMS.asc`.
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
gpg --import release-public.asc
gpg --verify SHA256SUMS.asc SHA256SUMS
sha256sum -c SHA256SUMS
```
