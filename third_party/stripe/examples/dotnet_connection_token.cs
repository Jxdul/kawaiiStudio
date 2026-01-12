// Minimal C# example using HttpClient to request a Stripe Terminal connection token
// Set environment variable STRIPE_SECRET_KEY to your secret key (sk_test_...)
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        var secret = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        if (string.IsNullOrEmpty(secret)) { Console.WriteLine("Set STRIPE_SECRET_KEY"); return; }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var resp = await client.PostAsync("https://api.stripe.com/v1/terminal/connection_tokens", null);
        var body = await resp.Content.ReadAsStringAsync();
        Console.WriteLine(body);
    }
}
