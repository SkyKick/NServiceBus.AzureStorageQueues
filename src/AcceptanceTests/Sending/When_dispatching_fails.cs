﻿namespace NServiceBus.AcceptanceTests.WindowsAzureStorageQueues.Sending
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTests;
    using EndpointTemplates;
    using ScenarioDescriptors;
    using NUnit.Framework;

    public class When_dispatching_fails : NServiceBusAcceptanceTest
    {
        [Test]
        public void Should_log_send_related_error()
        {
            Assert.Throws<AggregateException>(async () => await Scenario.Define<Context>()
                .WithEndpoint<Sender>(b => b.When((bus, c) => Send(bus)))
                .WithEndpoint<Receiver>()
                .Done(c => c.Logs.Any(li => li.Message.Contains("Fail on proxy") && li.Level == "error"))
                .Repeat(r => r.For(Transports.Default))
                .Run());
        }

        [Test]
        public void Should_log_queue_related_error_if_queue_doesnt_exist()
        {
            const string queue = "non-existent";

            Assert.Throws<AggregateException>(async () => await Scenario.Define<Context>()
                .WithEndpoint<Sender>(b => b.When((bus, c) =>
                {
                    var options = new SendOptions();
                    options.SetDestination(queue);
                    return bus.Send(new MyMessage(), options);
                }))
                .WithEndpoint<Receiver>()
                .Done(c => c.Logs.Any(li => li.Message.Contains($"The queue {queue} was not found. Create the queue.") && li.Level == "error"))
                .Repeat(r => r.For(Transports.Default))
                .Run());
        }

        static async Task Send(IMessageSession messageSession)
        {
            await messageSession.Send(new MyMessage()).ConfigureAwait(false);
            var previous = WebRequest.DefaultWebProxy;
            WebRequest.DefaultWebProxy = new ThrowingProxy();
            try
            {
                await messageSession.Send(new MyMessage());
            }
            finally
            {
                WebRequest.DefaultWebProxy = previous;
            }
        }

        class ThrowingProxy : IWebProxy
        {
            public Uri GetProxy(Uri destination)
            {
                throw ThrownException;
            }

            public bool IsBypassed(Uri host)
            {
                throw ThrownException;
            }

            public ICredentials Credentials { get; set; }
            public static readonly Exception ThrownException = new Exception("Fail on proxy");
        }

        public class Context : ScenarioContext
        {
        }

        public class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>()
                    .AddMapping<MyMessage>(typeof(Receiver));
            }
        }

        public class Receiver : EndpointConfigurationBuilder
        {
            public Receiver()
            {
                EndpointSetup<DefaultServer>();
            }
        }

        public class MyMessage : IMessage
        {
        }

        public class MyMessageHandler : IHandleMessages<MyMessage>
        {
            public Task Handle(MyMessage message, IMessageHandlerContext context)
            {
                return Task.FromResult(0);
            }
        }
    }
}