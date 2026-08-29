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
