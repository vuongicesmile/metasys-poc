using BMS.Fake.App.Hosting;
using BMS.Fake.App.Endpoints;
using BMS.Fake.DataAccess.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMetasysServices(builder.Configuration);
var app = builder.Build();
// Tạo database riêng và seed fixture trước khi bắt đầu nhận request.
await app.Services.InitializeFakeDatabaseAsync();
app.MapMetasysEndpoints();
app.Run();

public partial class Program;
