# Simmy sample app

This repo presents an intentionally simple example .NET Core WebAPI app demonstrating Simmy.

The app demonstrates the following patterns with Simmy:

+ Configuring `StartUp` so that Simmy chaos policies are only introduced in builds for certain environments (for instance, Dev but not Prod)
+ Configuring Simmy chaos policies to be injected into the app without changing any existing configuration code
+ Injecting faults or chaos by modifying external configuration.

The patterns shown in this sample app are not mandatory.  They are intended to demonstrate approaches you could take when introducing Simmy to an app, but Simmy is very flexible, and comments in this article describe how you could also take Simmy further.

## The sample app: a simple monitoring service

The example app is an intentionally simplified endpoint monitoring service, reporting on the health of endpoints configured in the `MonitoringEndpoints` section of `appsettings.json`.

The app offers two public endpoints: 

+ `monitoring/status` returns the health of each monitored url, just as the HttpStatusCode
+ `monitoring/responsetime` returns the ResponseTime of each monitored url, in ms.

(The use of two separate endpoints here just helps us demonstrate injecting faults into one downstream operation but not another.)

The simple metrics returned - status code and response time - allow us to easily see the results of introducing faults in to our calls. ;~)

## Run the app without fault injection

Run the app without fault injection. If in Visual Studio, starting the app should open a web page calling `/monitoring/status`.  If it doesn't, navigate to that endpoint manually. You should receive results something like this:

```
[
    {
        "url": "www.bbc.co.uk",
        "value": 200,
    },
    {
        "url": "www.google.co.uk",
        "value": 200 
    }
]
```

## Injecting faults or chaos

Open the file `chaossettings.json` in the root folder of the app.  The file can be configured with fault-injection settings for any number of call sites within your app.  

`appsettings.json` is a json array, looking something like this:

    "ChaosSettings": {
        "OperationChaosSettings": [
        {
            "OperationKey": "Status",
            "Enabled": true,
            "InjectionRate": 0.75,
            "LatencyMs": 0,
            "StatusCode": 503,
        },
        {
            "OperationKey": "ResponseTime",
            "Enabled": false,
            "InjectionRate": 0.1,
            "LatencyMs": 2000,
            "Exception": "System.SocketException"
        }
      ]
    } 

The elements are:

#### OperationKey 

Which operation within your app these chaos settings apply to.  Each call site in your codebase which uses Polly and Simmy can be tagged with an `OperationKey`:

    Context context = new Context("FooOperationKey");


This is simply a string tag you choose, to identify different call paths in your app.  Steps to attach this further to your http call are shown in the sample app.

#### Enabled

A master switch for this call site. When `true`, faults may be injected at this call site per the other parameters; when `false`, no faults will be injected.

#### InjectionRate

A `double` between 0 and 1, indicating what proportion of calls should be subject to failure-injection.  For example, if `0.2`, twenty percent of calls will be randomly affected; if `0.01`, one percent of calls; if `1`, all calls.

#### Latency

If set, this much extra latency in ms will be added to affected calls, before the http request is made.  

#### StatusCode

If set, a result with the given http status code will be returned for affected calls.  (The original outbound http call will not be placed.)

#### Exception

If set, affected calls will throw the given exception.  (The original outbound http call will not be placed.)

### Live update during running

The sample app is constructed using `IOptionsSnapshot<>` so that adjusting the settings immediately affects subsequent calls.  

### Complete example: Inject a different status code 

    "ChaosSettings": {
        "OperationChaosSettings": [
        {
            "OperationKey": "Status",
            "Enabled": true,
            "InjectionRate": 1,
            "StatusCode": 503,
        }
      ]
    } 

#### Expected result (/monitoring/status)

    {"results":[{"url":"http://www.google.co.uk/","value":503},{"url":"http://www.bbc.co.uk/","value":503}]}

> _Note:_ During startup the sample app configures a limited resilience policy which retries typical failure status codes a couple of times.  Therefore, do not be surprised if you configure a 50% injection rate (`"InjectionRate": 0.5`) for a 503 code but see 503s actually surfacing less frequently in the demo - the resilience policy will be handling _some_ of them.

### Complete example: Inject latency 

    "ChaosSettings": {
        "OperationChaosSettings": [
        {
            "OperationKey": "ResponseTime",
            "Enabled": true,
            "InjectionRate": 1,
            "LatencyMs": 2000,
        }
      ]
    } 

#### Expected result (/monitoring/responsetime)

    {"results":[{"url":"http://www.google.co.uk/","value":2262},{"url":"http://www.bbc.co.uk/","value":2526}]}

### Complete example: Inject OperationCanceledException 

    "ChaosSettings": {
        "OperationChaosSettings": [
        {
            "OperationKey": "Status",
            "Enabled": true,
            "InjectionRate": 1,
            "Exception": "System.OperationCanceledException"
        }
      ]
    } 

#### Expected result (/monitoring/status)

    An unhandled exception occurred while processing the request.
    OperationCanceledException: The operation was canceled.

## How the sample app injects the chaos

Calls guarded by Polly policies often wrap a series of policies around a call using `PolicyWrap`.  The policies in the PolicyWrap act as nesting middleware around the outbound call.

The recommended technique for introducing `Simmy` is to use one or more Simmy chaos policies as the _innermost_ policies in a `PolicyWrap`.

