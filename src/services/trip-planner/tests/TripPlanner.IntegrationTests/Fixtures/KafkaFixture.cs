using Testcontainers.Kafka;

namespace TripPlanner.IntegrationTests.Fixtures;

[CollectionDefinition("Kafka")]
public class KafkaCollection : ICollectionFixture<KafkaFixture> { }

public class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder().Build();

    public string BootstrapServers { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        BootstrapServers = _container.GetBootstrapAddress();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
