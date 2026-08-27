# Q visual assistant

Q is Dynamic Island’s on-demand visual assistant. Press `Ctrl+Alt+Q` from another
application to capture the current context, expand the Island, and ask a question
without opening a separate chat window.

## Setup

1. Open **Settings → Q Assistant**.
2. Enable Q, select a provider and model, and add the provider API key when one is
   required.
3. Choose **Active window** or **Active monitor** as the capture source.
4. Decide whether to send the captured PNG to vision-capable models. OCR text is
   extracted from the capture for context.
5. Use **Test connection**, then invoke Q with `Ctrl+Alt+Q`.

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
xAI/Grok, OpenRouter, DeepSeek, and Ollama. Providers stream responses through a
common session controller; OpenRouter can discover available models and reports
whether each model supports image input.

## Privacy and data flow

Q captures only when invoked. The captured pixels, OCR text, and conversation
history remain in memory for the current request/session and are discarded when Q
is cleared or the application exits. If **Send screen image** is enabled and the
selected model supports images, the captured PNG is included in that provider
request; otherwise the request uses text/OCR context only.

Q does not provide a local model or an account system. Choosing a hosted provider
means the prompt and any enabled screen image are sent to that provider. Review
the provider’s terms before using Q with sensitive windows, and avoid capturing
passwords, private messages, financial data, or other information you would not
send to that provider.

## Troubleshooting

- If Q does not open, confirm **Enable Q** is on and that another application is
  not reserving `Ctrl+Alt+Q`.
- If the capture is empty, retry with **Active monitor** or bring the source window
  to the foreground. Protected or minimized windows may not expose pixels.
- If a provider returns an error, verify the key, model, Ollama URL, and network
  access, then use **Retry** or **Test connection**.
- If a model responds without text, Q reports an actionable error instead of
  showing a misleading completed response.
