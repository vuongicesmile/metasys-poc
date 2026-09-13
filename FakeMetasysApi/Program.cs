using FakeMetasysApi.Hosting;
using FakeMetasysApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMetasysServices();
var app = builder.Build();
app.MapMetasysEndpoints();
app.Run();

public partial class Program;
