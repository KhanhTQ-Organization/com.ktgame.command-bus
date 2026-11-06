using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace com.ktgame.command_bus.tests
{
    public class CommandBusTests
    {
        #region Test types

        private class Ping : ICommand
        {
        }

        private class DerivedPing : Ping
        {
        }

        private class Add : ICommand<int>
        {
            public int A;
            public int B;
        }

        private class PingHandler : ICommandHandler<Ping>
        {
            public int Calls;

            public UniTask Execute(Ping command, CancellationToken cancellationToken = default)
            {
                Calls++;
                return UniTask.CompletedTask;
            }
        }

        private class SpecialPingHandler : PingHandler
        {
        }

        private class AddHandler : ICommandHandler<Add, int>
        {
            public int Calls;

            public UniTask<int> Execute(Add command, CancellationToken cancellationToken = default)
            {
                Calls++;
                return UniTask.FromResult(command.A + command.B);
            }
        }

        private class VoidAddHandler : ICommandHandler<Add>
        {
            public UniTask Execute(Add command, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        private class RecordingMiddleware : ICommandMiddleware
        {
            private readonly string _name;
            private readonly List<string> _log;
            private readonly bool _block;

            public RecordingMiddleware(string name, List<string> log, bool block = false)
            {
                _name = name;
                _log = log;
                _block = block;
            }

            public async UniTask Invoke(ICommand command, Func<UniTask> next, CancellationToken cancellationToken)
            {
                _log.Add(_name + ":before:" + command.GetType().Name);
                if (!_block)
                {
                    await next();
                }

                _log.Add(_name + ":after");
            }
        }

        #endregion

        private CommandBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new CommandBus();
        }

        private static void Run(UniTask task) => task.GetAwaiter().GetResult();

        private static T Run<T>(UniTask<T> task) => task.GetAwaiter().GetResult();

        [Test]
        public void Execute_RunsHandler()
        {
            var handler = new PingHandler();
            _bus.Register(handler);

            Run(_bus.Execute(new Ping()));

            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void Send_InfersResponseType()
        {
            _bus.Register(new AddHandler());

            int result = Run(_bus.Send(new Add { A = 2, B = 3 }));

            Assert.AreEqual(5, result);
        }

        [Test]
        public void Execute_WithExplicitResponseType_StillWorks()
        {
            _bus.Register(new AddHandler());

            Assert.AreEqual(7, Run(_bus.Execute<Add, int>(new Add { A = 3, B = 4 })));
        }

        [Test]
        public void Execute_OnResponseCommand_RunsHandlerAndIgnoresResult()
        {
            var handler = new AddHandler();
            _bus.Register(handler);

            Run(_bus.Execute(new Add { A = 1, B = 1 }));

            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void Execute_UsesRuntimeType_WhenPassedAsInterface()
        {
            var handler = new PingHandler();
            _bus.Register(handler);

            ICommand command = new Ping();
            Run(_bus.Execute(command));

            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void Execute_DerivedCommand_FallsBackToDeclaredType()
        {
            var handler = new PingHandler();
            _bus.Register(handler);

            Ping command = new DerivedPing();
            Run(_bus.Execute(command));

            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void MissingHandler_Throws()
        {
            Assert.Throws<CommandHandlerNotFoundException>(() => Run(_bus.Execute(new Ping())));
            Assert.Throws<CommandHandlerNotFoundException>(() => Run(_bus.Send(new Add())));
            Assert.Throws<CommandHandlerNotFoundException>(() => Run(_bus.Execute<Add, int>(new Add())));
        }

        [Test]
        public void TryExecute_ReturnsFalseWithoutHandler_TrueWithHandler()
        {
            Assert.IsFalse(Run(_bus.TryExecute(new Ping())));

            var handler = new PingHandler();
            _bus.Register(handler);

            Assert.IsTrue(Run(_bus.TryExecute(new Ping())));
            Assert.AreEqual(1, handler.Calls);
        }

        [Test]
        public void Register_Duplicate_Throws_UnlessOverwrite()
        {
            _bus.Register(new PingHandler());

            Assert.Throws<InvalidOperationException>(() => _bus.Register(new PingHandler()));

            var replacement = new PingHandler();
            _bus.Register(replacement, overwrite: true);
            Run(_bus.Execute(new Ping()));

            Assert.AreEqual(1, replacement.Calls);
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _bus.Register((ICommandHandler<Ping>)null));
            Assert.Throws<ArgumentNullException>(() => _bus.Register((ICommandHandler<Add, int>)null));
            Assert.Throws<ArgumentNullException>(() => _bus.Use(null));
        }

        [Test]
        public void Execute_NullCommand_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Run(_bus.Execute<Ping>(null)));
            Assert.Throws<ArgumentNullException>(() => Run(_bus.Send<int>(null)));
        }

        [Test]
        public void DisposeRegistration_RemovesHandler()
        {
            var registration = _bus.Register(new PingHandler());
            Assert.IsTrue(_bus.HasHandler<Ping>());

            registration.Dispose();
            registration.Dispose(); // gọi 2 lần không lỗi

            Assert.IsFalse(_bus.HasHandler<Ping>());
        }

        [Test]
        public void DisposeOldRegistration_AfterOverwrite_KeepsNewHandler()
        {
            var oldRegistration = _bus.Register(new PingHandler());
            var newHandler = new PingHandler();
            _bus.Register(newHandler, overwrite: true);

            oldRegistration.Dispose();

            Assert.IsTrue(_bus.HasHandler<Ping>());
            Run(_bus.Execute(new Ping()));
            Assert.AreEqual(1, newHandler.Calls);
        }

        [Test]
        public void UnRegister_ByHandlerType_IncludesSubclasses()
        {
            _bus.Register(new SpecialPingHandler());

            _bus.UnRegister<PingHandler>();

            Assert.IsFalse(_bus.HasHandler<Ping>());
        }

        [Test]
        public void UnRegister_ByInstance_And_ByCommand()
        {
            var ping = new PingHandler();
            _bus.Register(ping);
            _bus.Register(new AddHandler());

            _bus.UnRegister(new PingHandler()); // instance khác: không gỡ
            Assert.IsTrue(_bus.HasHandler<Ping>());

            _bus.UnRegister(ping);
            Assert.IsFalse(_bus.HasHandler<Ping>());

            _bus.UnRegisterCommand<Add>();
            Assert.IsFalse(_bus.HasHandler(typeof(Add)));
        }

        [Test]
        public void Clear_RemovesAllHandlers()
        {
            _bus.Register(new PingHandler());
            _bus.Register(new AddHandler());

            _bus.Clear();

            Assert.IsFalse(_bus.HasHandler<Ping>());
            Assert.IsFalse(_bus.HasHandler<Add>());
        }

        [Test]
        public void Middleware_RunsInOrder_AndPassesResultThrough()
        {
            var log = new List<string>();
            _bus.Use(new RecordingMiddleware("A", log));
            _bus.Use(new RecordingMiddleware("B", log));
            _bus.Register(new AddHandler());

            var result = Run(_bus.Send(new Add { A = 10, B = 5 }));

            Assert.AreEqual(15, result);
            CollectionAssert.AreEqual(new[] { "A:before:Add", "B:before:Add", "B:after", "A:after" }, log);
        }

        [Test]
        public void Middleware_CanBlockCommand()
        {
            var log = new List<string>();
            var handler = new PingHandler();
            _bus.Use(new RecordingMiddleware("Guard", log, block: true));
            _bus.Register(handler);

            Run(_bus.Execute(new Ping()));

            Assert.AreEqual(0, handler.Calls);
        }

        [Test]
        public void Middleware_Dispose_RemovesIt()
        {
            var log = new List<string>();
            var middleware = _bus.Use(new RecordingMiddleware("A", log));
            _bus.Register(new PingHandler());

            middleware.Dispose();
            Run(_bus.Execute(new Ping()));

            Assert.IsEmpty(log);
        }

        [Test]
        public void CancelledToken_ThrowsBeforeHandler()
        {
            var handler = new PingHandler();
            _bus.Register(handler);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => Run(_bus.Execute(new Ping(), cts.Token)));
            Assert.AreEqual(0, handler.Calls);
        }

        [Test]
        public void Send_WithVoidHandler_ThrowsInvalidCast()
        {
            _bus.Register<Add>(new VoidAddHandler());

            Assert.Throws<InvalidCastException>(() => Run(_bus.Send(new Add())));
        }
    }
}
