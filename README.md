# PvP Ledger

A small, quiet personal PvP overlay for FFXIV / Dalamud API 15, by **Roxyz0501**.

**Public preview, 1.0.0.0.** Offline tests and native signature checks are available; actual match recording, Battle High memory reads, in-game overlays, and live Discord delivery have not yet been verified in a running match. Native result/Battle High integration is restricted to game build `2026.10.06.0000.0000`; other builds retain observed kill/death logging and disable native readers.

## できること

- 小さな半透明のK/D/A表示と、自分に関係するキル・デスのログ。初期設定は位置固定・クリック透過。プレビューで移動、倍率・透明度・ログ行数・表示時間を変更できます。
- PvP開始時にリセット。終了後は試合別に記録し、`/pvpledger` で履歴と個別ログを閲覧できます。
- クリスタルコンフリクト／フロントライン／ライバルウィングズの公式リザルトから、最終K/D/Aと勝敗・順位を取得します。
- フロントラインでは、高揚の増加量と観測キル数からアシストの**推定範囲**を表示できます。設定でオン／オフ可能です。
- 履歴の表示ページ（最大25試合）と、選択した試合のイベントログをUTF-8 BOM付きCSVへ出力します。Excel用の式注入対策があります。
- Discord Webhook通知は初期状態でオフ。オンにすると公式結果を受信した試合を1回だけ送信します。

## Installation

Use the existing shared Roxyz0501 custom repository in Dalamud Settings → Experimental → Custom Plugin Repositories:

```text
https://raw.githubusercontent.com/Roxyz0501/DalamudPluginRepo/main/repo.json
```

Install **PvP Ledger**, then run `/pvpledger`. Open Settings to enable the overlay preview and position it. Language settings is a separate tab after Settings. The Support tab is the last tab.

