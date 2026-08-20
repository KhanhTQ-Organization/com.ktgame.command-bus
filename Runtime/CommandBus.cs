using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace com.ktgame.command_bus
{
    public class CommandBus : ICommandBus
    {
        private readonly Dictionary<Type, ICommandHandler> _handlers;

        public CommandBus()
        {
            _handlers = new Dictionary<Type, ICommandHandler>();
        }

        public void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
        {
            var type = typeof(TCommand);
            if (!_handlers.ContainsKey(type))
            {
                _handlers.Add(type, handler);
            }
            else
            {
                _handlers[type] = handler;
            }
        }

        public void Register<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> handler) where TCommand : ICommand<TResponse>
        {
            var type = typeof(TCommand);
            if (!_handlers.ContainsKey(type))
            {
                _handlers.Add(type, handler);
            }
            else
            {
                _handlers[type] = handler;
            }
        }

        public void UnRegister<THandler>() where THandler : ICommandHandler
        {
            var handlerType = typeof(THandler);
            List<Type> keysToRemove = null;
            
            foreach (var kvp in _handlers)
            {
                if (kvp.Value.GetType() == handlerType)
                {
                    if (keysToRemove == null) keysToRemove = new List<Type>();
                    keysToRemove.Add(kvp.Key);
                }
            }

            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    _handlers.Remove(key);
                }
            }
        }

        private void UnRegister(Type type)
        {
            if (_handlers.ContainsKey(type))
            {
                _handlers.Remove(type);
            }
        }

        public async UniTask Execute<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            var type = typeof(TCommand);
            if (_handlers.TryGetValue(type, out var rawHandler))
            {
                if (rawHandler is ICommandHandler<TCommand> handler)
                {
                    await handler.Execute(command, cancellationToken);
                }
                else
                {
                    throw new InvalidCastException($"Cannot cast handler from {rawHandler.GetType()} to {typeof(ICommandHandler<TCommand>)}");
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[CommandBus] No handler registered for command: {type.Name}");
            }
        }

        public async UniTask<TResponse> Execute<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResponse>
        {
            var type = typeof(TCommand);
            if (_handlers.TryGetValue(type, out var rawHandler))
            {
                if (rawHandler is ICommandHandler<TCommand, TResponse> handler)
                {
                    return await handler.Execute(command, cancellationToken);
                }
                else
                {
                    throw new InvalidCastException($"[{GetType().Name}] Cannot cast handler from {rawHandler.GetType()} to {typeof(ICommandHandler<TCommand>)}");
                }
            }

            UnityEngine.Debug.LogWarning($"[CommandBus] No handler registered for command with response: {type.Name}. Returning default.");
            return default(TResponse);
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}