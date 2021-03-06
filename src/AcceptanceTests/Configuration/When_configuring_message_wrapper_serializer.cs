﻿namespace NServiceBus.AcceptanceTests.WindowsAzureStorageQueues.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Azure.Transports.WindowsAzureStorageQueues;
    using EndpointTemplates;
    using MessageInterfaces;
    using NServiceBus.Configuration.AdvanceExtensibility;
    using NUnit.Framework;
    using Serialization;
    using Settings;

    public class When_configuring_message_wrapper_serializer : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_use_configured_serializer_for_wrapper_message()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<DefaultConfigurationEndpoint>(c => c
                    .When(e => e.SendLocal(new MyRequest())))
                .Done(c => c.InvokedHandler)
                .Run();

            Assert.IsTrue(context.InvokedHandler);
            Assert.IsTrue(context.SerializedWrapper);
            Assert.IsTrue(context.DeserializedWrapper);
        }

        class Context : ScenarioContext
        {
            public bool SerializedWrapper { get; set; }
            public bool DeserializedWrapper { get; set; }
            public bool InvokedHandler { get; set; }
        }

        class DefaultConfigurationEndpoint : EndpointConfigurationBuilder
        {
            public DefaultConfigurationEndpoint()
            {
                EndpointSetup<DefaultServer>(e =>
                {
                    e.UseSerialization<JsonSerializer>();
                    e.GetSettings().Set("Transport.AzureStorageQueue.MessageWrapperSerializationDefinition", new CustomSerializer());
                });
            }

            class MyRequestHandler : IHandleMessages<MyRequest>
            {
                Context scenarioContext;

                public MyRequestHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyRequest message, IMessageHandlerContext context)
                {
                    scenarioContext.InvokedHandler = true;
                    return Task.FromResult(0);
                }
            }
        }

        public class MyRequest : IMessage
        {
        }

        class CustomSerializer : SerializationDefinition
        {
            public override Func<IMessageMapper, IMessageSerializer> Configure(ReadOnlySettings settings)
            {
                return mapper => new MyCustomSerializer(settings.Get<ScenarioContext>());
            }
        }

        class MyCustomSerializer : IMessageSerializer
        {
            Context scenarioContext;

            public MyCustomSerializer(ScenarioContext scenarioContext)
            {
                this.scenarioContext = (Context) scenarioContext;
            }

            public void Serialize(object message, Stream stream)
            {
                var wrapper = message as MessageWrapper;
                if (wrapper != null)
                {
                    scenarioContext.SerializedWrapper = true;
                }

                var serializer = new BinaryFormatter();
                serializer.Serialize(stream, message);
            }

            public object[] Deserialize(Stream stream, IList<Type> messageTypes = null)
            {
                var serializer = new BinaryFormatter();

                stream.Position = 0;
                var message = serializer.Deserialize(stream);

                var wrapper = message as MessageWrapper;
                if (wrapper != null)
                {
                    scenarioContext.DeserializedWrapper = true;
                }

                return new[]
                {
                    message
                };
            }

            public string ContentType => "MyCustomSerializer";
        }
    }
}