using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace com.ktgame.command_bus
{
    public class CommandBus : ICommandBus
    {
        private sealed class Entry
        {
            public ICommandHandler Handler;
            
            public Func<ICommand, CancellationToken, UniTask> Run;
            
            public object TypedRun;
        }

        private sealed class Registration : IDisposable
        {
            private Action _dispose;

            public Registration(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }

        private readonly Dictionary<Type, Entry> _handlers;
        
        private ICommandMiddleware[] _middlewares = Array.Empty<ICommandMiddleware>();

        public CommandBus()
        {
            _handlers = new Dictionary<Type, Entry>();
        }

        #region Register

        public IDisposable Register<TCommand>(ICommandHandler<TCommand> handler, bool overwrite = false) where TCommand : ICommand
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var entry = new Entry
            {
                Handler = handler,
                Run = (command, cancellationToken) => handler.Execute((TCommand)command, cancellationToken),
                TypedRun = null
            };

            return Add(typeof(TCommand), entry, overwrite);
        }

        public IDisposable Register<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> handler, bool overwrite = false) where TCommand : ICommand<TResponse>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Func<ICommand, CancellationToken, UniTask<TResponse>> typedRun =
                (command, cancellationToken) => handler.Execute((TCommand)command, cancellationToken);

            var entry = new Entry
            {
                Handler = handler,
                Run = async (command, cancellationToken) => { await typedRun(command, cancellationToken); },
                TypedRun = typedRun
            };

            return Add(typeof(TCommand), entry, overwrite);
        }

        private IDisposable Add(Type commandType, Entry entry, bool overwrite)
        {
            if (!overwrite && _handlers.TryGetValue(commandType, out var existing))
            {
                throw new InvalidOperationException(
                    $"[CommandBus] Command {commandType.Name} already has a handler ({existing.Handler.GetType().Name}). " +
                    "Pass overwrite: true to replace it.");
            }

            _handlers[commandType] = entry;
            
            return new Registration(() =>
            {
                if (_handlers.TryGetValue(commandType, out var current) && ReferenceEquals(current, entry))
                {
                    _handlers.Remove(commandType);
                }
            });
        }

        public void UnRegister<THandler>() where THandler : ICommandHandler
        {
            RemoveWhere(entry => entry.Handler is THandler);
        }

        public void UnRegister(ICommandHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            RemoveWhere(entry => ReferenceEquals(entry.Handler, handler));
        }

        public void UnRegisterCommand<TCommand>() where TCommand : ICommand
        {
            _handlers.Remove(typeof(TCommand));
        }

        private void RemoveWhere(Func<Entry, bool> predicate)
        {
            List<Type> keysToRemove = null;

            foreach (var pair in _handlers)
            {
                if (predicate(pair.Value))
                {
                    if (keysToRemove == null)
                    {
                        keysToRemove = new List<Type>();
                    }

                    keysToRemove.Add(pair.Key);
                }
            }

            if (keysToRemove == null)
            {
                return;
            }

            foreach (var key in keysToRemove)
            {
                _handlers.Remove(key);
            }
        }

        public bool HasHandler<TCommand>() where TCommand : ICommand
        {
            return _handlers.ContainsKey(typeof(TCommand));
        }

        public bool HasHandler(Type commandType)
        {
            return commandType != null && _handlers.ContainsKey(commandType);
        }

        public void Clear()
        {
            _handlers.Clear();
        }

        #endregion

        #region Execute

        public UniTask Execute<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var entry = Find(command.GetType(), typeof(TCommand));
            cancellationToken.ThrowIfCancellationRequested();

            if (_middlewares.Length == 0)
            {
                return entry.Run(command, cancellationToken);
            }

            return RunPipeline(command, () => entry.Run(command, cancellationToken), cancellationToken);
        }

        public UniTask<TResponse> Execute<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResponse>
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var entry = Find(command.GetType(), typeof(TCommand));
            return RunTyped<TResponse>(entry, command, cancellationToken);
        }

        public UniTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var entry = Find(command.GetType(), null);
            return RunTyped<TResponse>(entry, command, cancellationToken);
        }

        public async UniTask<bool> TryExecute(ICommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!_handlers.TryGetValue(command.GetType(), out var entry))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RunPipeline(command, () => entry.Run(command, cancellationToken), cancellationToken);
            return true;
        }
        
        private Entry Find(Type runtimeType, Type declaredType)
        {
            if (_handlers.TryGetValue(runtimeType, out var entry))
            {
                return entry;
            }

            if (declaredType != null && declaredType != runtimeType && _handlers.TryGetValue(declaredType, out entry))
            {
                return entry;
            }

            throw new CommandHandlerNotFoundException(runtimeType);
        }

        private UniTask<TResponse> RunTyped<TResponse>(Entry entry, ICommand command, CancellationToken cancellationToken)
        {
            if (!(entry.TypedRun is Func<ICommand, CancellationToken, UniTask<TResponse>> typedRun))
            {
                throw new InvalidCastException(
                    $"[CommandBus] Handler {entry.Handler.GetType().Name} for command {command.GetType().Name} " +
                    $"does not implement {nameof(ICommandHandler)}<{command.GetType().Name}, {typeof(TResponse).Name}>.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_middlewares.Length == 0)
            {
                return typedRun(command, cancellationToken);
            }

            return RunTypedPipeline(typedRun, command, cancellationToken);
        }

        private async UniTask<TResponse> RunTypedPipeline<TResponse>(Func<ICommand, CancellationToken, UniTask<TResponse>> typedRun,
            ICommand command, CancellationToken cancellationToken)
        {
            var result = default(TResponse);
            await RunPipeline(command, async () => { result = await typedRun(command, cancellationToken); }, cancellationToken);
            return result;
        }

        #endregion

        #region Middleware

        public IDisposable Use(ICommandMiddleware middleware)
        {
            if (middleware == null)
            {
                throw new ArgumentNullException(nameof(middleware));
            }

            var list = new List<ICommandMiddleware>(_middlewares) { middleware };
            _middlewares = list.ToArray();

            return new Registration(() =>
            {
                var remaining = new List<ICommandMiddleware>(_middlewares);
                if (remaining.Remove(middleware))
                {
                    _middlewares = remaining.ToArray();
                }
            });
        }

        private UniTask RunPipeline(ICommand command, Func<UniTask> handler, CancellationToken cancellationToken)
        {
            var middlewares = _middlewares;
            return middlewares.Length == 0 ? handler() : InvokeMiddleware(middlewares, 0, command, handler, cancellationToken);
        }

        private static UniTask InvokeMiddleware(ICommandMiddleware[] middlewares, int index, ICommand command,
            Func<UniTask> handler, CancellationToken cancellationToken)
        {
            if (index >= middlewares.Length)
            {
                return handler();
            }

            return middlewares[index].Invoke(command,
                () => InvokeMiddleware(middlewares, index + 1, command, handler, cancellationToken),
                cancellationToken);
        }

        #endregion
    }
}
