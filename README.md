# Kawaii Studio Photobooth

Kiosk-style photobooth app for Windows with a complete customer flow and frame-driven asset pipeline. Built with WPF (.NET 8), featuring full-screen navigation, asset auto-discovery, template editing, payment integration (Stripe Terminal + cash acceptor), photo capture, video compilation, and cloud upload capabilities. Aligned to the v0.1 spec in `specifications.txt`.

## Implemented Features

### Customer Flow
- Complete full-screen WPF kiosk application with full customer journey
- Screen navigation: Startup → Home → Size → Quantity → Layout (4x6) → Category → Frame → Payment → Capture → Review → Finalize → Printing → Thank You → Home
- Session state management for size, quantity, layout, and frame selection
- Even quantity enforcement (2, 4, 6, 8...)
- Inactivity handling: home/capture screens exempt; post-payment screens auto-advance; review timeout auto-fills remaining slots

### Startup & Configuration
- Startup checks with test mode toggle
- Camera error screen when camera fails (unless test mode enabled)
- Worker/server health check on startup
- Test mode support (simulated camera, cash reader, and card payment)
- Staff settings screen for configuring:
  - Pricing (per size and quantity pairs)
  - Timeouts (per screen)
  - Hardware settings (COM ports, printer names)
  - Payment providers (Stripe Terminal configuration)
  - Upload settings (base URL, enable/disable)
  - Booth ID configuration
  - Test mode toggle

### Asset Management
- Frame auto-discovery from folder structure (`Config/frames/`)
- Frame categories organized by folder names
- Template types: `2x6_4slots`, `4x6_2slots`, `4x6_4slots`, `4x6_6slots`
- Theme backgrounds per screen (first image in each folder used)
- Template editor with drag/resize slot placement + QR code positioning
- Per-frame layout overrides saved as `*.layout.json` next to frame PNGs
- Template geometry stored in `Config/templates.json`

### Photo Capture & Processing
- Canon SDK camera integration (with simulated fallback)
- Live view preview during capture
- Preview frame capture from live view (low frame rate snapshots)
- Video compilation: preview frames compiled into MP4 via ffmpeg
- Support for multiple image formats (PNG, JPEG)
- Session folder organization with photos, preview_frames, and videos subfolders

### Review & Composition
- Review screen with slot mapping for photo selection
- Composite preview generation
- Composite rendering with QR overlay
- Print-ready output (4x6 format, with 2x6 duplicated side-by-side)
- QR code generation using QRCoder library
- Print preview screen showing final composite

### Payment Integration
- Cash acceptor RS232 provider (TP Series bill acceptors)
  - Handshake protocol
  - Cash denomination tracking
  - Event logging (inserted, accepted, rejected, jam)
- Stripe Terminal card payment (worker-backed)
  - Connection token generation
  - Location and reader registration
  - Payment intent creation and processing
  - Manual capture support
  - Simulator buttons available in all modes (test mode)
- Payment gating: capture only starts after successful payment

### Upload & Sharing
- Upload service with multipart upload support
  - Initialize upload (gets upload URLs)
  - Upload image and video separately
  - Complete upload (creates manifest)
- Integration with Cloudflare Worker server
- Booth ID prefixing for session organization
- Upload enable/disable toggle
- QR code links to share page with download links
- Session metadata tracking (session ID, booth ID)

### Output & Storage
- Local session storage:
  - `Config/logs/YYYYMMDD/session_X/photos/` - Captured photos
  - `Config/logs/YYYYMMDD/session_X/preview_frames/` - Preview snapshots
  - `Config/logs/YYYYMMDD/session_X/videos/` - Compiled videos
  - `Config/logs/YYYYMMDD/session_X/session_X_final.png` - Final composite
- Print preview (physical printer integration removed - preview only)
- Video compilation from preview frames using ffmpeg (12 FPS)

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

Each `session_x` folder stores captured photos in `photos/`, low-frame-rate snapshots of live view in `preview_frames/` (captured during the session), and the final compiled video in `videos/` (stitched from preview frames using ffmpeg).


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

## Configuration

The app reads configuration from `Config/appconfig.ini`. Key settings:

