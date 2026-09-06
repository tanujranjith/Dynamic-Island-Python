# Q visual assistant

Q is Dynamic Island’s on-demand visual assistant. Press `Ctrl+Alt+Q` from another
application to capture the current context and ask a question without opening a
separate chat window. By default, Q expands the Island automatically. Disable
**Auto-expand island for Q** in **Settings → Q Assistant** if you want Q to stay
compact when it is invoked.

## Setup

1. Open **Settings → Q Assistant**.
2. Enable Q, select a provider and model, and add the provider API key when one is
   required.
3. Choose **Active window** or **Active monitor** as the capture source.
4. Decide whether to send the captured PNG to vision-capable models. OCR text is
   extracted from the capture for context.
5. Choose **Reasoning effort**. **Auto** uses the provider/model default; explicit
   values are sent when supported by the selected model.
6. Use **Test connection**, then invoke Q with `Ctrl+Alt+Q`.

In **Settings → Q Assistant**, enable **Auto-close Q after response** if you want
completed Q sessions to close automatically. **Q auto-close delay** controls the
wait before closing, from 1 to 300 seconds; it is off by default.

### ChatGPT / Codex sign-in

The **ChatGPT / Codex** provider uses the official Codex app-server and does not
need an OpenAI API key. For the simplest test, download the release asset named
`DynamicIsland-Codex-Test-v1.0.6-win-x64.zip`, extract the whole folder, and run
`DynamicIsland.exe`. The normal `DynamicIsland.exe` remains available for users
who already have a supported official Codex installation.

In Q settings, choose **ChatGPT / Codex**, select **Sign in with ChatGPT**, and
enter the displayed device code at the official verification URL. The app-server
owns the OAuth login and token-refresh lifecycle; Dynamic Island receives account
status but never reads, copies, logs, or stores the OAuth token.

Runtime lookup is deterministic: a bundled and SHA-256-verified Codex 0.151.0
runtime is preferred, then a supported official per-user installation, then
`codex.exe` on `PATH`. A present but modified/incomplete bundle is rejected rather
than silently bypassed. The bundled ZIP is intentionally a test distribution so
the Codex version can be pinned while the app-server protocol evolves.

This provider uses Codex service access associated with the signed-in ChatGPT account,
including the account's Codex-specific model availability and rate limits. It does
not turn a ChatGPT subscription into a general OpenAI API key, and it does not use
API-platform billing or API credits. The API-key OpenAI provider remains available
separately.

The signed-in account is shared with official Codex apps on the same Windows user
profile. Signing out from Dynamic Island therefore signs that shared Codex profile
out everywhere; the UI warns before doing so. Model and effort choices come from
`model/list`, and Q passes the exact selected model and supported effort into each
turn. The displayed usage percentage and reset time come from Codex rate-limit
status. Access is subject to the account's eligible Codex subscription limits and
may vary by plan, model, region, rollout, and OpenAI policy.

Provider credentials are stored outside `settings.json` in a Windows user-scoped
DPAPI-protected file. They are not included in exported presets or repository
files. Ollama can be configured with its local base URL and does not require an
API key.

## Ask, Say, and shortcuts

- **Ask** answers, explains, solves, or analyzes the visible context.
- **Say** suggests concise first-person wording for what to say next; it does not
  claim that an action was taken.
- Type a prompt, use Windows dictation when available, or send follow-ups from
  the composer.
- Use **Stop**, **Copy**, **Retry**, **New question**, and **Quit Q** as the session
  state allows.
- Create one-click prompt buttons under **Quick shortcuts** in Q settings. The
  `Ctrl+Alt+Q action` setting can optionally run one of those shortcuts after the
  screen capture completes. Leave it as **None** to open Q without auto-submitting.

## Providers

The built-in provider registry supports OpenAI, Anthropic, Google Gemini, Groq,
xAI/Grok, OpenRouter, DeepSeek, and Ollama. Each adapter uses its provider's native
current contract instead of assuming that every API is interchangeable:

