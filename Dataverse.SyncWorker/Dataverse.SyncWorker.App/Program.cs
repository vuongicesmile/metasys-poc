using DataverseSyncWorker.Hosting;
using DataverseSyncWorker.Endpoints;

// Tách command maintenance khỏi args dùng cho ASP.NET host.
// Nhờ vậy các lệnh --self-test, --verify, --provision... không khởi động worker nền.
var commandLine = WorkerCommandLine.Parse(args);

// WebApplication vừa cung cấp DI/configuration, vừa expose status/Swagger endpoint.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = commandLine.HostArgs,
    ContentRootPath = AppContext.BaseDirectory
});

// Cho phép cài process này thành Windows Service với tên ổn định.
builder.Host.UseWindowsService(options => options.ServiceName = "FMCentralDataverseSync");

// Đăng ký toàn bộ adapter SQL, Dataverse, command processor và hosted worker.
builder.Services.AddDataverseSync(builder.Configuration);
await using var app = builder.Build();

// Maintenance commands chạy xong rồi thoát trước app.RunAsync();
// đây là ranh giới an toàn để verify/provision không vô tình chạy sync nền.
if (await app.Services.GetRequiredService<WorkerCommandDispatcher>().Execute(commandLine)) return;

// Chỉ process runtime bình thường mới mở các endpoint trạng thái và bắt đầu worker.
app.MapSyncEndpoints();
await app.RunAsync();
