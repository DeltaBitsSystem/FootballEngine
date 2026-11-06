# Project-local Blender skills

Installed for OpenCode/OpenChamber, not globally. Entry skill: `footballengine-blender`.

Upstream: https://github.com/RobLe3/cc-blender-skill

Pinned revision: `a2634b588e80265e796aa53f8ab178d75105c2af`.

Nine unmodified specialist skills are installed beside this wrapper: modeling, UV/texturing, animation, lighting, materials, cameras, rendering, export and production workflow. Required shared Markdown references are retained under `text-to-blender/references/`; the upstream orchestrator and its other specialist skills are not installed. MIT notices accompany the downloaded documents. `UPSTREAM.json` records the installed source files and SHA-256 values.

The local wrapper and `AGENTS.md` specify FootballEngine's original-content requirement for **all asset categories**, safe execution policy and reuse of established tools/techniques. Player models, rigs, animation, clothing, equipment, stadiums, textures, skies and clouds share this policy. Upstream stock-asset/HDRI/studio recipes do not override it. A permissively licensed imported asset is not relabelled as original authorship.

OpenCode discovers `.opencode/skills/<id>/SKILL.md` automatically. No `opencode.json` change or OpenChamber UI setting is required. Invoke `@footballengine-blender` explicitly if needed. All nine downloaded specialist skills were confirmed in the current session's available-skill catalog after installation.

Updating is a deliberate review operation: select an upstream commit, review changed instructions, retain the license, check referenced files and update the checksums. Do not use an unpinned installer or overwrite local project policy during updates.
