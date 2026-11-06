using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace com.ktgame.command_bus
{
    public interface ICommandBus
    { 
        IDisposable Register<TCommand>(ICommandHandler<TCommand> handler, bool overwrite = false) where TCommand : ICommand;
        IDisposable Register<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> handler, bool overwrite = false) where TCommand : ICommand<TResponse>;
        void UnRegister<THandler>() where THandler : ICommandHandler;
        void UnRegister(ICommandHandler handler);
        void UnRegisterCommand<TCommand>() where TCommand : ICommand;
        bool HasHandler<TCommand>() where TCommand : ICommand;
        bool HasHandler(Type commandType);

        UniTask Execute<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand;
        UniTask<TResponse> Execute<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResponse>;
        UniTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
        UniTask<bool> TryExecute(ICommand command, CancellationToken cancellationToken = default);
        IDisposable Use(ICommandMiddleware middleware);
        void Clear();
    }
}
