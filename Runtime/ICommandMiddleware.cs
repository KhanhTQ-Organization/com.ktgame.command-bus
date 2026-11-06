using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace com.ktgame.command_bus
{
    public interface ICommandMiddleware
    {
        UniTask Invoke(ICommand command, Func<UniTask> next, CancellationToken cancellationToken);
    }
}
