using JasperFx;
using Marten;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Server.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHearthCalendarPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<HearthCalendarDatabaseOptions>()
            .Bind(configuration.GetSection(HearthCalendarDatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database connection string is required.")
            .ValidateOnStart();

        services.AddMarten(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<HearthCalendarDatabaseOptions>>()
                .Value;
            var storeOptions = new StoreOptions();

            storeOptions.Connection(options.ConnectionString);
            storeOptions.DatabaseSchemaName = options.SchemaName;
            storeOptions.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            storeOptions.Schema.For<EventIntentDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<CalendarEventDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<ReviewDecisionDocument>()
                .Identity(document => document.Id)
                .UseOptimisticConcurrency(true);
            storeOptions.Schema.For<AiReviewSuggestionDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<AuditEntryDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<ClientCredentialDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<FeedTokenDocument>().Identity(document => document.Id);
            storeOptions.Schema.For<CalDavCredentialDocument>().Identity(document => document.Id);

            return storeOptions;
        });

        services.AddScoped<IHearthCalendarStore, MartenHearthCalendarStore>();

        return services;
    }
}