By placing the chaos policies innermost, they subvert the usual outbound call at the last minute, substituting their fault or adding extra latency.

The existing Polly policies - further out in the PolicyWrap - still apply, so you can test how the Polly resilience you have configured handles the chaos/faults injected by Simmy.

## Experimenting with adjusting resilience policies to handle injected faults (a simple example)

The sample app is intentionally undefended from exceptions, so that you can see the exceptions surface in the examples above.

Now you can experiment with changing the resilience in your app to handle the faults that occur.  

First, in `appsettings.json`, change the injection rate for `OperationCanceledException` to inject faults 50% of the time: `"InjectionRate": 0.5`.  

Run the endpoint and you should see many calls fail with `OperationCanceledException`.

Now, in the sample app, in the method `GetResiliencePolicy()`, change the retry policy so that it also handles `OperationCanceledException`, retrying a number of times:

    var retry = HttpPolicyExtensions.HandleTransientHttpError()
        .Or<OperationCanceledException>()
        .RetryAsync(3);

Running the endpoint with the extra configured resilience should significantly reduce the number of `OperationCanceledException` which actually surface to the caller as errors.

This is an intentionally simplistic example to demonstrate iterating a feedback loop from experimenting with faults to adjusting policies. Of course, you can run far more sophisticated chaos experiments on a real app: introducing 100ms latency to all database calls briefly, and see if your retry/circuit-breaker policies are configured to give a good customer experience in those circumstances; block all calls to a recommendations subsystem - whatever.

## Adding Simmy chaos without changing existing configuration code

As mentioned above, the usual technique to add chaos-injection is to configure Simmy policies innermost in your app's `PolicyWrap`s.

One of the simplest ways to do this all across your app is to make all policies used in your app be stored in and drawn from `PolicyRegistry`.  This is the technique demonstrated in this sample app.

In `StartUp`, all the Polly policies which will be used are configured, and registered in `PolicyRegistry`:

    var policyRegistry = services.AddPolicyRegistry();
    policyRegistry["ResiliencePolicy"] = GetResiliencePolicy();

Typed-clients are configured on `HttpClientFactory`, which will use policies from `PolicyRegistry`:

    services.AddHttpClient<ResilientHttpClient>()
        .AddPolicyHandlerFromRegistry("ResiliencePolicy");


> (_When using Polly and Simmy without HttpClientFactory_, simply pass the `PolicyRegistry` by DI into the components making outbound calls, and pull the appropriate policy out of `PolicyRegistry` at the call site.)

If you have taken the above `PolicyRegistry`-driven approach, the sample app demonstrates a very simple technique that can be used to add Simmy throughout your app, .  The `AddChaosInjectors()` extension method on `IPolicyRegistry<>` simply takes every policy in your `PolicyRegistry` and wraps Simmy policies (as the innermost policy) inside.  

    // Only add Simmy chaos injection in development-environment runs 
    // (ie prevent chaos-injection ever reaching staging or prod - if that is what you want).
    if (env.IsDevelopment())
    {
        // Wrap every policy in the policy registry in Simmy chaos injectors.
        var registry = app.ApplicationServices.GetRequiredService<IPolicyRegistry<string>>();
        registry?.AddChaosInjectors();
    }


This allows you to inject Simmy into your app without changing any of your existing app configuration of Polly policies.

This extension method configures the policies in your PolicyRegistry with Simmy policies which react to chaos configured by `chaossettings.json`.

The code lines above also demonstrate a construct to ensure that fault-injection is only included in builds for certain environments: for example if you want to inject chaos into stage environments but not prod.

## Using other sources to control chaos settings

The use of `chaossettings.json` here as the source to control fault/chaos
injection is just one technique. 

+ Any other config source can equally be used to populate an `IOptions<>`;
+ An http endpoint (suitably secured!) could be used to set chaos settings.

## Filtering how and what chaos is applied using constructs particular to your app

The fault-injection policies configured by `InjectBehaviour(...)`, `InjectLatency(...)` and `InjectFault(...)` can all be configured with `Func<>`s which take `Polly.Context` as an input parameter.  And  `Polly.Context` can carry any arbitrary data, using `Dictionary<string, object>` semantics.

You can therefore build policies to control chaos based on _any_ custom data particular to your app.  

For example, it may be that the urls of downstream systems in your app follow certain patterns, and you filter on the url to introduce chaos to only certain subsystems, or only certain primaries/failovers.

Or you might choose to whitelist or blacklist certain callers, so that chaos is only introduced for your test callers but not for your live customers.

Every parameter of the chaos policy exists in a form taking a `Func<Context, ...>` for configuration, so all dimensions of the chaos policy - whether it is enabled, what proportion of calls should be affected, and what chaos should be injected - can be inflected by data set on the `Context` passed to execution.

## Going beyond http calls

The sample app here demonstrates Simmy policies configured into `HttpClient` instances provided by `HttpClientFactory`.  The chaos therefore governs outbound http calls.

Again, this is just an example.  Polly and Simmy policies are not tied to http calls.  All Polly and Simmy policies exist in generic `<TResult>` forms, and can be used around any type of call, including: 

+ calls to SDKs for your storage, be that via Entity Framework, MongoDB, whatever
+ calls to any part a cloud SDK (Azure, AWS, GCP).