- OpenAI uses the [Responses API](https://developers.openai.com/api/reference/resources/responses/methods/create)
  with `max_output_tokens`, `reasoning.effort`, and Responses image parts.
- Anthropic uses the [Messages API](https://platform.claude.com/docs/en/api/messages/create)
  with `x-api-key`, `anthropic-version`, `output_config.effort`, and Anthropic SSE events.
- Gemini uses [`streamGenerateContent`](https://ai.google.dev/api/generate-content)
  with the key in `x-goog-api-key`, `thinkingConfig`, Gemini history roles, and `inlineData` images.
- Groq uses its [OpenAI-compatible chat endpoint](https://console.groq.com/docs/api-reference)
  with `max_completion_tokens` and model-supported reasoning effort.
- xAI uses [streaming Chat Completions](https://docs.x.ai/developers/model-capabilities/text/streaming)
  with Grok reasoning effort and image content.
- OpenRouter uses [Chat Completions](https://openrouter.ai/docs/quickstart), discovers
  model capabilities, and maps its per-model reasoning metadata.
- DeepSeek uses its [Chat Completions API](https://api-docs.deepseek.com/api/create-chat-completion/)
  with its thinking and reasoning controls; screenshots are sent only to a selected vision model.
- Ollama uses the native local [`/api/chat`](https://docs.ollama.com/api/chat) and
  `/api/tags` APIs, including NDJSON streaming, `think`, and base64 `images`.

Model lists are refreshed by **Test connection** when a provider exposes discovery.
The model menu retains a current suggested default as an offline fallback. **Auto**
omits an explicit effort whenever possible so the provider/model default wins;
selecting another effort sends that exact supported value.

Provider APIs and model catalogs evolve independently. Preview models can be renamed
or retired, an account may not have every suggested model, and some models ignore or
reject effort/image fields they do not support. Use **Test connection** after changing
a provider or model. API-key providers use that vendor's API billing and rate limits;
they do not consume a ChatGPT subscription. Only **ChatGPT / Codex** uses eligible
Codex subscription limits.

## Privacy and data flow

Q captures only when invoked. The captured pixels, OCR text, and conversation
history remain in memory for the current request/session and are discarded when Q
is cleared or the application exits. If **Send screen image** is enabled and the
selected model supports images, the captured PNG is included in that provider
request; otherwise the request uses text/OCR context only.

Q does not provide a local model or its own account system. Choosing a hosted provider
means the prompt and any enabled screen image are sent to that provider. Review
the provider’s terms before using Q with sensitive windows, and avoid capturing
passwords, private messages, financial data, or other information you would not
send to that provider.

For Codex, Q creates a temporary isolated workspace under the app's local-data
folder, requests read-only operation with approvals disabled, rejects any tool or
interactive approval request, and deletes only the app-owned Codex thread after
the request. Temporary image files are deleted after the turn. Diagnostics are
local and sanitized: they can include runtime source/version, model, effort, and
failure category, but not prompts, screenshots, device codes, OAuth tokens, or
raw account credentials. These controls reduce risk; they do not make hosted
inference private from OpenAI.

## Troubleshooting

- If Q does not open, confirm **Enable Q** is on and that another application is
  not reserving `Ctrl+Alt+Q`.
- If the capture is empty, retry with **Active monitor** or bring the source window
  to the foreground. Protected or minimized windows may not expose pixels.
- If a provider returns an error, verify the key, model, Ollama URL, and network
  access, then use **Retry** or **Test connection**.
- If the Codex provider cannot start, re-download and fully extract the bundled
  test ZIP, or install/update official Codex so a supported `codex.exe` is found.
- If sign-in expires, use the account control in Q settings or the expanded Island
  to sign in again. If a usage limit is reached, wait for the displayed reset time
  or use a different configured provider.
- Codex app-server is an evolving integration surface. Q currently uses it through
  local stdio and does not grant Codex command execution, file changes, or MCP
  actions from the Q flow.
- If a model responds without text, Q reports an actionable error instead of
  showing a misleading completed response.
