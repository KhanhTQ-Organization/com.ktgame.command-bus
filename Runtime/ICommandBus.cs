using System.Threading;
using Cysharp.Threading.Tasks;

namespace com.ktgame.command_bus
{
    public interface ICommandBus
    {
        void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand;

        void Register<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> handler) where TCommand : ICommand<TResponse>;

        void UnRegister<THandler>() where THandler : ICommandHandler;

        UniTask Execute<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand;

        UniTask<TResponse> Execute<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResponse>;

        void Clear();
    }
}