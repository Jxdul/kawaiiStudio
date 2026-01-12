# Accept in-person payments

Set up the Stripe Terminal SDK so you can begin accepting in-person payments. Included are some basic build and run scripts you can use to start up the application.

## Running the sample

1. Build the server

~~~
dotnet restore
~~~

2. Run the server

~~~
dotnet run --project StripeExample.csproj
~~~

3. Go to [http://localhost:4242](http://localhost:4242)

Note: running `dotnet run` with `Server.cs` directly will fail because it skips the project references.
