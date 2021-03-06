namespace NServiceBus
{
    using Configuration.AdvanceExtensibility;

    public static class AzureStorageTransportAddressingExtensions
    {
        public static AzureStorageAddressingSettings Addressing(this TransportExtensions<AzureStorageQueueTransport> config)
        {
            AzureStorageAddressingSettings settings;
            var settingsHolder = config.GetSettings();
            if (settingsHolder.TryGet(out settings) == false)
            {
                settings = new AzureStorageAddressingSettings();
                settingsHolder.Set<AzureStorageAddressingSettings>(settings);
            }

            return settings;
        }

        public static IAzureStoragePartitioningSettings Partitioning(this AzureStorageAddressingSettings addressingSettings)
        {
            return addressingSettings;
        }
    }
}