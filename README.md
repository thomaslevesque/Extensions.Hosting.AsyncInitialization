# Extensions.Hosting.AsyncInitialization

[![NuGet version](https://img.shields.io/nuget/v/Extensions.Hosting.AsyncInitialization.svg?logo=nuget)](https://www.nuget.org/packages/Extensions.Hosting.AsyncInitialization)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/thomaslevesque/Extensions.Hosting.AsyncInitialization/build.yml?branch=master&logo=github)](https://github.com/thomaslevesque/Extensions.Hosting.AsyncInitialization/actions/workflows/build.yml)

A simple helper to perform async application initialization and teardown for the generic host in .NET 6.0 or higher (e.g. in ASP.NET Core apps).

## Basic usage

1. Install the [Extensions.Hosting.AsyncInitialization](https://www.nuget.org/packages/Extensions.Hosting.AsyncInitialization/) NuGet package:

    Command line:
   
    ```PowerShell
    dotnet add package Extensions.Hosting.AsyncInitialization
    ```

    Package manager console:
    ```PowerShell
    Install-Package Extensions.Hosting.AsyncInitialization
    ```


2. Create a class (or several) that implements `IAsyncInitializer`. This class can depend on any registered service.

    ```csharp
    public class MyAppInitializer : IAsyncInitializer
    {
        public MyAppInitializer(IFoo foo, IBar bar)
        {
            ...
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            // Initialization code here
        }
    }
    ```

3. Register your initializer(s) in the same place as other services:

    ```csharp
    services.AddAsyncInitializer<MyAppInitializer>();
    ```

4. In the `Program` class, replace the call to `host.RunAsync()` with `host.InitAndRunAsync()`:

```csharp
public static async Task Main(string[] args)
{
    var host = CreateHostBuilder(args).Build();
    await host.InitAndRunAsync();
}
```

This will run each initializer, in the order in which they were registered.

## Teardown

In addition to initialization, this library also supports performing cleanup tasks when the app terminates. To use this, make your initializer implement `IAsyncTeardown`, and implement the `TeardownAsync` method:


```csharp
public class MyAppInitializer : IAsyncTeardown
{
    public MyAppInitializer(IFoo foo, IBar bar)
    {
        ...
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Initialization code here
    }

    public async Task TeardownAsync(CancellationToken cancellationToken)
    {
        // Cleanup code here
    }
}
```

When you run the application with `InitAndRunAsync`, each initializer that supports teardown will be invoked in reverse registration order, i.e.:

- initializer 1 performs initialization
- initializer 2 performs initialization
- application runs
- initializer 2 performs teardown
- initializer 1 performs teardown

## Lifetime and state considerations

When you create an initializer that also performs teardown, keep in mind that it will actually be resolved twice:
- once for initialization
- once for teardown

Initialization and teardown each runs in its own service provider scope. This means that a different instance of your initializer will be used for initialization and teardown, so your initializer cannot keep state between initialization and teardown, unless it's registered as a singleton.

If you do register your initializer as a singleton, keep in mind that it must not depend on any scoped service, otherwise the scoped service will live for the whole lifetime of the application; this is an anti-pattern known as [captive dependency](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#captive-dependency).

## Advanced usage

If, for some reason, you need more control over the application execution process, you can manually call the `InitAsync` and `TeardownAsync` methods on the host. If you do that, keep in mind that `TeardownAsync` cannot be called after `host.RunAsync()` completes, because the host will already have been disposed:

```csharp
// DO NOT DO THIS
await host.InitAsync();
await host.RunAsync();
await host.TeardownAsync(); // Will fail because RunAsync disposed the host
```

So you will need to manually call `StartAsync` and `WaitForShutdownAsync`, and call `TeardownAsync` _before_ you dispose the host:

```csharp
await using (var host = CreateHostBuilder(args).Build())
{
    await host.InitAsync();
    await host.StartAsync();
    await host.WaitForShutdownAsync();
    await host.TeardownAsync();
}
```

## Cancellation

The `InitAndRunAsync`, `InitAsync` and `TeardownAsync` all support passing a cancellation token to abort execution if needed. Cancellation will be propagated to the initializers.

In the following example, execution (including initialization, but not teardown) will be aborted when `Ctrl + C` keys are pressed :
```csharp
public static async Task Main(string[] args)
{
    using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    // The following line will hook `Ctrl` + `C` to the cancellation token.
    Console.CancelKeyPress += (source, args) => cancellationTokenSource.Cancel();

    var host = CreateHostBuilder(args).Build();
    await host.InitAndRunAsync(cancellationTokenSource.Token);
}
```

As mentioned above, when using `InitAndRunAsync`, the cancellation token will *not* be passed to the teardown. This is because when your application is stopped, you typically still want the teardown to occur. Instead, teardown will run with a default timeout of 10 seconds (you can also specify a timeout explicitly).

If you don't want this behavior, you can manually call `InitAsync` and `TeardownAsync` as explained in the previous section.

### Migration from 2.x or earlier

If you were already using this library prior to version 3.x, your code would typically look like this:

```csharp
await host.InitAsync();
await host.RunAsync();
```

This will still work without changes. Just keep in mind that, as explained in the previous section, adding a call to `host.TeardownAsync()` after `host.RunAsync()` *will not work*. If you need teardown, the simplest way is to remove the explicit call to `InitAsync`, and call `InitAndRunAsync` instead of `RunAsync`.
