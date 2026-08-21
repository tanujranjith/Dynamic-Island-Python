# Q visual assistant

Q is the native visual assistant in Dynamic Island. Press `Ctrl+Alt+Q` from another application to capture the active window, expand the Apple-style Island, and ask a question. The Q panel also supports Ask/Say modes, typed prompts, Windows dictation, OCR, and vision-capable providers.

Supported providers are OpenAI, Anthropic, Gemini, Groq, xAI/Grok, OpenRouter, DeepSeek, and Ollama. Provider credentials are stored in a separate DPAPI-protected file under the current user's Local AppData; they are not part of `settings.json` or exported presets.

Q captures only when invoked. Captured pixels and OCR text remain in memory for the current request/session and are discarded when Q is cleared or the application exits.
