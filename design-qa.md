# Q Island Design QA

**Source visual truth**

- Local generated design reference (not committed to the repository).
- Source pixels: 1684 x 934.

**Implementation evidence**

- Pre-fix user capture (local temporary file; not committed to the repository).
- Capture pixels: 901 x 511.
- Native WPF surface; CSS viewport and browser device scale do not apply. Windows display density was not available in the capture metadata.
- State: Q expanded, Complete, empty provider response, Chrome source, dark theme.

**Full-view comparison evidence**

- The Q shell preserves the mockup's overall information architecture: identity header, source/status row, user card, assistant card, Ask/Say controls, new/quit actions, and pinned composer.
- The implementation capture exposed media artwork through the shell's top-left rounded corner. The mockup has a fully opaque, consistently clipped shell.
- The implementation also showed a native white text-box scrollbar and an empty assistant response while reporting Complete; neither appears in the visual target.

**Focused region comparison evidence**

- Top-left corner: a bright media-art fragment is visible outside the apparent rounded shell in the implementation; the source has a clean rounded mask.
- Composer: the implementation shows a white vertical scrollbar at the right edge of the prompt field; the source composer is visually uninterrupted.
- Assistant card: the implementation displays the empty-state prompt after submission while its status says Complete; the source shows either active streaming content or a real response.

**Findings and comparison history**

- [P1] Rounded-shell layer bleed.
  - Earlier evidence: media artwork visible through the top-left corner.
  - Fix made: apply a real `RectangleGeometry` rounded clip to the entire `GlassShell` and refresh it whenever shell size or settings change.
  - Post-fix visual evidence: unavailable; the native overlay is not exposed as a targetable window to the automated capture helper.
- [P1] Empty provider streams incorrectly reported as Complete.
  - Earlier evidence: status reads Complete while the assistant card says “Ask Q about what you’re looking at.”
  - Fix made: convert an empty completed stream into an actionable Error state with Retry guidance.
  - Post-fix evidence: covered by the new unit test; native visual capture remains unavailable.
- [P2] Native white composer scrollbar diverges from the mockup.
  - Earlier evidence: white up/down scrollbar inside the prompt field.
  - Fix made: hide the prompt field's native vertical scrollbar while retaining multiline input and Shift+Enter.
  - Post-fix visual evidence: unavailable for the same native-capture limitation.

**Required fidelity surfaces**

- Fonts and typography: Segoe UI Variable hierarchy is consistent with the source; post-fix runtime antialiasing not recaptured.
- Spacing and layout rhythm: overall structure matches, but post-fix corner and composer rendering require a fresh capture.
- Colors and visual tokens: dark navy shell/cards and blue accent are consistent with the source.
- Image quality and asset fidelity: no generated visual assets are required inside Q; the unintended album-art fragment was a clipping defect and is now masked in code.
- Copy and content: empty completion now produces a clear provider error instead of misleading placeholder copy.

**Implementation checklist**

- [x] Apply physical rounded clipping to the complete Island surface.
- [x] Remove the native composer scrollbar.
- [x] Handle empty provider responses as errors.
- [x] Add regression coverage and run the complete test suite.
- [ ] Capture the corrected native Q surface in the same state and confirm the corner is clean.

final result: blocked

Blocker: a post-fix native WPF overlay screenshot is not available from the automated capture helper; visual confirmation needs the next user screenshot.

## Documentation publishing note

The raw desktop captures in `test-artifacts/` are local QA evidence and may show
the contents of the active test window. The public README uses only the clean
settings and timer crops; do not publish full-desktop captures without reviewing
and redacting their visible content first.
