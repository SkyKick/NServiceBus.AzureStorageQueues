﻿namespace NServiceBus.Azure.Transports.WindowsAzureStorageQueues
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Transactions;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Queue;
    using NServiceBus.Extensibility;
    using NServiceBus.Settings;
    using NServiceBus.Transports;
    using NServiceBus.Unicast.Queuing;

    public class AzureMessageQueueSender2 : IDispatchMessages
    {
        readonly ICreateQueueClients createQueueClients;

        public AzureMessageQueueSender2(ICreateQueueClients createQueueClients)
        {
            this.createQueueClients = createQueueClients;
        }

        public Task Dispatch(TransportOperations outgoingMessages, ContextBag context)
        {
            if (outgoingMessages.MulticastTransportOperations.Any())
            {
                throw new Exception("The Azure Storage Queue transport only supports unicast transport operations.");
            }

            foreach (var unicastTransportOperation in outgoingMessages.UnicastTransportOperations)
            {
                Send(unicastTransportOperation, context);
            }

            return Task.FromResult(0);
        }

        private void Send(UnicastTransportOperation operation, ContextBag options)
        {
            var address = operation.Destination;

            // TODO: is it address or connection string?
            var sendClient = createQueueClients.Create(address);
            var sendQueue = sendClient.GetQueueReference(AzureMessageQueueUtils.GetQueueName(address));

            if (!Exists(sendQueue))
            {
                throw new QueueNotFoundException
                {
                    Queue = address
                };
            }

            // TODO: start here!

            var timeToBeReceived = options.TimeToBeReceived.HasValue && options.TimeToBeReceived < TimeSpan.MaxValue ? options.TimeToBeReceived : null;
            timeToBeReceived = timeToBeReceived ?? operation.TimeToBeReceived;

            if (timeToBeReceived.Value == TimeSpan.Zero)
            {
                var messageType = operation.Headers[Headers.EnclosedMessageTypes].Split(',').First();
                logger.WarnFormat("TimeToBeReceived is set to zero for message of type '{0}'. Cannot send operation.", messageType);
                return;
            }

            // user explicitly specified TimeToBeReceived that is not TimeSpan.MaxValue - fail
            if (timeToBeReceived.Value > CloudQueueMessage.MaxTimeToLive && timeToBeReceived != TimeSpan.MaxValue)
            {
                var messageType = operation.Headers[Headers.EnclosedMessageTypes].Split(',').First();
                throw new InvalidOperationException(string.Format("TimeToBeReceived is set to more than 7 days (maximum for Azure Storage queue) for message type '{0}'.",
                    messageType));
            }

            // TimeToBeReceived was not specified on message - go for maximum set by SDK
            if (timeToBeReceived == TimeSpan.MaxValue)
            {
                timeToBeReceived = null;
            }

            var rawMessage = SerializeMessage(operation, options);

            if (!config.Settings.Get<bool>("Transactions.Enabled") || Transaction.Current == null)
            {
                sendQueue.AddMessage(rawMessage, timeToBeReceived);
            }
            else
            {
                Transaction.Current.EnlistVolatile(new SendResourceManager(sendQueue, rawMessage, timeToBeReceived), EnlistmentOptions.None);
            }
        }

        bool Exists(CloudQueue sendQueue)
        {
            var key = sendQueue.Uri.ToString();
            return rememberExistence.GetOrAdd(key, keyNotFound => sendQueue.Exists());
        }

        CloudQueueMessage SerializeMessage(OutgoingMessage message, SendOptions options)
        {
            using (var stream = new MemoryStream())
            {
                var validation = new DeterminesBestConnectionStringForStorageQueues();
                var replyToAddress = validation.Determine(config.Settings, message.ReplyToAddress ?? options.ReplyToAddress ?? config.LocalAddress, config.TransportConnectionString());

                var toSend = new MessageWrapper
                {
                    Id = message.Id,
                    Body = message.Body,
                    CorrelationId = message.CorrelationId ?? options.CorrelationId,
                    Recoverable = message.Recoverable,
                    ReplyToAddress = replyToAddress,
                    TimeToBeReceived = options.TimeToBeReceived.HasValue ? options.TimeToBeReceived.Value : message.TimeToBeReceived,
                    Headers = message.Headers,
                    MessageIntent = message.MessageIntent
                };


                MessageSerializer.Serialize(toSend, stream);
                return new CloudQueueMessage(stream.ToArray());
            }
        }
    }

    public class CreateQueueClients : ICreateQueueClients
    {
        readonly ConcurrentDictionary<string, CloudQueueClient> destinationQueueClients = new ConcurrentDictionary<string, CloudQueueClient>();
        readonly ReadOnlySettings settings;

        public CreateQueueClients(ReadOnlySettings settings)
        {
            this.settings = settings;
        }

        public CloudQueueClient Create(string connectionString)
        {
            return destinationQueueClients.GetOrAdd(connectionString, s =>
            {
                var validation = new DeterminesBestConnectionStringForStorageQueues();
                if (!validation.IsPotentialStorageQueueConnectionString(connectionString))
                {
                    // TODO: instead of null, default connection string should be passed
                    connectionString = validation.Determine(settings, null);
                }

                CloudQueueClient sendClient = null;
                CloudStorageAccount account;

                if (CloudStorageAccount.TryParse(connectionString, out account))
                {
                    sendClient = account.CreateCloudQueueClient();
                }

                return sendClient;
            });
        }
    }

    public interface ICreateQueueClients
    {
        CloudQueueClient Create(string connectionString);
    }
}