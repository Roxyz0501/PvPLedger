# Validation — 1.0.0.0 public preview

2026-10-18, Windows x64, .NET SDK 10.0.401, Dalamud.NET.Sdk 15.0.0.

## Measured offline checks

- Release build: zero warnings, zero errors.
- 5,829 passing core, history, CSV, privacy, Discord mock, localization, glyph and isolated production-ImGui checks.
- Randomized 2,000-case Battle High inference replay: approximately 15 ms total on the development host, including allocations and test setup. This is not a game FPS or in-game latency measurement.
- History first page: 25 summary file reads, about 8.7 KB. 500 unchanged accesses: zero additional file reads, writes or bytes transferred. Concurrent requests for a second page: 25 total reads (not per caller). Detail selection: one bounded file read. Navigation: zero writes.
- 60 synthetic match saves: 120 atomic document writes, two per match. One summary and one detail are appended per match; existing matches are not rewritten. Retention only removes expired/excess records after successful saves.
- Tests verify three result layouts, local-player filtering, short/invalid result rejection, duplicate end/result suppression, official-score correction, event cap, assist ambiguity/saturation, character separation, retention expiry, durable notification claims across restart, formula-safe UTF-8 CSV and DPAPI roundtrip.
- Discord tests use a fake HTTP handler: success, HTTP 429 and uncertain failure; one request per invocation, no retries, no opponent names, no mentions. No real webhook was called during development.
- All seven dictionaries have matching nonempty keys; language priority and normalization checked. Every static UI glyph and range symbol is present in all four host Noto CJK faces.
- Actual plugin Draw / settings / language code rendered using native ImGui in an isolated test process, for all seven languages. Japanese history/overlay and German settings inspected visually. Rendering and tab navigation caused no history I/O or configuration saves.
- Installed game build read as `2026.10.06.0000.0000`. All three result hook signatures matched exactly once in the installed executable. This only verifies signature uniqueness, not native callback semantics or packet offsets in a match.
- Canonical `StandardPluginTabs.cs` SHA-256 matches the workspace template byte-for-byte: `062372ECBF0030427BCDFE471592C6691CE3A3D3F8835A7438EA95C7763B25E9`.
- NuGet vulnerable-package query returned no vulnerable dependencies. No additional runtime NuGet package is bundled.

## Design bounds (not in-game performance measurements)

| Operation | Target/bound |
| --- | --- |
| Outside PvP, windows closed | No game-table scans, network or history I/O |
| Active Frontline | At most 4 one-byte Battle High reads/second; infer only on changes |
| Live event | One local state update; at most 16 deduplication comparisons |
| Overlay frame | At most 5 feed rows; no I/O |
| History page | At most 25 summaries, each <=4 KiB; four-page cache |
| Detail | One file <=1 MiB, <=4,096 events; draw 50/page |
| Match end | Two atomic files; optional one Discord POST, no retry |
| Local retention | 1,000 matches, 365 days, 128 MiB per character |
| Config editing | Changed values only, 500 ms debounce |

Directory metadata is enumerated for a character's first history request and after invalidation/expiry; match bodies are not loaded for listing. Retention enumeration runs only after match saves. Explicit exports are outside automatic retention. CSV page exports intentionally limit output to 25 matches rather than silently reading all history.

## Still requires live acceptance

- One actual match each in Crystalline Conflict, Frontline and Rival Wings; compare final K/D/A and outcome to the game scoreboard.
- Validate log-message source/target attribution and compare observed KO/death feed, including anonymous opponent display, pets, environmental deaths and chat filters.
- Validate Frontline's Battle High offset against the visible gauge at 0, intermediate values and 100. Test multiple simultaneous KOs/assists, delayed KO logs and late joins. Estimates must remain marked `~` and never replace official assists.
- Verify duty start, end, immediate exit, disconnect, reconnect, reload mid-match and custom rematch events. Spectator recording is unsupported.
- Verify drag/click-through, DPI, font atlas, toggling seven languages, persistence across reload and character switching inside the game.
- With a user-configured webhook, confirm a single real result post and delivery status. Do not share its URL in issues.

These open items are disclosed in the README, manifest and release notes. This is a public preview, not a claim of completed live game validation.
