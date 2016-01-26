﻿namespace NServiceBus.Azure.Transports.WindowsAzureStorageQueues
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Queue;
    using NServiceBus.Transports;

    /// <summary>
    ///     Creates the queues. Note that this class will only be invoked when running the windows host and not when running in
    ///     the fabric
    /// </summary>
    public class AzureMessageQueueCreator : ICreateQueues
    {
        readonly CloudQueueClient client;

        public AzureMessageQueueCreator(CloudQueueClient client)
        {
            this.client = client;
        }

        public async Task CreateQueueIfNecessary(QueueBindings queueBindings, string identity)
        {
            // TODO: possibly Task.WhenAll ?

            foreach (var address in queueBindings.ReceivingAddresses)
            {
                await CreateQueue(address);
            }

            foreach (var address in queueBindings.SendingAddresses)
            {
                await CreateQueue(address);
            }
        }

        private async Task CreateQueue(string address)
        {
            var queueName = AzureMessageQueueUtils.GetQueueName(address);
            try
            {
                var queue = client.GetQueueReference(queueName);
                await queue.CreateIfNotExistsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new StorageException($"Failed to create queue: {queueName}, because {ex.Message}.", ex);
            }
        }
    }
}