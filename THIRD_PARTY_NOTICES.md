# Third-party components

## cloudflared

DirectLink Share bundles the official, unmodified Windows amd64 cloudflared executable, compressed into an application resource. The executable is extracted to a private temporary directory and its SHA-256 is verified before it is started.

- Project: https://github.com/cloudflare/cloudflared
- Publisher: Cloudflare
- Bundled version: **2026.11.1**
- Official release: https://github.com/cloudflare/cloudflared/releases/tag/2026.11.1
- License: **Apache License 2.0**, included at [licenses/cloudflared-LICENSE.txt](licenses/cloudflared-LICENSE.txt) and embedded into DirectLink Share.
- SHA-256 of the original executable: `341deda3db5ec498a0482254d6c38c0b7cf01a30c37bb5dd95d2216d08955e35`.

Cloudflare and cloudflared are third-party products. This repository is not an official Cloudflare product.

Build scripts download cloudflared from its official GitHub release. Generated binaries, downloaded dependencies, local build caches and test output are excluded from version control. Application EXEs are distributed through GitHub Releases.
