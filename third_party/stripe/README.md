# Stripe docs for KawaiiStudio

This folder contains documentation and example snippets for integrating Stripe (Terminal and in-person payments) with the KawaiiStudio photobooth app.

Files included:

- `TERMINAL_INTEGRATION.md` — step-by-step guidance for integrating Stripe Terminal with your app (including options for desktop WPF apps).
- `examples/node_connection_token.js` — minimal Node/Express example that returns a Terminal connection token.
- `examples/dotnet_connection_token.cs` — minimal C# example showing how to create a connection token with HttpClient.
- `collectCardPaymentsDocumentation.txt` — existing legacy notes.
- `TestStripeTerminal.txt` — existing notes for Terminal testing.
- `AcceptInpersonPaymentsGuide/` — existing guide files.

Quick start:

1. Read `TERMINAL_INTEGRATION.md` for the recommended integration flow and checklist.
2. Use the `examples` code as a starting point for the backend endpoint that creates connection tokens and PaymentIntents.
3. Test with the simulated reader first, then move to a physical reader.

Helpful links:

- Stripe Terminal docs: https://stripe.com/docs/terminal
- Terminal Quickstart: https://stripe.com/docs/terminal/quickstart
