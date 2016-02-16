namespace NServiceBus.Azure.Transports.WindowsAzureStorageQueues
{
    using System.Globalization;
    using Support;

    class QueueIndividualizer
    {
        public static string Individualize(string queueName)
        {
            var individualQueueName = queueName;
            if (SafeRoleEnvironment.IsAvailable)
            {
                var index = ConnectionStringParser.ParseIndexFrom(SafeRoleEnvironment.CurrentRoleInstanceId);

                var currentQueue = ConnectionStringParser.ParseQueueNameFrom(queueName);
                if (!currentQueue.EndsWith("-" + index.ToString(CultureInfo.InvariantCulture))) //individualize can be applied multiple times
                {
                    individualQueueName = currentQueue
                                          + (index > 0 ? "-" : "")
                                          + (index > 0 ? index.ToString(CultureInfo.InvariantCulture) : "");

                    if (queueName.Contains("@"))
                        individualQueueName += "@" + ConnectionStringParser.ParseNamespaceFrom(queueName);
                }
            }
            else
            {
                var currentQueue = ConnectionStringParser.ParseQueueNameFrom(queueName);
                if (!currentQueue.EndsWith("-" + RuntimeEnvironment.MachineName)) //individualize can be applied multiple times
                {
                    individualQueueName = currentQueue + "-" + RuntimeEnvironment.MachineName;

                    if (queueName.Contains("@"))
                        individualQueueName += "@" + ConnectionStringParser.ParseNamespaceFrom(queueName);
                }
            }

            return individualQueueName;
        }

        public static string Discriminator {
            get
            {
                if (SafeRoleEnvironment.IsAvailable)
                {
                    var index = ConnectionStringParser.ParseIndexFrom(SafeRoleEnvironment.CurrentRoleInstanceId);

                    return "-" + index.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    return "-" + RuntimeEnvironment.MachineName;
                }
            }
        }
    }
}