# Stripe Terminal Integration Guide for KawaiiStudio

## 🎯 Goal
Add in-person card payment support to the photobooth using Stripe Terminal readers while keeping a simple, reliable flow for customers and reconciliation with your backend.

---

## 🔧 Recommended flow (high-level)
1. Backend creates a Connection Token (short-lived) using your Stripe secret key.
2. App (photobooth) requests a Connection Token from your backend to connect to a reader.
3. App uses a Terminal SDK on a supported platform to discover and connect to a reader (simulator for testing).
4. Backend creates a PaymentIntent (or PaymentIntent + capture flow) for each sale.
5. App uses the reader to collect card and process the payment using the PaymentIntent.
6. Backend confirms/captures payment and records transaction details for reconciliation.

---

## 🧪 Testing first
- Use the **simulated reader** during development — it avoids hardware delays and lets you test the flow end-to-end.
- Use test Stripe API keys (prefixed `sk_test_...`) in all development servers.

---

## ✅ Backend responsibilities (minimum)
- Expose POST `/connection_token` that returns the result of POST to `https://api.stripe.com/v1/terminal/connection_tokens` (authenticated with your Stripe Secret Key).
- Expose endpoints to create a PaymentIntent, record order metadata, and reconcile captures.
- Keep the secret API key on the server only — never embed in the client.

---

## 📌 App (client) options for a Windows/WPF photobooth
- Option A – Embed a webview (Chromium / WebView2) and use the **Stripe Terminal JavaScript** SDK inside it.
  - Works well if the embedded web view can access USB/Bluetooth or if you use a network-based reader.
- Option B – Use a companion mobile device (iOS/Android) running a small app (using Stripe Terminal iOS/Android SDK) that handles the reader and communicates with your WPF app (local TCP, WebSocket, or REST).
- Option C – Build an Electron wrapper around a web-based UI and use Terminal JS in Electron.

Pick the option that fits your hardware (Bluetooth vs. network reader) and your deployment constraints.

---

## 🛠 Example endpoints & snippets
### Node (Express) — `POST /connection_token`
```js
// examples/node_connection_token.js
const express = require('express');
const Stripe = require('stripe');
const app = express();
const stripe = Stripe(process.env.STRIPE_SECRET_KEY);

app.post('/connection_token', async (req, res) => {
  const token = await stripe.terminal.connectionTokens.create();
  res.json(token);
});

app.listen(3000);
```

### Simple curl example
```bash
curl -u sk_test_...: \
  -X POST "https://api.stripe.com/v1/terminal/connection_tokens"
```

### Minimal C# example (HttpClient)
```csharp
// examples/dotnet_connection_token.cs
using System.Net.Http.Headers;

var secret = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

var resp = await client.PostAsync("https://api.stripe.com/v1/terminal/connection_tokens", null);
var body = await resp.Content.ReadAsStringAsync();
Console.WriteLine(body);
```

---

## ⚠️ Important notes
- Use `capture_method: 'manual'` if you want to create a PaymentIntent and capture it after other checks (tip handling, refunds, etc.).
- Ensure your `Location` and reader are created/registered in the Stripe Dashboard if using physical readers.
- Keep logs for successful/failed payments and store `payment_intent` IDs for reconciliation and support.

---

## 📚 References
- Stripe Terminal docs: https://stripe.com/docs/terminal
- Example backends and apps: https://github.com/stripe

---

If you'd like, I can:
- Add an end-to-end example server in the repo matching your preferred backend (.NET or Node).
- Create a small sample WebView-based client example you can embed in your WPF app.

Tell me which you'd prefer and I’ll scaffold it. ✅