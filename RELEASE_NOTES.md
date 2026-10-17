# PvP Ledger 1.0.0.0 — public preview

自分のPvPを控えめに記録する、Roxyz0501の新規独立プラグインです。

- 半透明・クリック透過の小型K/D/Aオーバーレイと自分のキル／デスログ。位置・倍率・背景・ログ表示を設定できます。
- PvP開始でリセットし、試合終了後に履歴をローカル保存。試合一覧・個別ログ・CSV出力に対応。
- フロントラインの高揚からアシストを推定範囲で表示（切替可）。高揚100や途中参加時は推定を表示しません。
- クリスタルコンフリクト／フロントライン／ライバルウィングズの公式リザルトで最終K/D/Aを確定。
- 任意のDiscord Webhook通知（初期オフ、1試合1回）。URLはWindows DPAPIで暗号化。相手の名前・イベントログは送信しません。
- 7言語、共通言語設定タブ、任意の支援タブ。

`/pvpledger` で開きます。共通リポジトリは [repo.json](https://raw.githubusercontent.com/Roxyz0501/DalamudPluginRepo/main/repo.json) です。

**試用版の注意点:** 実際のPvP試合中のイベント取得・高揚値・画面表示・Discord実送信は未検証です。試合中のK/Dは観測値、`~`付きAは推定値です。公式結果の取得後に確定します。CC/RWのAは終了まで未取得です。ネイティブ連携の対象ゲームビルドは `2026.10.06.0000.0000`。別ビルドでは結果・高揚取得を停止します。

Releaseビルドは警告／エラー0件、5,829検証に合格。7言語の字形と独立ImGui描画、3種目のリザルトパーサー、重複防止、CSV、キャッシュ、疑似Discord応答を検証しました。実行ファイル上の3シグネチャは各1箇所に一致しましたが、試合中の動作確認を代替するものではありません。

See [README](https://github.com/Roxyz0501/PvPLedger#readme) and [validation details](https://github.com/Roxyz0501/PvPLedger/blob/main/VALIDATION.md).
