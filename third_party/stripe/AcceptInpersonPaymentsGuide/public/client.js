document.addEventListener("DOMContentLoaded", () => {
  document.getElementById("create-reader-button").disabled = true;
  document.getElementById("create-payment-intent-button").disabled = true;
  document.getElementById("process-payment-button").disabled = true;
  document.getElementById("simulate-payment-button").disabled = true;
  document.getElementById("capture-button").disabled = true;
  document.getElementById("view-dashboard-button").disabled = true;
});

function createLocation() {
  return fetch("/create_location", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      display_name: "HQ",
      address: {
        line1: "1272 Valencia Street",
        city: "San Francisco",
        state: "CA",
        country: "US",
        postal_code: "94110",
      },
    }),
  }).then((response) => {
    return response.json();
  });
}

function createReader() {
  return fetch("/register_reader", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ location_id: locationId }),
  }).then((response) => {
    return response.json();
  });
}

function createPaymentIntent(amount) {
  return fetch("/create_payment_intent", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ amount: amount }),
  }).then((response) => {
    return response.json();
  });
}

function processPayment() {
  return fetch("/process_payment", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      reader_id: readerId,
      payment_intent_id: paymentIntentId,
    }),
  }).then((response) => {
    return response.json();
  });
}

function simulatePayment() {
  return fetch("/simulate_payment", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      reader_id: readerId,
      card_number: document.getElementById("card-number-input").value,
    }),
  }).then((response) => {
    return response.json();
  });
}

function capture(paymentIntentId) {
  return fetch("/capture_payment_intent", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ payment_intent_id: paymentIntentId }),
  }).then((response) => {
    return response.json();
  });
}

var locationId;
var readerId;
var paymentIntentId;

const createLocationButton = document.getElementById("create-location-button");
createLocationButton.addEventListener("click", async (event) => {
  createLocationButton.className = "btn btn-light float-right loading";
  createLocationButton.disabled = true;

  const requestData = {
    display_name: "HQ",
    address: {
      line1: "1272 Valencia Street",
      city: "San Francisco",
      state: "CA",
      country: "US",
      postal_code: "94110",
    },
  };

  createLocation().then((location) => {
    createLocationButton.className = "btn btn-light float-right";
    logApiCall("POST /v1/terminal/locations", requestData, location);
    locationId = location["id"];

    document.getElementById("create-reader-button").disabled = false;
  });
});

const createReaderButton = document.getElementById("create-reader-button");
createReaderButton.addEventListener("click", async (event) => {
  createReaderButton.className = "btn btn-light float-right loading";
  createReaderButton.disabled = true;

  const requestData = { location_id: locationId };
  createReader().then((reader) => {
    createReaderButton.className = "btn btn-light float-right";
    logApiCall("POST /v1/terminal/readers", requestData, reader);
    readerId = reader["id"];

    document.getElementById("create-payment-intent-button").disabled = false;
  });
});

const createPaymentIntentButton = document.getElementById("create-payment-intent-button");
createPaymentIntentButton.addEventListener("click", async (event) => {
  createPaymentIntentButton.className = "btn btn-light float-right loading";
  createPaymentIntentButton.disabled = true;

  amount = document.getElementById("amount-input").value;
  const paymentIntentRequestData = { amount: amount };
  createPaymentIntent(amount)
    .then((paymentIntent) => {
      createPaymentIntentButton.className = "btn btn-light float-right";
      logApiCall(
        "POST /v1/payment_intents",
        paymentIntentRequestData,
        paymentIntent
      );
      paymentIntentId = paymentIntent["id"];

      document.getElementById("process-payment-button").disabled = false;
    })
    .catch((error) => {
      createPaymentIntentButton.className = "btn btn-light float-right";
      createPaymentIntentButton.disabled = false;
      console.error("Error creating payment intent:", error);
    });
});

const processPaymentButton = document.getElementById("process-payment-button");
processPaymentButton.addEventListener("click", async (event) => {
  processPaymentButton.className = "btn btn-light float-right loading";
  processPaymentButton.disabled = true;

  const processPaymentRequestData = {
    reader_id: readerId,
    payment_intent_id: paymentIntentId,
  };
  processPayment()
    .then((reader) => {
      processPaymentButton.className = "btn btn-light float-right";
      logApiCall(
        "POST /v1/terminal/readers/" + readerId + "/process_payment_intent",
        processPaymentRequestData,
        reader
      );
      document.getElementById("simulate-payment-button").disabled = false;
    })
    .catch((error) => {
      processPaymentButton.className = "btn btn-light float-right";
      processPaymentButton.disabled = false;
      console.error("Error processing payment:", error);
    });
});

