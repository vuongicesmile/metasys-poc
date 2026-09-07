using FakeMetasysApi.Models;
using FakeMetasysApi.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MetasysPointStore>();
builder.Services.AddSingleton<SubscriptionManager>();
builder.Services.AddHostedService<MetasysSimulator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var sseJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Fake Metasys API v1");
    options.DocumentTitle = "Fake Metasys API";
    options.DisplayRequestDuration();
});

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.MapGet("/api/metasys/objects", (MetasysPointStore store) => Results.Ok(store.GetAll()))
    .WithTags("Metasys Objects")
    .WithSummary("List all BMS points")
    .Produces<IReadOnlyList<MetasysPoint>>();

app.MapGet("/api/metasys/objects/{objectId}", (string objectId, MetasysPointStore store) =>
{
    var point = store.Get(objectId);
    return point is null
        ? Results.NotFound(new { message = $"BMS point '{objectId}' was not found." })
        : Results.Ok(point);
})
    .WithTags("Metasys Objects")
    .WithSummary("Get the current value of one BMS point")
    .Produces<MetasysPoint>()
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/metasys/subscriptions", (
    SubscriptionRequest request,
    MetasysPointStore store,
    SubscriptionManager subscriptions) =>
{
    var objectIds = request.ObjectIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (objectIds.Length == 0)
    {
        return Results.BadRequest(new { message = "At least one objectId is required." });
    }

    var unknownIds = objectIds.Where(id => store.Get(id) is null).ToArray();
    if (unknownIds.Length > 0)
    {
        return Results.BadRequest(new
        {
            message = "One or more BMS points do not exist.",
            unknownObjectIds = unknownIds
        });
    }

    return Results.Ok(subscriptions.Create(objectIds));
})
    .WithTags("COV Subscriptions")
    .WithSummary("Subscribe to one or more BMS points")
    .Produces<SubscriptionResponse>()
    .Produces(StatusCodes.Status400BadRequest);

app.MapGet("/api/metasys/subscriptions/{subscriptionId}/stream", async (
    string subscriptionId,
    HttpContext context,
    SubscriptionManager subscriptions) =>
{
    var subscription = subscriptions.Get(subscriptionId);
    if (subscription is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(
            new { message = $"Subscription '{subscriptionId}' was not found." },
            context.RequestAborted);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    await context.Response.WriteAsync("retry: 3000\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);

    try
    {
        await foreach (var covEvent in subscription.Events.Reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync("event: cov\n", context.RequestAborted);
            await context.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(covEvent, sseJsonOptions)}\n\n",
                context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The SSE client closed the connection.
    }
})
    .WithTags("COV Subscriptions")
    .WithSummary("Open the Server-Sent Events COV stream")
    .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
    .Produces(StatusCodes.Status404NotFound);

app.Run();

public partial class Program;