The [release ZIP](https://github.com/Roxyz0501/PvPLedger/releases/latest) is also available for local development installations. No other PvP plugin is required. The plugin does not automate gameplay or send game commands.

## Accuracy and limits

Live K/D comes from structured game log-message events (IDs 557–559), attributed by character name and home world, not translated-text matching. The feed reflects events observed on this client; it is not guaranteed to reproduce every official KO. At match end, the official result replaces the summary counters without inventing missing kill-log entries. Late joins/reloads and interrupted matches are marked. If a result does not arrive within eight seconds after completion, observed data is retained with unknown assists/result and no Discord notification.

**A with `~` is an estimate, not an official count.** For Frontline, the estimator uses the 7.55 KO gains `[4, 6, 8, 12, 16, 20]` and assist gains `[2, 3, 4, 6, 8, 10]`, plus the observed KO total. It considers all possible opponent Battle High levels and combinations, producing a conservative range under those assumptions. For example, +4 with no observed KO may mean one assist or two. Delayed/missing KO logs can make the range wrong; a range of one value is still an estimate. The estimator does not infer opponents or fabricate assist events.

No live assist estimate is shown at gauge 100, on late joins, or when a valid solution is unavailable. Crystal Conflict and Rival Wings show `—` until the final assist count arrives. The official count is stored separately from the optional estimate columns in CSV. Battle High does not decrease on death under the supported rules.

Sources: [official 7.55 gain changes](https://jp.finalfantasyxiv.com/lodestone/topics/detail/dfb99538aaee5e47079411b07356ef3c4a820486), [official 7.5 death-rule change](https://eu.finalfantasyxiv.com/lodestone/topics/detail/07320affa7e0fcd9685afcbe54fbf55405b6d822).

Spectator matches are unsupported; no result is accepted unless the local character's ContentId occurs in it. Custom rematches rely on the host's duty recommence event and require in-game validation. Normal exit/unload saves the active match as interrupted. A game crash can lose the active in-memory match. Observed logs are capped at 4,096 events per match, with a truncation indicator; counters continue to increment.

## Storage, performance and privacy

History stays in the Dalamud plugin configuration directory, separated by a hash of the current character's ContentId. Opponent names are present in local event files and explicitly exported event CSV files. No account IDs or ContentIds are written to history. Login is required to view the current character's history. These local files are not encrypted; this separation is for organization, not protection against another process under the same Windows account.

- No database, server, background web polling, or full player-table scans.
- Match data remains in memory during the match. At the end, one atomic detail file and one atomic summary file are written by a serial background worker.
- Frontline-only Battle High reads: one byte, at most 4 Hz during an active match. Inference only recalculates when the gauge or observed KO count changes.
- History: 25 summaries per page, four cached pages, five-minute cache lifetime checked on requests; save/character changes invalidate the cache. Selected details read only one file and stay in memory. No disk I/O during drawing.
- Per character: at most 1,000 matches, 365 days, or 128 MiB. The oldest data is pruned on a successful match save. Read sizes are capped at 4 KiB per summary and 1 MiB per match. CSV exports are user-controlled and are not automatically pruned.
- Settings are saved only after changes, debounced by 500 ms. CSV export runs in the background.

Discord configuration accepts a standard `https://discord.com/api/webhooks/...` incoming webhook URL, without query parameters. The URL is masked and encrypted with Windows DPAPI for the current Windows account. Store it inside the plugin, not in issues or screenshots. Changing Windows accounts/machines may require entering it again.

Only mode, arena, final outcome, K/D/A, match identifier and end time are sent. Sending your own character name is a separate opt-in. Opponent names and event logs are never sent. Mentions are disabled, redirects are not followed, and HTTP has a 10-second timeout. Local saving completes first. A durable delivery marker is written before POST, and uncertain delivery, HTTP errors and rate limiting never trigger automatic retries. This prevents automatic duplicates but may omit a notification if the process stops after marking it and before sending. Status is shown in the plugin; saved results can be exported locally.

## Languages

Japanese, English, German, French, Korean, Simplified Chinese, Traditional Chinese. On first launch: saved valid language → game language → public Dalamud UI language → English. The resolved language is saved; no Auto choice and no startup overwrite of a saved choice. No public launcher language API is used beyond the host UI-language fallback. Region codes are normalized; ambiguous `zh` is not guessed. Change language in **Language settings**, with immediate effect.

The host-provided Noto Sans CJK is used for all plugin-owned UI text, including Korean, Simplified/Traditional Chinese and accented Latin characters. Game-provided map names retain the game language. Player names, CSV headers, identifiers and URLs are not translated. In-game typography and DPI remain to be verified.

## Build and verification

.NET 10 SDK, Dalamud.NET.Sdk 15.0.0, and a local Dalamud API 15 dev SDK are required. The SDK path defaults to `%APPDATA%/XIVLauncher/addon/Hooks/dev`; override `DalamudLibPath` when needed.

```powershell
dotnet restore PvPLedger/PvPLedger.csproj --locked-mode
dotnet build PvPLedger/PvPLedger.csproj -c Release --no-restore
dotnet run --project Tests -c Release -- artifacts/tests .
```

Tests cover result-packet parsing for all three modes, match-end deduplication, official/observed separation, bounded logs, Battle High ambiguity/caps, storage caching, character separation, CSV escaping, DPAPI, mock Discord responses, localization and isolated native ImGui rendering. Test networking uses a fake handler and never posts to Discord. See [VALIDATION.md](VALIDATION.md) for measured results and live acceptance checks.

## Attribution and license

New standalone implementation, MIT, copyright 2026 Roxyz0501. Native result-packet offsets and signatures reference **[PvP Tracker / PvpStats](https://github.com/wrath16/PvpStats)** by **wrath16**, MIT, commit `ba41904e9cdd2d3c4cb77800945a1a30c8f88715`. The Frontline Battle High offset is also referenced from its content-director definition. The original license is preserved verbatim in `PvPLedger/PvpStats-LICENSE.txt`; see `PvPLedger/NOTICE`. No third-party UI, storage system, game art or font files are bundled. Dalamud, FFXIVClientStructs and Lumina are host dependencies.

Optional development support for Roxyz0501: [Ko-fi](https://ko-fi.com/roxyz0501).
