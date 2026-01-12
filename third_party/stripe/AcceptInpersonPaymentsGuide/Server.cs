using System;
using System.Collections.Generic;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Stripe;
using Stripe.Terminal;

namespace StripeExample
{
  public class Program
  {
    public static void Main(string[] args)
    {
      WebHost.CreateDefaultBuilder(args)
        .UseUrls("http://0.0.0.0:4242")
        .UseWebRoot("public")
        .UseStartup<Startup>()
        .Build()
        .Run();
    }

  }

  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddMvc().AddNewtonsoftJson();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      // This is your test secret API key.
      StripeConfiguration.ApiKey = "sk_test_51SnxK7RaU3Tiq93j4r1w5uF2bYYUAatAzyeULVZ9SMBn3nmf9OCW6zy5MM8iHVVHSj3iohY7kPQwIpgSe6SM4Xy500rqBgx6kD";

      if (env.IsDevelopment()) app.UseDeveloperExceptionPage();
      app.UseRouting();
      app.UseDefaultFiles();
      app.UseStaticFiles();
      app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
  }


  [Route("create_location")]
  [ApiController]
  public class CreateLocationApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(CreateLocationRequest request)
    {
      var options = new LocationCreateOptions
      {
        DisplayName = request.DisplayName,
        Address = new AddressOptions
        {
          Line1 = request.Address.Line1,
          City = request.Address.City,
          State = request.Address.State,
          Country = request.Address.Country,
          PostalCode = request.Address.PostalCode,
        },
      };
      var service = new LocationService();
      var location = service.Create(options);
      return Json(location);
    }
  }

  public class CreateLocationRequest
  {
    [JsonProperty("display_name")]
    public string DisplayName { get; set; }

    [JsonProperty("address")]
    public CreateLocationAddress Address { get; set; }
  }

  public class CreateLocationAddress
  {
    [JsonProperty("line1")]
    public string Line1 { get; set; }

    [JsonProperty("city")]
    public string City { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("postal_code")]
    public string PostalCode { get; set; }
  }

  [Route("register_reader")]
  [ApiController]
  public class RegisterReaderApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(RegisterReaderRequest request)
    {
      var options = new ReaderCreateOptions
      {
        RegistrationCode = "simulated-s700",
        Location = request.LocationId,
        Label = "Quickstart - S700 Simulated Reader",
      };
      var service = new ReaderService();
      var reader = service.Create(options);
      return Json(reader);
    }
  }

  public class RegisterReaderRequest
  {
    [JsonProperty("location_id")]
    public string LocationId { get; set; }
  }

  [Route("create_payment_intent")]
  [ApiController]
  public class PaymentIntentApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(PaymentIntentCreateRequest request)
    {
      var service = new PaymentIntentService();

      // For Terminal payments, the 'payment_method_types' parameter must include
      // 'card_present'.
      // To automatically capture funds when a charge is authorized,
      // set `capture_method` to `automatic`.
      var options = new PaymentIntentCreateOptions
      {
          Amount = long.Parse(request.Amount),
          Currency = "cad",
          PaymentMethodTypes = new List<string>
          {
            "card_present",
            "interac_present",
          },
          CaptureMethod = "automatic",
          PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
          {
              CardPresent = new PaymentIntentPaymentMethodOptionsCardPresentOptions
              {
                  CaptureMethod = "manual_preferred"
              }
          }
      };
      var intent = service.Create(options);

      return Json(intent);
    }

    public class PaymentIntentCreateRequest
    {
      [JsonProperty("amount")]
      public string Amount { get; set; }
    }
  }

  [Route("process_payment")]
  [ApiController]
  public class ProcessPaymentApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(ProcessPaymentRequest request)
    {
      var service = new ReaderService();
      var options = new ReaderProcessPaymentIntentOptions
      {
        PaymentIntent = request.PaymentIntentId,
      };

      var attempt = 0;
      var tries = 3;
      while (true)
      {
        attempt++;
        try
        {
          var reader = service.ProcessPaymentIntent(request.ReaderId, options);
          return Json(reader);
        }
        catch (StripeException e)
        {
          switch (e.StripeError.Code)
          {
            case "terminal_reader_timeout":
              // Temporary networking blip, automatically retry a few times.
              if (attempt == tries)
              {
                return Json(e.StripeError);
              }
              break;
            case "terminal_reader_offline":
              // Reader is offline and won't respond to API requests. Make sure the reader is powered on
              // and connected to the internet before retrying.
              return Json(e.StripeError);
            case "terminal_reader_busy":
              // Reader is currently busy processing another request, installing updates or changing settings.
              // Remember to disable the pay button in your point-of-sale application while waiting for a
              // reader to respond to an API request.
              return Json(e.StripeError);
            case "intent_invalid_state":
              // Check PaymentIntent status because it's not ready to be processed. It might have been already
              // successfully processed or canceled.
              var paymentIntentService = new PaymentIntentService();
              var paymentIntent = paymentIntentService.Get(request.PaymentIntentId);
              Console.WriteLine($"PaymentIntent is already in {paymentIntent.Status} state.");
              return Json(e.StripeError);
            default:
              return Json(e.StripeError);
          }
        }
      }
    }

    public class ProcessPaymentRequest
    {
      [JsonProperty("reader_id")]
      public string ReaderId { get; set; }

      [JsonProperty("payment_intent_id")]
      public string PaymentIntentId { get; set; }
    }
  }

  [Route("simulate_payment")]
  [ApiController]
  public class SimulatePaymentApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(SimulatePaymentRequest request)
    {
      var service = new Stripe.TestHelpers.Terminal.ReaderService();

      var parameters = new Stripe.TestHelpers.Terminal.ReaderPresentPaymentMethodOptions
      {
          CardPresent = new Stripe.TestHelpers.Terminal.ReaderCardPresentOptions
          {
              Number = request.CardNumber
          },
          Type = "card_present"
      };

      var reader = service.PresentPaymentMethod(request.ReaderId, parameters);
      return Json(reader);
    }

    public class SimulatePaymentRequest
    {
      [JsonProperty("reader_id")]
      public string ReaderId { get; set; }

      [JsonProperty("card_number")]
      public string CardNumber { get; set; }
    }
  }

  [Route("capture_payment_intent")]
  [ApiController]
  public class CapturePaymentIntentApiController : Controller
  {
    [HttpPost]
    public ActionResult Post(PaymentIntentCaptureRequest request)
    {
      var service = new PaymentIntentService();
      var intent = service.Capture(request.PaymentIntentId, null);
      return Json(intent);
    }

    public class PaymentIntentCaptureRequest
    {
      [JsonProperty("payment_intent_id")]
      public string PaymentIntentId { get; set; }
    }
  }
}