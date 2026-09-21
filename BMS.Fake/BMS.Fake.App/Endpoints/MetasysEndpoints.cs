using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Contracts;
using BMS.Fake.Business.Models;
using System.Text.Json;


namespace BMS.Fake.App.Endpoints;

public static class MetasysEndpoints
{
    /// <summary>Đăng ký toàn bộ HTTP endpoint và Swagger cho Fake Metasys.</summary>
    public static WebApplication MapMetasysEndpoints(this WebApplication app)
    {
        // Dùng một JSON option giống response web để serialize SSE event.
        var sseJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Bật Swagger middleware.
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            // Khai báo OpenAPI document cần hiển thị.
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Fake Metasys API v1");
            // Đặt tiêu đề browser tab.
            options.DocumentTitle = "Fake Metasys API";
            // Hiển thị thời gian request trong Swagger UI.
            options.DisplayRequestDuration();
        });

        // Root chỉ redirect người dùng tới Swagger.
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        // Trả snapshot hiện tại của toàn bộ point.
        app.MapGet("/api/metasys/objects", async (
            IMetasysPointStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(await store.GetAllAsync(cancellationToken)))
            .WithTags("Metasys Objects")
            .WithSummary("List all BMS points")
            .Produces<IReadOnlyList<MetasysPointDto>>();

        // Trả catalog building từ fixture.
        app.MapGet("/api/metasys/buildings", async (
            IMetasysPointStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(await store.GetBuildingsAsync(cancellationToken)))
            .WithTags("Metasys Catalog")
            .WithSummary("List BMS buildings")
            .Produces<IReadOnlyList<BmsBuildingDto>>();

        // Trả catalog equipment từ fixture.
        app.MapGet("/api/metasys/equipment", async (
            IMetasysPointStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(await store.GetEquipmentAsync(cancellationToken)))
            .WithTags("Metasys Catalog")
            .WithSummary("List BMS equipment")
            .Produces<IReadOnlyList<BmsEquipmentDto>>();

        // Tìm một point theo ObjectId.
        app.MapGet("/api/metasys/objects/{objectId}", async (
            string objectId,
            IMetasysPointStore store,
            CancellationToken cancellationToken) =>
        {
            // Store trả bản copy hoặc null.
            var point = await store.GetAsync(objectId, cancellationToken);
            // Không tìm thấy thì trả HTTP 404; tìm thấy thì trả HTTP 200.
            return point is null
                ? Results.NotFound(new { message = $"BMS point '{objectId}' was not found." })
                : Results.Ok(point);
        })
            .WithTags("Metasys Objects")
            .WithSummary("Get the current value of one BMS point")
            .Produces<MetasysPointDto>()
            .Produces(StatusCodes.Status404NotFound);

        // Tạo subscription để client nhận event qua SSE.
        app.MapPost("/api/metasys/subscriptions", async (
            SubscriptionRequestDto request,
            IMetasysPointStore store,
            ISubscriptionManager subscriptions,
            CancellationToken cancellationToken) =>
        {
            // Bỏ ID rỗng và loại duplicate không phân biệt hoa thường.
            var objectIds = request.ObjectIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (objectIds.Length == 0)
            {
                // Không thể tạo subscription nếu không có point hợp lệ.
                return Results.BadRequest(new { message = "At least one objectId is required." });
            }

            // Kiểm tra toàn bộ ID có tồn tại trước khi đăng ký.
            var unknownIds = new List<string>();
            foreach (var objectId in objectIds)
            {
                // Query database từng ID để giữ code dễ đọc và cancellation rõ ràng.
                if (!await store.ExistsAsync(objectId, cancellationToken))
                    unknownIds.Add(objectId);
            }
            if (unknownIds.Count > 0)
            {
                // Trả danh sách ID sai để caller sửa request.
                return Results.BadRequest(new
                {
                    message = "One or more BMS points do not exist.",
                    unknownObjectIds = unknownIds
                });
            }

            // Manager tạo subscription và trả subscription ID.
            return Results.Ok(subscriptions.Create(objectIds));
        })
            .WithTags("COV Subscriptions")
            .WithSummary("Subscribe to one or more BMS points")
            .Produces<SubscriptionResponseDto>()
            .Produces(StatusCodes.Status400BadRequest);

        // Mở SSE stream cho một subscription đã tạo.
        app.MapGet("/api/metasys/subscriptions/{subscriptionId}/stream", async (
            string subscriptionId,
            HttpContext context,
            ISubscriptionManager subscriptions) =>
        {
            // Tìm runtime subscription trong memory.
            var subscription = subscriptions.Get(subscriptionId);
            if (subscription is null)
            {
                // ID không tồn tại thì trả 404.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(
                    new { message = $"Subscription '{subscriptionId}' was not found." },
                    context.RequestAborted);
                return;
            }

            // Thiết lập response headers chuẩn cho Server-Sent Events.
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            // Gợi ý client retry sau 3 giây nếu stream bị ngắt.
            await context.Response.WriteAsync("retry: 3000\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            try
            {
                // Chờ event từ Channel và ghi từng event thành SSE message.
                await foreach (var covEvent in subscription.Events.Reader.ReadAllAsync(context.RequestAborted))
                {
                    // Tên event để client có thể lắng nghe event cov.
                    await context.Response.WriteAsync("event: cov\n", context.RequestAborted);
                    // Serialize CovEvent thành một dòng data JSON.
                    await context.Response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(covEvent, sseJsonOptions)}\n\n",
                        context.RequestAborted);
                    // Flush ngay để client nhận event realtime.
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client đóng connection; đây là kết thúc bình thường của stream.
            }
        })
            .WithTags("COV Subscriptions")
            .WithSummary("Open the Server-Sent Events COV stream")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }
}
