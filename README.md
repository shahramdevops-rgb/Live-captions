# LiveCaptions → Gemini

A low-latency Windows overlay: press a global hotkey and the English text that
Windows 11 **Live Captions** showed since your last press is sent to **Gemini**;
the answer streams into a small always-on-top window that never steals focus.
You never type anything while using it — all configuration lives in `config.json`.

## Requirements
- Windows 11 with Live Captions (`Win + Ctrl + L`), caption language English
- .NET 8 Desktop Runtime (builds with the .NET 8/9/10 SDK)
- A Gemini API key from [Google AI Studio](https://aistudio.google.com/apikey) (free tier works)

## Double-click app (recommended)
Build a single exe into `publish\` and put a **LiveCaptions Gemini** shortcut on the desktop:
```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```
Then just double-click the shortcut. `config.json` (copied once from the repo root) and `log.csv`
live next to the exe in `publish\`; rebuilding never overwrites them. Run the script again after
changing the code (close the app first). Only one instance runs at a time.

## Build & run from source
Run from the repository root, so `config.json` and `log.csv` live there:
```bash
dotnet build
```
```bash
dotnet run --project src/LiveCaptionsGemini
```

On first run `config.json` is created, opened in Notepad, and the app exits. Put your key in it
(or set the `GEMINI_API_KEY` environment variable) and start again.
`config.json` and `log.csv` are git-ignored — never commit your key.

## Using it
On start a **settings window** opens, pre-filled with the last values:
- **System prompt** — who you are, the role, how Gemini should answer (paste freely, multiple lines are fine)
- **Max output tokens** (0 = no limit) and **History turns** (0 = off)

**Start** (`Ctrl+Enter`) saves them to `config.json` and opens the overlay; **Exit** (`Esc`) quits.
Only these values are rewritten in `config.json` — the API key, comments and other fields stay as they are.
Set `"showSettingsOnStart": false` to skip the window.

During the session:

| Key | Action |
|---|---|
| `Ctrl+Shift` — press and release, nothing else | Send the caption text spoken since the last send/skip |
| `F9` | Skip: mark everything currently visible as already sent |

- The dim italic line under the header is a live preview of what the next send would contain.
- At startup nothing has been marked yet, so **everything visible counts as new** — press `F9` first
  if Live Captions still shows old text.
- Pressing send again with no new text sends nothing ("nothing new"); a second press within 300 ms is ignored.
- Send while an answer is still streaming: the new request starts immediately, its answer is shown
  after the current one ("sent · queued").
- Each answer ends with `⏱ first token: X ms · total: Y ms`. Errors are shown in red.
- Drag the window by its header, resize from any edge, close with ✕. Clicking it never takes focus
  away from the app you're using.
- **Text size:** `A−` / `A+` in the header, or `Ctrl` + mouse wheel over the window. Everything scales
  together (status, preview, answers already shown) and the size is saved to `config.json`.
- `Ctrl+Shift+T` and other shortcuts with Ctrl/Shift are not affected: a send only fires when no
  other key was pressed while Ctrl/Shift were held. Keys are never swallowed.

## config.json
Comments and trailing commas are allowed. Looked up in the current directory first, then next to the exe.

| Field | Default | Meaning |
|---|---|---|
| `apiKey` | `""` | Gemini key; empty → `GEMINI_API_KEY` env var |
| `model` | `gemini-3.5-flash-lite` | Gemini model id |
| `thinkingLevel` | `minimal` | Sent as `thinkingConfig.thinkingLevel`; `""` = not sent. If the model rejects it: `minimal` → `low` → not sent |
| `systemPrompt` | concise assistant | System instruction (e.g. add "Plain text only, no markdown.") |
| `maxOutputTokens` | `0` | Answer length cap; `0` = not sent |
| `keepHistory` / `maxHistoryTurns` | `true` / `6` | Previous exchanges sent as context (added only after a successful answer) |
| `sendKey` / `skipKey` | `CtrlShift` / `F9` | `CtrlShift`, `None`, or a key name like `F8`, `Pause`, `Scroll` |
| `pollMs` | `150` | Preview refresh interval |
| `opacity`, `fontSize`, `width`, `height` | `0.92`, `11`, `520`, `360` | Window look |
| `showSettingsOnStart` | `true` | Show the settings window (prompt, max tokens, history) before the overlay |
| `saveTranscript` | `true` | Append every question and answer to `transcript.txt` |
| `saveCaptions` | `true` | Record the whole conversation (everything Live Captions shows) to `captions.txt` |

## transcript.txt
Every sent text and its answer, next to `config.json` — the overlay itself can't be copied from
(it never takes focus), so review or copy answers here after the session:
```text
===== Session 2026-10-30 14:49 =====

[14:52:37] gemini-3.5-flash-lite · first token 832 ms · total 1165 ms
Q: What is the purpose of appsettings.json in .NET Core?
A: So, appsettings json is basically where I put all my configuration settings...
```
Each app start adds a session header. Turn off with `"saveTranscript": false`. Git-ignored.

## captions.txt
The whole conversation: everything Live Captions shows during the session, sent or not, next to
`config.json`. A new timestamped paragraph starts after each pause:
```text
===== Session 2026-10-30 15:00 =====
[15:00:04] Hello, can you tell me what dependency injection is in C#?
[15:00:20] And how is it different from a service locator?
```
Live Captions keeps correcting its last words, so while someone speaks the last 6 words are held
back and written after a 2-second pause (or when the app closes). Text already on screen when the
app starts is not recorded. Turn off with `"saveCaptions": false`. Git-ignored.

## log.csv
One row per request, next to `config.json`:
`time, provider, model, ttft_ms, total_ms, input_chars, output_chars, error`.
If the file is locked (open in Excel) the row is skipped and a message flashes in the status line.

## Latency notes
- One shared `HttpClient` (HTTP/2, keep-alive pings, infinite connection lifetime); a warm-up
  `GET models/{model}` at startup and every 4 minutes keeps TLS open, so a send never pays for a handshake.
- Measured on a free-tier key: `gemini-3.5-flash-lite` first token ≈ 0.6–1 s; `gemini-3.8-flash`
  2.5 to 20+ s (and does not support `minimal` thinking).

## Troubleshooting
| Symptom | Cause / fix |
|---|---|
| Status `○ Live Captions ✗` | Live Captions is closed. Open it (`Win + Ctrl + L`); the app reconnects within a second. It is only auto-started when the app starts. |
| Status `Gemini ✗` + red message | Wrong key or model name — the message says which. |
| `HTTP 429` | Free-tier rate limit; wait or check Rate Limit in AI Studio. |
| `The response ended prematurely` | The model took too long (usually high thinking on a big model). Use a Flash-Lite model / lower `thinkingLevel`. |

Diagnose the network and models (key never printed):
```bash
dotnet run --project tests/LiveCaptionsGemini.Tests -- --diag --model gemini-3.5-flash-lite
```

## Tests
Unit tests (plain console runner, no NuGet; exit code 0 = all passed):
```bash
dotnet run --project tests/LiveCaptionsGemini.Tests
```

Live API test with the key from `config.json` (`--model <name>` overrides the model):
```bash
dotnet run --project tests/LiveCaptionsGemini.Tests -- --live
```

## Project layout
```text
src/LiveCaptionsGemini/
  Program.cs     entry point: loads config, wires the parts together
  Config/        AppConfig — load/create/validate config.json
  Captions/      CaptionsReader (UI Automation), CaptionsPoller (live preview),
                 AnchorText (new-text extraction), ICaptionSource
  Input/         Hotkey (config parsing), KeyboardHook (WH_KEYBOARD_LL on its own thread)
  Providers/     IChatProvider + ProviderResult, GeminiProvider, SharedHttp, ChatHistory
  Pipeline/      SendPipeline — anchor, send/skip, parallel requests with ordered display, warm-up
  UI/            OverlayForm (IOverlayView, A−/A+ zoom), SettingsForm (startup settings),
                 ZoomBlockingRichTextBox, NativeMethods
  Logging/       CsvLogger → log.csv, TranscriptLogger → transcript.txt, CaptionsRecorder → captions.txt
tests/LiveCaptionsGemini.Tests/
  Program.cs     AnchorText / Normalize scenarios + runner
  PipelineTests.cs, CsvLoggerTests.cs, TranscriptLoggerTests.cs, CaptionsRecorderTests.cs, ConfigSaveTests.cs
  LiveGeminiTests.cs, NetworkDiagnostics.cs   --live / --diag modes
```

## Adding a provider (Claude, OpenAI, …)
1. Implement `IChatProvider` in `Providers/` (`WarmUpAsync`, `StreamAsync` writing chunks to the channel
   and completing it; return errors in `ProviderResult.Error` instead of throwing). Reuse `SharedHttp.Client`
   and `ChatHistory`.
2. Create it in `Program.cs` instead of / next to `GeminiProvider`.

Comparison mode (same text to several providers in parallel, answers side by side with TTFT each)
would start one `StreamAsync` per provider in `SendPipeline.Send` and give each its own column in the view.
