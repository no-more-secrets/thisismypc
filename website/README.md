# ThisIsMyPC website

Local product-site preview for thisismypc.com. The static build deploys to the dedicated Cloudflare Pages project `thisismypc`. The custom domain is thisismypc.com.

Run `npm ci`, then `npm run dev` from this directory. Preview: http://127.0.0.1:3220/.

Run `npm run build` for the static site in `out/`. Run `npm run typecheck` for source checks. Deploy that directory with `npx wrangler@4.151.0 pages deploy out --project-name thisismypc --branch main`. This is an existing Pages project; do not convert it to a Worker.

The site reuses RE Universe's Surface renderer, Figma corner geometry, purple materials, cream text, and IBM Plex Sans. The dotted background is static. No WebGL animation runs. The app icon and screenshot come from this repository. Website source retains the imported AGPL license and additional permission in this directory; the native app's root license is unchanged. No React Bits component is included. Font notices are in public/design/licenses/ibm-plex.txt.

Product copy follows the root README. The build-from-source action remains until a published installer exists. The screenshot is copied from docs/assets/thisismypc-presets.png. Refresh that copy when the source screenshot changes.
