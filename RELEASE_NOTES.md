# Dynamic Island v1.0.6

## Provider API refresh

- Updated OpenAI API-key inference to the Responses API while preserving the separate ChatGPT/Codex subscription provider.
- Updated Anthropic Messages, Gemini `streamGenerateContent`, Groq, xAI/Grok, OpenRouter, DeepSeek, and native Ollama request and streaming contracts from their official developer documentation.
- Added current provider defaults, model discovery, provider/model-specific effort choices, full conversation history, and correct image payloads.
- **Auto** now preserves each model's own reasoning default; explicit model and effort selections are forwarded on the wire.

## Reliability and security

- API keys remain in the Windows user-scoped DPAPI secret store and are sent in provider-defined headers rather than logs or settings exports.
- Added structured, sanitized HTTP and in-stream errors, including request IDs, retry timing, blocked responses, and output-limit failures.
- Added wire-contract tests for every provider, including headers, URLs, models, efforts, images, SSE/NDJSON output, and post-HTTP-200 stream errors.
- Bundled-runtime verification now requires exact SHA-256 coverage for both official Codex executables.
- Fixed expanded AirPods-card clipping by keeping the transparent host window synchronized with dynamic shell content.

## Known limitations

- Hosted API providers use their own API billing and rate limits; only the ChatGPT/Codex provider can use eligible Codex subscription limits.
- Provider model catalogs change independently. Preview models may be renamed or retired, and model availability depends on the configured account.
- Automated tests validate the documented HTTP contracts without sending prompts through the user's paid API keys.

## Verification

- Release test suite: 114 passed.
- Release WPF publish and hash-verified Codex test package: succeeded.
- Clean-extraction smoke test: bundled Codex 0.151.0 selected, signed-in account restored, and 7 models discovered.

---

# Dynamic Island v1.0.5

## ChatGPT / Codex integration

- Added ChatGPT-managed device-code sign-in through the official Codex app-server; no OpenAI API key is required.
- Added live account, plan, usage/reset, model, and model-specific reasoning-effort controls in Settings and the expanded Island.
- Added clear signed-out, expired-session, network, model, usage-limit, server-exit, and cancellation errors.
- Sends the exact selected model/effort, interrupts stopped turns, and deletes only Dynamic-Island-owned Codex threads.
- Keeps the existing OpenAI API-key provider and all other providers intact.

## Codex test distribution and security

- Added a second `DynamicIsland-Codex-Test-v1.0.5-win-x64.zip` release asset with pinned official Codex 0.151.0 Windows binaries.
- Release packaging verifies OpenAI's published SHA-256 checksums; the app verifies a bundled runtime manifest before launch.
- Codex Q turns use an isolated local workspace, read-only/no-approval settings, and reject tool or interactive approval requests.
- OAuth tokens remain owned by Codex. Local diagnostics omit prompts, screenshots, device codes, tokens, and raw credentials.

## Reliability

- Repaired streamed response mojibake and made empty Codex responses actionable.
- Fixed repeated AirPods connection animations when the underlying connection did not change.
- Added timeout enforcement, dynamic model discovery, and regression coverage for runtime integrity, model/effort forwarding, thread ownership, output decoding, and AirPods connection policy.

## Known limitations

- The bundled Codex package is a test build while the official app-server protocol continues to evolve.
- ChatGPT access is limited to eligible Codex service usage; it is not a general OpenAI API key or API credit balance.
- Signing out affects the shared official Codex profile for the current Windows user.

## Verification

- Release test suite: 101 passed.
- Release WPF publish: succeeded with 0 errors.
- Live installed-runtime and bundled-runtime account/model discovery: passed with Codex 0.151.0.

---

# Dynamic Island v1.0.4

## AirPods and BLE device status

- Added native Windows Bluetooth LE detection for paired, connected AirPods and compatible Beats devices.
- Parses Apple manufacturer advertisements for model, left/right/case battery, charging, case-lid, and in-ear state.
- Handles rotating BLE advertisement addresses and uses the active audio endpoint as a connection fallback.
- Displays honest 10% battery ranges such as 90-99% when the public advertisement does not provide exact percentages.
- Keeps battery and model refreshes from replaying expanded-island animations.

## UI reliability

- Added the AirPods connection banner and expanded status card with the bundled AirPods render.
- Fixed clipped/overlapping layout and restored missing banner columns.
- Consolidated the island resize animation to avoid duplicate expand/shrink transitions.

## Deliberate limitation

ANC, transparency, gesture, and other AirPods controls are not included because Windows does not expose a safe public API for those controls in this driver-free integration.

## Verification

- Release test suite: 80 passed.
- Release WPF build: succeeded with 0 errors.
- Physical AirPods hardware validation remains adapter/device dependent and is not represented as automated coverage.
