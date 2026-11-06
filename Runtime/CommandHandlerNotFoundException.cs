using System;

namespace com.ktgame.command_bus
{
    public class CommandHandlerNotFoundException : InvalidOperationException
    {
        public Type CommandType { get; }

        public CommandHandlerNotFoundException(Type commandType)
            : base($"[CommandBus] No handler registered for command: {commandType?.Name}")
        {
            CommandType = commandType;
        }
    }
}
