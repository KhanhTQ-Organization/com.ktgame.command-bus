# com.ktgame.command-bus

Command Bus for Unity on top of UniTask. Each command type has exactly one handler, and middlewares can wrap every command (logging, analytics, validation...).

- No reflection at execution time (IL2CPP safe).
- Designed for the Unity main thread (not thread-safe).

## Define commands and handlers

```csharp
using com.ktgame.command_bus;

// Command without result
public class SaveGame : ICommand { }

public class SaveGameHandler : ICommandHandler<SaveGame>
{
    public UniTask Execute(SaveGame command, CancellationToken cancellationToken = default)
    {
        // ...
        return UniTask.CompletedTask;
    }
}

// Command with result
public class BuyItem : ICommand<bool>
{
    public string ItemId;
}

public class BuyItemHandler : ICommandHandler<BuyItem, bool>
{
    public async UniTask<bool> Execute(BuyItem command, CancellationToken cancellationToken = default)
    {
        // ...
        return true;
    }
}
```

## Register

```csharp
ICommandBus bus = new CommandBus();

// Register returns an IDisposable: dispose it to unregister (e.g. in OnDestroy).
IDisposable registration = bus.Register(new BuyItemHandler());
registration.Dispose();

// Registering a second handler for the same command throws, unless you opt in:
bus.Register(new BuyItemHandler(), overwrite: true);

// Other ways to unregister
bus.UnRegister(handlerInstance);      // this instance
bus.UnRegister<BuyItemHandler>();     // every handler of this type (incl. subclasses)
bus.UnRegisterCommand<BuyItem>();     // the handler of this command
bus.Clear();                          // all handlers
```

## Execute

```csharp
// Command with result: the result type is inferred
bool ok = await bus.Send(new BuyItem { ItemId = "sword" });

// Command without result
await bus.Execute(new SaveGame());

// Old API (still supported)
bool ok2 = await bus.Execute<BuyItem, bool>(new BuyItem { ItemId = "sword" });

// Optional command: returns false instead of throwing when no handler is registered
bool handled = await bus.TryExecute(new SaveGame());
```

`Execute` / `Send` throw `CommandHandlerNotFoundException` when no handler is registered. Use `HasHandler<T>()` or `TryExecute` for optional commands.

## Middleware

```csharp
public class LoggingMiddleware : ICommandMiddleware
{
    public async UniTask Invoke(ICommand command, Func<UniTask> next, CancellationToken cancellationToken)
    {
        var start = Time.realtimeSinceStartup;
        await next(); // skip this call to block the command
        Debug.Log($"{command.GetType().Name} took {(Time.realtimeSinceStartup - start) * 1000f:0.0} ms");
    }
}

IDisposable middleware = bus.Use(new LoggingMiddleware()); // runs in the order added
middleware.Dispose();                                      // remove it
```

## Migrating from 1.x

| 1.x | 2.x |
|---|---|
| Registering a second handler silently replaced the first | Throws `InvalidOperationException`; pass `overwrite: true` to replace |
| No handler: warning log, command without result did nothing, command with result returned `default` | Throws `CommandHandlerNotFoundException`; use `TryExecute` / `HasHandler` when the handler is optional |
| `Register` returned `void` | Returns `IDisposable` (existing calls still compile) |
| Handler looked up by the declared type `TCommand` | Looked up by the runtime type of the command, then by `TCommand` |
