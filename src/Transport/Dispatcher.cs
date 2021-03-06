﻿namespace NServiceBus.AzureStorageQueues
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Azure.Transports.WindowsAzureStorageQueues;
    using Config;
    using Extensibility;
    using Logging;
    using Microsoft.WindowsAzure.Storage.Queue;
    using Transports;
    using Unicast.Queuing;

    class Dispatcher : IDispatchMessages
    {
        public Dispatcher(CreateQueueClients createQueueClients, MessageWrapperSerializer messageSerializer, QueueAddressGenerator addressGenerator, AzureStorageAddressingSettings addressing)
        {
            this.createQueueClients = createQueueClients;
            this.messageSerializer = messageSerializer;
            this.addressGenerator = addressGenerator;
            this.addressing = addressing;
        }

        public async Task Dispatch(TransportOperations outgoingMessages, ContextBag context)
        {
            if (outgoingMessages.MulticastTransportOperations.Any())
            {
                throw new Exception("The Azure Storage Queue transport only supports unicast transport operations.");
            }

            var sends = new List<Task>(outgoingMessages.UnicastTransportOperations.Count());
            foreach (var unicastTransportOperation in outgoingMessages.UnicastTransportOperations)
            {
                sends.Add(Send(unicastTransportOperation));
            }

            await Task.WhenAll(sends).ConfigureAwait(false);
        }


        async Task Send(UnicastTransportOperation operation)
        {
            // The destination might be in a queue@destination format
            var destination = operation.Destination;

            var queue = QueueAddress.Parse(destination);
            var connectionString = addressing.Map(queue.StorageAccount);

            var sendClient = createQueueClients.Create(connectionString);
            var q = addressGenerator.GetQueueName(queue.QueueName);
            var sendQueue = sendClient.GetQueueReference(q);

            if (!await ExistsAsync(sendQueue).ConfigureAwait(false))
            {
                throw new QueueNotFoundException
                {
                    Queue = queue.ToString()
                };
            }

            var toBeReceived = operation.GetTimeToBeReceived();
            var timeToBeReceived = toBeReceived.HasValue && toBeReceived.Value < TimeSpan.MaxValue ? toBeReceived : null;

            if (timeToBeReceived != null && timeToBeReceived.Value == TimeSpan.Zero)
            {
                var messageType = operation.Message.Headers[Headers.EnclosedMessageTypes].Split(',').First();
                logger.WarnFormat("TimeToBeReceived is set to zero for message of type '{0}'. Cannot send operation.", messageType);
                return;
            }

            // user explicitly specified TimeToBeReceived that is not TimeSpan.MaxValue - fail
            if (timeToBeReceived != null && timeToBeReceived.Value > CloudQueueMessage.MaxTimeToLive && timeToBeReceived != TimeSpan.MaxValue)
            {
                var messageType = operation.Message.Headers[Headers.EnclosedMessageTypes].Split(',').First();
                throw new InvalidOperationException($"TimeToBeReceived is set to more than 7 days (maximum for Azure Storage queue) for message type '{messageType}'.");
            }

            // TimeToBeReceived was not specified on message - go for maximum set by SDK
            if (timeToBeReceived == TimeSpan.MaxValue)
            {
                timeToBeReceived = null;
            }

            var rawMessage = SerializeMessage(operation, timeToBeReceived);

            await sendQueue.AddMessageAsync(rawMessage, timeToBeReceived, null, null, null).ConfigureAwait(false);
        }

        Task<bool> ExistsAsync(CloudQueue sendQueue)
        {
            var key = sendQueue.Uri.ToString();
            return rememberExistence.GetOrAdd(key, keyNotFound => sendQueue.ExistsAsync());
        }

        CloudQueueMessage SerializeMessage(IOutgoingTransportOperation operation, TimeSpan? timeToBeReceived)
        {
            using (var stream = new MemoryStream())
            {
                var msg = operation.Message;
                var headers = new Dictionary<string, string>(msg.Headers);
                addressing.ApplyMappingOnOutgoingHeaders(headers);

                var messageIntent = default(MessageIntentEnum);
                string messageIntentString;
                if (headers.TryGetValue(Headers.MessageIntent, out messageIntentString))
                {
                    Enum.TryParse(messageIntentString, true, out messageIntent);
                }

                var toSend = new MessageWrapper
                {
                    Id = msg.MessageId,
                    Body = msg.Body,
                    CorrelationId = headers.GetValueOrDefault(Headers.CorrelationId),
                    Recoverable = operation.GetDeliveryConstraint<NonDurableDelivery>() == null,
                    ReplyToAddress = headers.GetValueOrDefault(Headers.ReplyToAddress),
                    TimeToBeReceived = timeToBeReceived ?? TimeSpan.MaxValue,
                    Headers = headers,
                    MessageIntent = messageIntent
                };

                messageSerializer.Serialize(toSend, stream);
                return new CloudQueueMessage(stream.ToArray());
            }
        }

        QueueAddressGenerator addressGenerator;
        AzureStorageAddressingSettings addressing;
        CreateQueueClients createQueueClients;
        ILog logger = LogManager.GetLogger(typeof(Dispatcher));
        MessageWrapperSerializer messageSerializer;
        ConcurrentDictionary<string, Task<bool>> rememberExistence = new ConcurrentDictionary<string, Task<bool>>();
    }
}