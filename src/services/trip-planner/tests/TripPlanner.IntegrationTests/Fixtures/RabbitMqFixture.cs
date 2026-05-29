using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace TripPlanner.IntegrationTests.Fixtures;

[CollectionDefinition("RabbitMq")]
public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture> { }

public class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string Host     { get; private set; } = null!;
    public int    Port     { get; private set; }
    public string Username => "guest";
    public string Password => "guest";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Host = _container.Hostname;
        Port = _container.GetMappedPublicPort(5672);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IConnectionFactory CreateConnectionFactory() => new ConnectionFactory
    {
        HostName = Host,
        Port     = Port,
        UserName = Username,
        Password = Password,
    };
}
