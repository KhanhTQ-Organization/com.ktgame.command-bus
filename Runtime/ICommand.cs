namespace com.ktgame.command_bus
{
    public interface ICommand { }

    public interface ICommand<out TResponse> : ICommand { }
}