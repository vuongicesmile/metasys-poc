using BmsIngestionApp.Models;
using BmsIngestionApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BmsIngestionApp.Endpoints;

public static class IngestionEndpoints
{
    public static WebApplication MapIngestionEndpoints(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "BMS Ingestion API v1");
            options.DocumentTitle = "BMS Ingestion App";
            options.DisplayRequestDuration();
        });

        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        app.MapGet("/api/ingestion/status", (IngestionStatusTracker status) => status.GetSnapshot())
            .WithTags("Ingestion")
            .WithSummary("Get COV ingestion and SQL persistence status")
            .Produces<IngestionStatusSnapshot>();

        app.MapGet("/api/ingestion/events/recent", (IngestionStatusTracker status) => status.GetRecentEvents())
            .WithTags("Ingestion")
            .WithSummary("Get the 25 most recent COV events")
            .Produces<IReadOnlyList<CovEvent>>();
        return app;
    }
}
