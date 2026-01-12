// Minimal Node example: run with `npm install express stripe` and set STRIPE_SECRET_KEY
const express = require('express');
const Stripe = require('stripe');
const app = express();
const stripe = Stripe(process.env.STRIPE_SECRET_KEY);

app.post('/connection_token', async (req, res) => {
  try {
    const token = await stripe.terminal.connectionTokens.create();
    res.json(token);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: err.message });
  }
});

app.listen(3000, () => console.log('Connection token server running on :3000'));
