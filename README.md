# Kawaii Studio Photobooth

Kiosk-style photobooth app for Windows with a simple customer flow and a frame-driven asset pipeline. This repo currently contains a WPF shell, navigation, asset scanning, template editing, and device scaffolding aligned to the v0.1 spec in `specifications.txt`.

## Scope (current)

- Full screen WPF shell with Startup -> Home -> Thank You flow
- Screen navigation and session state for size, quantity, layout, and frame selection
- Startup checks with test mode, camera error screen, and worker health check
- Frame auto-discovery from folder structure
- Theme backgrounds per screen
- Staff settings screen (pricing, timeouts, hardware, test mode)
- Template editor with drag/resize slot + QR placement per template
- Per-frame overrides saved next to individual frames
- Review selection with slot mapping + composite preview
- Composite rendering with QR overlay and print-ready output
- Print preview screen (4x6, with 2x6 duplicated)
- Cash acceptor RS232 provider (handshake + cash totals)
- Stripe Terminal card flow (worker-backed) with simulator buttons (available in all modes)
- Inactivity handling: home/capture suppressed; post-payment screens auto-advance; review timeout auto-fills

## Customer flow

STARTUP -> HOME -> SIZE -> QUANTITY -> (LAYOUT if 4x6) -> CATEGORY -> FRAME -> PAYMENT -> CAPTURE -> REVIEW -> FINALIZE -> PRINTING -> THANK YOU -> HOME

## Asset folders

The app resolves the config root by searching for `Config/` or `config/` near the executable.

Frames (auto-discovery):

```
Config/frames/
  2x6/<Category>/*.png
  4x6/2slots/<Category>/*.png
  4x6/4slots/<Category>/*.png
  4x6/6slots/<Category>/*.png
```

Optional per-frame overrides live next to the PNG:

```
frame001.png
frame001.layout.json
```

Theme backgrounds (first image per folder is used):

```
Config/themes/default/backgrounds/
  startup/
  error/
  home/
  size/
  quantity/
  layout/
  category/
  frame/
  payment/
  capture/
  review/
  finalize/
  printing/
  thank_you/
  template_editor/
  staff/... (optional)
```

Runtime outputs (ignored by git):

```
Config/logs/YYYYMMDD/session.log
Config/logs/YYYYMMDD/session_1/
  photos/
  preview_frames/
  videos/
  session_1_final.png
prints/
videos/
```

Each `session_x` folder stores captured photos in `photos/`, low-frame-rate live view frames in `preview_frames/`, and the final compiled video in `videos/`.

## Settings

The staff screen reads/writes `Config/appconfig.ini`:

```
PRICE1_26=10
PRICE2_26=20
...
PRICE1_46=15
PRICE2_46=30
...
MAX_QUANTITY=8
CASH_DENOMS=5,10,20
PrintName=DS-RX1
cash_COM=COM4
CARD_PROVIDER=stripe_terminal
STRIPE_TERMINAL_BASE_URL=https://kawaii-studio-server.daawesomej.workers.dev
STRIPE_TERMINAL_READER_ID=
STRIPE_TERMINAL_LOCATION_ID=tml_...
TEST_MODE=false
CAMERA_PROVIDER=simulated
TIMEOUT_DEFAULT=45
TIMEOUT_HOME=45
TIMEOUT_CAPTURE=45
TIMEOUT_REVIEW=45
```

Pricing uses `PRICE{pairs}_{sizeCode}`, where size codes are `26` for 2x6 and `46` for 4x6.

If `STRIPE_TERMINAL_READER_ID` is blank, the app will ask the worker to create a simulated reader (uses `STRIPE_TERMINAL_LOCATION_ID`).

## Templates

Template types are fixed by layout:

- `2x6_4slots`
- `4x6_2slots`
- `4x6_4slots`
- `4x6_6slots`

Template geometry lives in `Config/templates.json`. The staff Template Editor lets you adjust slots + QR per template and save per-frame overrides (stored as `*.layout.json` alongside the frame PNG).

Print output is always 4x6 (1200x1800). For 2x6 prints, the 2x6 composite is duplicated side-by-side into the 4x6 canvas.

## Build

- Windows target: .NET 8 WPF
- The project has `<EnableWindowsTargeting>true</EnableWindowsTargeting>` so it can build on non-Windows hosts.

Open `KawaiiStudio.sln` in Visual Studio 2022 and run the `KawaiiStudio.App` project.

## Notes

- Some screens remain placeholders for device integrations (camera, printer).
- See `specifications.txt` for the full functional spec and acceptance criteria.

## Remaining work (from `specifications.txt`)

- Finish payment flow: richer cash events (inserted/jam), disable intake after paid, and harden card provider errors.
- Capture needs video recording, robust disconnect handling, and per-screen timer tuning.
- Upload hooks, QR destination hosting, and print queue integration are not implemented.
- Config parsing for `config/app.json` and `config/pricing.json` is not implemented.
- Provider interfaces for printer/upload are not defined yet.
- Staff menu secret access, device diagnostics, and log export are not implemented.
