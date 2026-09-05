using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute;

public abstract class BaseDesignTimeDbContextFactory<TDbContext>
    : IDesignTimeDbContextFactory<TDbContext>
    where TDbContext : DbContext
{
    protected virtual string ConnectionStringName => "Postgresql";

    public TDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();

        var connectionString =
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found.");

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        ConfigureServices(services);

        services.AddDbContext<TDbContext>(options =>
        {
            options.UseNpgsql(connectionString);

            ConfigureDbContext(options);
        });

        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<TDbContext>();
    }

    protected virtual IConfigurationRoot BuildConfiguration()
    {
        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(
                $"appsettings.{environment}.json",
                optional: true)
            .Build();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected virtual void ConfigureDbContext(
        DbContextOptionsBuilder options)
    {
    }
}
