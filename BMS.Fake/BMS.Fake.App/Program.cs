using BMS.Fake.App.Hosting;
using BMS.Fake.App.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMetasysServices(builder.Configuration);
var app = builder.Build();
app.MapMetasysEndpoints();
app.Run();

public partial class Program;