**Pricing:**
- `PRICE{pairs}_{sizeCode}` format (e.g., `PRICE1_26=10` for 1 pair of 2x6 prints)
- Size codes: `26` (2x6), `46` (4x6)

**Hardware:**
- `CAMERA_PROVIDER` - `canon` or `simulated`
- `CARD_PROVIDER` - `stripe_terminal` or `simulated`
- `cash_COM` - COM port for cash acceptor (e.g., `COM4`)
- `CASH_DENOMS` - Comma-separated denominations (e.g., `5,10,20`)

**Stripe Terminal:**
- `STRIPE_TERMINAL_BASE_URL` - Worker server URL
- `STRIPE_TERMINAL_READER_ID` - Reader ID (blank to auto-register)
- `STRIPE_TERMINAL_LOCATION_ID` - Location ID

**Upload:**
- `UPLOAD_BASE_URL` - Server URL for uploads
- `UPLOAD_ENABLED` - `true` or `false`
- `BOOTH_ID` - Optional booth identifier (prefixed to session IDs)

**Timeouts:**
- `TIMEOUT_{SCREEN}` - Per-screen timeout in seconds
- `TIMEOUT_DEFAULT` - Default timeout fallback

**Other:**
- `TEST_MODE` - Enable test mode (simulated devices)
- `MAX_QUANTITY` - Maximum print quantity
- `CAMERA_TIMER_SECONDS` - Countdown timer for capture

## Notes

- Physical printer integration has been removed - only print preview is available
- Camera focus failures currently prevent a capture; it should still take the photo even if autofocus misses
- Upload service requires `UPLOAD_ENABLED=true` and `UPLOAD_BASE_URL` configured
- Video compilation requires ffmpeg installed (checks `C:\Program Files\ffmpeg\bin\ffmpeg.exe`, `third_party/ffmpeg*/bin/ffmpeg.exe`, or PATH)
- See `specifications.txt` for the full functional spec and acceptance criteria

## Remaining Work

### Payment Flow Enhancements
- Richer cash events: better handling of inserted/jam events
- Disable cash intake after payment completed
- Harden card provider error handling and retry logic

### Capture & Hardware
- Robust camera disconnect handling during capture
- Per-screen timer tuning and validation
- Camera autofocus: allow capture even if autofocus fails

### Physical Printer Integration
- Re-implement physical printer integration (currently preview only)
- Print queue management
- Print ticket support for different sizes

### Staff Tools
- Staff menu secret access (currently accessible from staff screen)
- Device diagnostics screen
- Log export functionality
- Session history viewer

### Configuration
- Config parsing for `config/app.json` and `config/pricing.json` (currently uses `appconfig.ini` only)

## Planned Integrations

### OpenCV Integration
- OpenCvSharp (via `OpenCvSharp4` / `OpenCvSharp4.Windows`) for richer template processing, compositing adjustments, and live video effects

### Server Integration Features

#### Booth Registration & Validation
- Server-side booth ID validation - software will not start without being in the allowed BOOTH_ID list (currently BOOTH_ID is configured but not validated)
- Automatic booth registration with the server on first launch
- Booth identity verification for all server communications

#### Remote Configuration Management
- Receive configuration updates from server without requiring manual restarts
- Dynamic reload of timeouts, pricing, and frame availability
- Real-time application of configuration changes

#### Health Monitoring & Reporting
- Automatic heartbeat/ping to server to track booth online/offline status
- Local device health tracking (CPU, memory, disk usage)
- Automatic incident logging for crashes, failed payments, and hardware issues
- Session health metrics reporting to server

#### Photo Management Enhancements
- Enhanced session metadata tracking (timestamp, size, layout, payment method) in uploads
- Per-booth organization improvements on server side
- Photo lifecycle management with retention policies

#### Audit Logging
- Comprehensive session activity logging with timestamps and user actions
- Transaction logging (payments, captures, prints) with server transmission
- Error and exception logging with context
- Automatic log transmission to server for centralized audit trail

#### Software Updates & Versioning
- Receive and apply software updates from server
- Staged update deployments with rollback capability
- Version verification on startup
- Automatic update notifications and scheduling