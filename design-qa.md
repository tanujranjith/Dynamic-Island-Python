# AirPods expanded-card design QA

- Defect reference: user-provided local screenshot (not committed with the repository).
- Reference pixels: 977 x 113
- Intended viewport: top-center desktop Island on the user's current monitor, native Windows density.
- State: Normal-size Apple expanded Island, volume/system/clock deck visible, connected AirPods card visible, auto-grow disabled.
- Current implementation evidence: the defect reference above is the post-second-fix capture; the user visually inspected the live post-third-fix root app and confirmed the clipping is gone. No additional desktop capture was retained.

## Findings

- [P1] AirPods card still clipped after the first host-window correction
  - Location: bottom row of the expanded Island.
  - Evidence: the defect reference shows the shell ending through the AirPods card. Runtime logs confirmed the native host and shell had already reached the requested 362 px height, so stale native sizing was no longer the active cause.
  - Root cause: the AirPods row had a fixed 58 px height, while the three-line content and interface scaling needed more room. In addition, disabling optional auto-grow also skipped the content measurement needed to prevent mandatory vertical clipping.
  - Fix: use a 64 px minimum row height, reserve 76 px for the card and margin, apply vertical anti-clipping measurement regardless of the optional auto-grow preference, and measure only after the expanded surface is visible.

- [P1] Expanded shell exceeds the stale transparent host window
  - Location: Island expansion event and native host bounds.
  - Evidence: the post-second-fix screenshot remains clipped. Runtime layout evidence records `window=1200x332`, `shell=900x368`, and `cardBottom=347`; the shell and card are correctly sized but the host cuts them off at 332 px.
  - Root cause: the expanded-state handler animated only the inner shell. If AirPods connected while compact, the native host retained the smaller height calculated at startup.
  - Fix: run the complete layout path on every expansion and whenever AirPods-card visibility changes, including while compact, so host and shell dimensions are recalculated together.

## Required fidelity surfaces

- Fonts and typography: existing Segoe UI Variable styling retained.
- Spacing and layout rhythm: existing 8 px top margin and 16 px corner radius retained; the row may grow vertically when scaling requires it.
- Colors and visual tokens: unchanged.
- Image quality and asset fidelity: existing packaged AirPods image retained at 38 x 38 with uniform scaling.
- Copy and content: unchanged.

## Comparison history

1. Original capture: bottom of the AirPods row clipped.
2. First correction: native host-window sizing used the newly requested dimensions instead of the stale previous measurement.
3. Post-first-fix capture: P1 remained; runtime logs showed `target=900x362` and `final=901x363`, isolating the issue to inner content sizing.
4. Second correction: removed the fixed row height, increased its minimum/reserved space, and made vertical anti-clipping independent of optional auto-grow.
5. Automated evidence: 113 release tests pass; Release build and self-contained publish complete; published and root executable SHA-256 hashes match.
6. Post-second-fix visual evidence: `codex-clipboard-vj5Gfw.png` still shows P1 clipping. Diagnostics isolate the mismatch to a 332 px host containing a 368 px shell whose card ends at 347 px.
7. Third correction: expanded-state and AirPods-visibility changes now run the full host-window layout path before animating the shell.
8. Automated evidence: 113 release tests pass; the Release build and self-contained publish complete; published and root executable SHA-256 hashes match.
9. Post-third-fix visual evidence: the user inspected the same expanded AirPods state in the relaunched root app and confirmed the full card is visible.

## Implementation checklist

- [x] Preserve the existing AirPods card styling and asset.
- [x] Correct host-window dimension selection.
- [x] Make the inner AirPods row responsive to text/interface scaling.
- [x] Enforce mandatory vertical anti-clipping when auto-grow is disabled.
- [x] Add sizing regression coverage and runtime layout diagnostics.
- [x] Recompute the native host window whenever expansion or AirPods visibility changes.
- [x] Build, publish, update, and relaunch the root application.
- [x] Verify the same expanded live state with the user.

## Follow-up polish

- No additional visual mismatch was reported after the final host-window correction.

final result: passed
