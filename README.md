# Kawaii Studio Photobooth

Kiosk-style photobooth app for Windows with a simple customer flow and a frame-driven asset pipeline. This repo currently contains a WPF shell, asset scanning, and screen scaffolding aligned to the v0.1 spec in `specifications.txt`.

## Scope (current)

- Full screen WPF shell with Home → Thank You flow
- Asset library view for frames and backgrounds
- Frame auto-discovery from folder structure
- Theme backgrounds per screen

## Customer Flow

HOME → SIZE → QUANTITY → (LAYOUT if 4x6) → CATEGORY → FRAME → PAYMENT → CAPTURE → REVIEW → FINALIZE → PRINTING → THANK YOU → HOME

## Asset Folders

Frames (auto-discovery):

```
Config/frames/
  2x6/<Category>/*.png
  4x6/2slots/<Category>/*.png
  4x6/4slots/<Category>/*.png
  4x6/6slots/<Category>/*.png
```

Theme backgrounds (first image per folder is used):

```
Config/themes/default/backgrounds/
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
  staff/... (optional)
```

Runtime outputs (ignored by git):

```
sessions/
prints/
videos/
logs/
```

## Templates

Template types are fixed by layout:

- `2x6_4slots`
- `4x6_2slots`
- `4x6_4slots`
- `4x6_6slots`

Template geometry lives in `Config/templates.json`. A starter `2x6_4slots` entry is included based on `Config/frames/2x6/K-Pop/skz.png`.

## Build

- Windows target: .NET 8 WPF
- The project has `<EnableWindowsTargeting>true</EnableWindowsTargeting>` so it can build on non-Windows hosts.

Open `KawaiiStudio.sln` in Visual Studio 2022 and run the `KawaiiStudio.App` project.

## Notes

- The current screens are placeholders for device integrations (camera, printer, payment).
- See `specifications.txt` for the full functional spec and acceptance criteria.

## Remaining Work (from `specifications.txt`)

- Payment logic is placeholder; needs pricing calculation and cash/card flows.
- Capture screen needs countdown, live view, 8-shot capture, and video recording.
- Review/select needs thumbnail grid, slot assignment, and continue gating by filled slots.
- Finalize/printing need composite render, QR generation, upload hooks, and print queue.
- Auto-return timing and post-payment lockout rules are not enforced globally.
- Config parsing for `config/app.json` and `config/pricing.json` is not implemented.
- Session model lacks required fields (sessionId, timestamps, photos, mapping, outputs).
- Provider interfaces for camera/payment/printer/upload are not defined yet.
- Inactivity timers, error handling, and "Call Staff" flow are missing.
- Staff menu (secret access, timers/pricing/devices/logs) is not implemented.