const simulatePaymentButton = document.getElementById(
  "simulate-payment-button"
);
simulatePaymentButton.addEventListener("click", async (event) => {
  simulatePaymentButton.className = "btn btn-light float-right loading";
  simulatePaymentButton.disabled = true;

  const simulatePaymentRequestData = {
    reader_id: readerId,
    card_number: document.getElementById("card-number-input").value,
  };
  simulatePayment().then((reader) => {
    simulatePaymentButton.className = "btn btn-light float-right";
    logApiCall(
      "POST /v1/test_helpers/terminal/readers/" +
        readerId +
        "/present_payment_method",
      simulatePaymentRequestData,
      reader
    );

    // Check if the action status is failed
    if (reader.action && reader.action.status === "failed") {
      // Skip capture button and enable view dashboard button directly
      document.getElementById("view-dashboard-button").disabled = false;
    } else {
      // Normal flow - enable capture button
      document.getElementById("capture-button").disabled = false;
    }
  });
});

const captureButton = document.getElementById("capture-button");
captureButton.addEventListener("click", async (event) => {
  captureButton.className = "btn btn-light float-right loading";
  captureButton.disabled = true;

  const captureRequestData = { payment_intent_id: paymentIntentId };
  capture(paymentIntentId).then((paymentIntent) => {
    captureButton.className = "btn btn-light float-right";
    logApiCall(
      "POST /v1/payment_intents/" + paymentIntentId + "/capture",
      captureRequestData,
      paymentIntent
    );
    document.getElementById("view-dashboard-button").disabled = false;
  });
});

function logApiCall(method, requestData, responseData) {
  var logs = document.getElementById("logs");

  // Create main container for this API call
  var apiCallContainer = document.createElement("div");
  apiCallContainer.classList.add("api-call");

  // Create title with method and status
  var title = document.createElement("div");
  title.classList.add("api-title");

  var methodSpan = document.createElement("span");
  methodSpan.classList.add("api-method");
  methodSpan.textContent = method.split(" ")[0]; // GET/POST etc

  var urlSpan = document.createElement("span");
  urlSpan.classList.add("api-url");
  urlSpan.textContent = " " + method.split(" ").slice(1).join(" ");

  title.appendChild(methodSpan);
  title.appendChild(urlSpan);

  // Create request section
  var requestSection = document.createElement("div");
  requestSection.classList.add("api-section");

  var requestTitle = document.createElement("div");
  requestTitle.classList.add("section-title");
  requestTitle.textContent = "Request body";

  var requestContent = document.createElement("div");
  requestContent.classList.add("section-content");
  var requestPre = document.createElement("pre");
  var requestCode = document.createElement("code");

  if (requestData) {
    requestCode.textContent = formatJson(
      JSON.stringify(requestData, undefined, 2)
    );
  } else {
    requestCode.textContent = "No request body";
  }

  requestPre.appendChild(requestCode);
  requestContent.appendChild(requestPre);
  requestSection.appendChild(requestTitle);
  requestSection.appendChild(requestContent);

  // Create response section
  var responseSection = document.createElement("div");
  responseSection.classList.add("api-section");

  var responseTitle = document.createElement("div");
  responseTitle.classList.add("section-title");
  responseTitle.textContent = "Response body";

  var responseContent = document.createElement("div");
  responseContent.classList.add("section-content");
  var responsePre = document.createElement("pre");
  var responseCode = document.createElement("code");
  responseCode.textContent = formatJson(
    JSON.stringify(responseData, undefined, 2)
  );

  responsePre.appendChild(responseCode);
  responseContent.appendChild(responsePre);
  responseSection.appendChild(responseTitle);
  responseSection.appendChild(responseContent);

  // Assemble the complete API call log
  apiCallContainer.appendChild(title);
  apiCallContainer.appendChild(requestSection);
  apiCallContainer.appendChild(responseSection);

  logs.appendChild(apiCallContainer);

  // Auto-scroll to the new log entry
  apiCallContainer.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function formatJson(message) {
  var lines = message.split("\n");
  var json = "";
  var lineNumberFixedWidth = stringLengthOfInt(lines.length);
  for (var i = 1; i <= lines.length; i += 1) {
    line = i + padSpaces(i, lineNumberFixedWidth) + lines[i - 1];
    json = json + line + "\n";
  }
  return json;
}

function stringLengthOfInt(number) {
  return number.toString().length;
}

function padSpaces(lineNumber, fixedWidth) {
  // Always indent by 2 and then maybe more, based on the width of the line
  // number.
  return " ".repeat(2 + fixedWidth - stringLengthOfInt(lineNumber));
}

// Update the displayed amount in dollars when the cents input changes
const amountInput = document.getElementById("amount-input");
amountInput.addEventListener("input", (event) => {
  const cents = parseInt(event.target.value, 10);
  const dollars = (cents / 100).toFixed(2);
  document.querySelector(".amount-display").textContent = `$${dollars}`;
});

// Initialize the displayed amount
document.querySelector(".amount-display").textContent = "$20.00";

// Handle View in Dashboard button
const viewDashboardButton = document.getElementById("view-dashboard-button");
viewDashboardButton.addEventListener("click", async (event) => {
  if (paymentIntentId) {
    const dashboardUrl = `https://dashboard.stripe.com/payments/${paymentIntentId}`;
    window.open(dashboardUrl, "_blank");
  } else {
    alert(
      "No payment intent found. Please complete the payment process first."
    );
  }
});