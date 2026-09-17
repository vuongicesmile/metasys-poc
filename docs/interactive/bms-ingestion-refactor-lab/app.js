(() => {
  const steps = [
    {
      group: 'Định hướng',
      title: 'Nhìn đúng cấu trúc đã triển khai',
      summary: 'Một solution toàn repo, một executable và bốn class library tên BMS.Ingestion.*.',
      lesson: `<h3>Tên thật trong source</h3><p><strong>Project</strong> là compilation boundary tạo DLL. <strong>Solution</strong> chỉ gom project để build/debug.</p><p>Không tạo solution ingestion riêng, không chia DataAccess thành hai project. HTTP/SSE và SQL cùng nằm trong <code>BMS.Ingestion.DataAccess</code>.</p>`,
      codeTitle: 'Target tree · đã áp dụng',
      code: `MetasysPoc.sln
├─ BmsIngestionApp/BMS.IngestionApp.csproj
├─ BMS.Ingestion.Domain
├─ BMS.Ingestion.Common
├─ BMS.Ingestion.Business
└─ BMS.Ingestion.DataAccess`,
      check: 'Tên project trên UI khớp chính xác với project đang có trong MetasysPoc.sln.'
    },
    {
      group: 'Định hướng',
      title: 'Chốt dependency trước khi move code',
      summary: 'Domain ở đáy; Business sở hữu ports; DataAccess implements; Presentation compose.',
      lesson: `<h3>Dependency inversion</h3><p>Business nói “tôi cần đọc catalog/event và lưu reading” bằng interface. DataAccess trả lời “tôi làm bằng HTTP/SSE và SQL Server”.</p><p>Nếu Business reference DataAccess, việc test và thay adapter sẽ khó hơn, đồng thời tạo sai chiều phụ thuộc.</p>`,
      codeTitle: 'Allowed project references',
      code: `Business   → Common + Domain
DataAccess → Business + Common + Domain
App        → Business + Common + Domain + DataAccess

KHÔNG: Business → DataAccess
KHÔNG: Domain → SQL / HTTP / ASP.NET`,
      check: 'Không có reference Business → DataAccess hoặc Domain → infrastructure.'
    },
    {
      group: 'Chuẩn bị',
      title: 'Tạo baseline và xác nhận .NET 10',
      summary: 'Build/test trước refactor, rồi kiểm tra bốn library đều là SDK-style net10.0.',
      lesson: `<h3>Tại sao baseline?</h3><p>Nếu move xong bị lỗi, baseline giúp biết lỗi mới do refactor hay lỗi đã có từ trước.</p><p>Bốn template <code>.NET Framework 4.6.2</code> tên <code>BmsIngestion.*</code> cũ chỉ có <code>Class1.cs</code> đã bị loại bỏ. Project dùng thật có tên viết hoa <code>BMS.Ingestion.*</code>.</p>`,
      codeTitle: 'PowerShell · baseline',
      code: `dotnet sln .\MetasysPoc.sln list
dotnet build .\MetasysPoc.sln -c Release -p:SignAssembly=false
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj -c Release

Get-ChildItem .\BMS.Ingestion.*\*.csproj`,
      check: 'Solution liệt kê đúng bốn library và BMS.IngestionApp; framework là net10.0.'
    },
    {
      group: 'Move source',
      title: 'Tách domain models trước',
      summary: 'CovEvent, Building, Equipment, Point và subscription models chuyển vào Domain.',
      lesson: `<h3>Domain là dữ liệu nghiệp vụ thuần</h3><p>Move layer đáy trước vì các layer khác đều dùng nó. Chỉ đổi namespace và path; không đổi JSON shape, <code>decimal</code>, timestamp hoặc identity.</p><h3>Before → After</h3><p><code>BmsIngestionApp/Models/CovEvent.cs</code><br>→ <code>BMS.Ingestion.Domain/Models/CovEvent.cs</code></p><p><code>MetasysCatalog.cs</code><br>→ <code>BMS.Ingestion.Domain/Models/BmsCatalog.cs</code></p>`,
      codeTitle: 'C# · namespace mới',
      code: `namespace BMS.Ingestion.Domain.Models;

public sealed class CovEvent
{
    public string ObjectId { get; init; } = "";
    public decimal CurrentValue { get; init; }
    public DateTime Timestamp { get; init; }
}`,
      check: 'Domain chỉ chứa models thuần và không reference project nội bộ nào.'
    },
    {
      group: 'Move source',
      title: 'Tách configuration vào Common',
      summary: 'Settings dùng chung được tách khỏi models nghiệp vụ và khỏi ASP.NET host.',
      lesson: `<h3>Common khác Domain</h3><p><code>CovEvent</code> mô tả nghiệp vụ nên thuộc Domain. <code>AppSettings</code> mô tả cách ứng dụng được cấu hình nên thuộc Common.</p><p><code>IngestionRuntimeOptions</code> giữ trạng thái <code>--no-sql</code> riêng, giúp worker test được mà không phụ thuộc command line.</p>`,
      codeTitle: 'C# · Common configuration',
      code: `namespace BMS.Ingestion.Common.Configuration;

public sealed class AppSettings
{
    public MetasysSettings Metasys { get; init; } = new();
    public SqlSettings Sql { get; init; } = new();
}

public sealed record IngestionRuntimeOptions(bool SqlEnabled);`,
      check: 'Common không chứa SqlConnection, HttpClient, endpoint hoặc workflow.'
    },
    {
      group: 'Business',
      title: 'Đưa ports vào BMS.Ingestion.Business',
      summary: 'IMetasysClient và IBmsReadingRepository mô tả nhu cầu của use case.',
      lesson: `<h3>Business sở hữu interface</h3><p>Interface nằm cạnh code sử dụng nó, không nằm cạnh SQL/HTTP implementation. Nhờ vậy unit test có thể truyền fake source và fake repository.</p><h3>Ý nghĩa method</h3><ul><li><code>ReadCatalogAsync</code>: lấy Building, Equipment, Point.</li><li><code>SubscribeAsync</code>: tạo subscription theo object IDs.</li><li><code>ReadEventsAsync</code>: stream COV event.</li><li><code>PersistCatalogAsync</code>/<code>InsertAsync</code>: yêu cầu lưu dữ liệu.</li></ul>`,
      codeTitle: 'C# · business ports',
      code: `public interface IMetasysClient
{
    Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct);
    Task<SubscriptionResponse> SubscribeAsync(
        IEnumerable<string> objectIds, CancellationToken ct);
    IAsyncEnumerable<CovEvent> ReadEventsAsync(
        string subscriptionId, CancellationToken ct);
}

public interface IBmsReadingRepository
{
    Task InsertAsync(CovEvent value, CancellationToken ct = default);
}`,
      check: 'Ports dùng Domain models nhưng không import implementation từ DataAccess.'
    },
    {
      group: 'Business',
      title: 'Move orchestration, giữ nguyên thứ tự',
      summary: 'CovIngestionWorker điều phối use case; không tự new HttpClient hay SqlConnection.',
      lesson: `<h3>Workflow phải giữ nguyên</h3><ol><li>Đọc catalog.</li><li>Nếu SQL bật, persist Building rồi Equipment.</li><li>Subscribe danh sách Point.</li><li>Đọc từng COV event.</li><li>Cập nhật status, rồi insert nếu SQL bật.</li></ol><p><code>IngestionStatusTracker</code> cũng chuyển vào Business vì nó mô tả trạng thái của use case.</p>`,
      codeTitle: 'PowerShell · boundary check',
      code: `rg -n "SqlConnection|Microsoft.Data.SqlClient|System.Net.Http.Json|HttpClient" \
  .\BMS.Ingestion.Business \
  .\BMS.Ingestion.Common \
  .\BMS.Ingestion.Domain -g "*.cs"

# Kỳ vọng: không có match`,
      check: 'Business vẫn compile và boundary search không trả kết quả.'
    },
    {
      group: 'DataAccess',
      title: 'Cô lập HTTP, JSON và SSE trong MetasysClient',
      summary: 'MetasysClient implement IMetasysClient và sở hữu toàn bộ transport detail.',
      lesson: `<h3>Vì sao thuộc DataAccess?</h3><p>URL, JSON serializer, <code>HttpClient</code>, SSE <code>data:</code> line và response disposal là chi tiết giao tiếp ngoài hệ thống.</p><p><code>ResponseHeadersRead</code> cho phép xử lý stream ngay. Cancellation được kiểm tra sau khi đọc line vì <code>StreamReader</code> có thể đã buffer dữ liệu.</p>`,
      codeTitle: 'C# · SSE boundary',
      code: `using var response = await client.GetAsync(
    streamUrl,
    HttpCompletionOption.ResponseHeadersRead,
    ct);
response.EnsureSuccessStatusCode();

while (await reader.ReadLineAsync(ct) is { } line)
{
    ct.ThrowIfCancellationRequested();
    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
    // deserialize CovEvent
}`,
      check: 'SSE order, disposal, precision và cancellation tests vẫn pass.'
    },
    {
      group: 'DataAccess',
      title: 'Cô lập SQL trong BmsReadingRepository',
      summary: 'SQL adapter giữ commands và transaction ngoài executable nhưng data contract không đổi.',
      lesson: `<h3>Invariants cần giữ</h3><ul><li><code>raw.bms_reading</code> append-only.</li><li>Giá trị dùng <code>DECIMAL(18,4)</code>.</li><li>Catalog chạy trong transaction.</li><li>Building ghi trước Equipment.</li><li>Duplicate codes và missing Building vẫn bị chặn.</li></ul>`,
      codeTitle: 'C# · decimal parameter',
      code: `var valueParameter = command.Parameters.Add(
    "@reading_value", SqlDbType.Decimal);
valueParameter.Precision = 18;
valueParameter.Scale = 4;
valueParameter.Value = covEvent.CurrentValue;

await command.ExecuteNonQueryAsync(cancellationToken);`,
      check: 'SQL chỉ xuất hiện trong BMS.Ingestion.DataAccess; schema và precision không đổi.'
    },
    {
      group: 'Presentation',
      title: 'Rewire Dependency Injection',
      summary: 'BMS.IngestionApp chỉ giữ startup, endpoint và chỗ chọn implementation thật.',
      lesson: `<h3>Composition root</h3><p>Presentation được reference Business và DataAccess vì đây là nơi nối port với adapter.</p><ul><li>Tracker là singleton để endpoint và worker dùng cùng instance.</li><li>Named HttpClient giữ BaseUrl/timeout ngoài Business.</li><li>Hosted service chạy worker theo lifecycle của host.</li></ul>`,
      codeTitle: 'C# · ServiceCollectionExtensions',
      code: `services.AddSingleton<IngestionStatusTracker>();
services.AddSingleton<IBmsReadingRepository>(
    _ => new BmsReadingRepository(settings.Sql.ConnectionString));
services.AddHttpClient(MetasysClient.ClientName, client =>
{
    client.BaseAddress = new Uri(settings.Metasys.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
services.AddSingleton<IMetasysClient, MetasysClient>();
services.AddHostedService<CovIngestionWorker>();`,
      check: 'Program/Endpoints/Hosting không chứa SQL statement hoặc SSE parser.'
    },
    {
      group: 'Verification',
      title: 'Sửa tests và chạy final gate',
      summary: 'Tests reference đúng bốn library; chỉ đổi namespace, không nới assertion.',
      lesson: `<h3>Kết quả đã chạy</h3><p><strong>Build:</strong> 0 warnings, 0 errors.<br><strong>Tests:</strong> 35 passed, 0 failed, 0 skipped.</p><p>Worker tests giữ thứ tự catalog → persist → subscribe → stream → insert. Client tests giữ behavior JSON/SSE, precision, disposal và cancellation.</p>`,
      codeTitle: 'PowerShell · final gate',
      code: `dotnet build .\MetasysPoc.sln -c Release -p:SignAssembly=false
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj \
  -c Release --no-build
git diff --check`,
      check: 'Release build và toàn bộ 35 tests đều pass.'
    },
    {
      group: 'Verification',
      title: 'Smoke test xuyên layer với --no-sql',
      summary: 'Chứng minh Presentation → Business → DataAccess HTTP hoạt động mà không ghi SQL.',
      lesson: `<h3>Receipt đã kiểm tra</h3><p><code>State = Listening</code><br><code>SqlEnabled = False</code><br><code>SubscriptionId = SUB-001</code><br><code>EventsReceived = 32</code><br><code>RowsInserted = 0</code><br><code>RecentCount = 25</code></p><p>Sau đó bỏ <code>--no-sql</code> để test SQL local; không cần chạy DataverseSyncWorker trong scope refactor này.</p>`,
      codeTitle: 'PowerShell · 3 terminals',
      code: `# Terminal 1
dotnet run --project .\FakeMetasysApi -c Release --no-build

# Terminal 2
dotnet run --project .\BmsIngestionApp\BMS.IngestionApp.csproj \
  -c Release --no-build -- --no-sql

# Terminal 3
Invoke-RestMethod http://localhost:5200/api/ingestion/status
Invoke-RestMethod http://localhost:5200/api/ingestion/events/recent`,
      check: 'Listening; EventsReceived tăng; RowsInserted bằng 0.'
    }
  ];

  const acceptance = [
    'Tên project/namespace dùng đúng BMS.Ingestion.*.',
    'Domain/Common/Business không phụ thuộc SQL hoặc HTTP adapter.',
    'Presentation chỉ còn Program, Endpoints, Hosting và configuration.',
    'Port 5100/5200 và API routes không đổi.',
    'SQL schema, DECIMAL(18,4) và append-only history không đổi.',
    'Catalog vẫn được persist trước khi nhận readings.',
    'Build đạt 0 warnings, 0 errors; 35 tests pass.',
    '--no-sql nhận COV nhưng không ghi SQL.',
    'Không chạy provisioning/deployment Dataverse trong scope này.'
  ];

  const storageKey = 'fmc.bms.ingestion.refactor.steps.v2';
  let done = new Set(JSON.parse(localStorage.getItem(storageKey) || '[]'));
  const $ = id => document.getElementById(id);
  const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));

  function renderNavigation(query = '') {
    const normalized = query.trim().toLocaleLowerCase('vi');
    let previousGroup = '';
    $('navigation').innerHTML = steps.map((step, index) => {
      if (normalized && !(step.title + ' ' + step.summary + ' ' + step.group).toLocaleLowerCase('vi').includes(normalized)) return '';
      const group = step.group === previousGroup ? '' : `<div class="nav-group">${escapeHtml(step.group)}</div>`;
      previousGroup = step.group;
      return `${group}<a class="nav-link ${done.has(index) ? 'done' : ''}" href="#step-${index}"><span class="nav-number">${String(index + 1).padStart(2, '0')}</span><span>${escapeHtml(step.title)}</span></a>`;
    }).join('');
  }

  function renderSteps() {
    $('step-container').innerHTML = steps.map((step, index) => `<article class="step ${done.has(index) ? 'done' : ''}" id="step-${index}"><div class="step-index"><span>${String(index + 1).padStart(2, '0')}</span></div><div class="step-content"><p class="eyebrow">${escapeHtml(step.group)} · CHECKPOINT ${String(index + 1).padStart(2, '0')}</p><h2>${escapeHtml(step.title)}</h2><p class="step-summary">${escapeHtml(step.summary)}</p><div class="lesson-grid"><div class="lesson">${step.lesson}</div><div class="code-card"><div class="code-head"><span>${escapeHtml(step.codeTitle)}</span><button type="button" class="copy" data-copy="${index}">Copy</button></div><pre><code>${escapeHtml(step.code)}</code></pre></div></div><label class="checkpoint"><input type="checkbox" data-step="${index}" ${done.has(index) ? 'checked' : ''}><span><strong>Đánh dấu hoàn thành checkpoint</strong><small>${escapeHtml(step.check)}</small></span></label></div></article>`).join('');
  }

  function updateProgress() {
    $('done-count').textContent = `${done.size}/${steps.length}`;
    $('progress-bar').style.width = `${done.size / steps.length * 100}%`;
    localStorage.setItem(storageKey, JSON.stringify([...done]));
    renderNavigation($('search').value);
  }

  let toastTimer;
  function toast(message) {
    clearTimeout(toastTimer);
    $('toast').textContent = message;
    $('toast').hidden = false;
    toastTimer = setTimeout(() => $('toast').hidden = true, 1800);
  }

  async function copy(value) {
    try {
      await navigator.clipboard.writeText(value);
      toast('Đã copy command/code.');
    } catch {
      const field = document.createElement('textarea');
      field.value = value;
      field.style.position = 'fixed';
      field.style.left = '-9999px';
      document.body.appendChild(field);
      field.select();
      document.execCommand('copy');
      field.remove();
      toast('Đã copy.');
    }
  }

  renderSteps();
  $('acceptance').innerHTML = acceptance.map((item, index) => `<label><input type="checkbox" data-acceptance="${index}"><span>${escapeHtml(item)}</span></label>`).join('');
  updateProgress();
  document.addEventListener('change', event => {
    if (!event.target.matches('[data-step]')) return;
    const index = Number(event.target.dataset.step);
    event.target.checked ? done.add(index) : done.delete(index);
    document.getElementById(`step-${index}`).classList.toggle('done', event.target.checked);
    updateProgress();
  });
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-copy]');
    if (button) copy(steps[Number(button.dataset.copy)].code);
  });
  $('search').addEventListener('input', event => renderNavigation(event.target.value));
  $('theme-toggle').addEventListener('click', () => document.body.classList.toggle('dark'));

  const observer = new IntersectionObserver(entries => {
    const visible = entries.filter(entry => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
    if (!visible) return;
    document.querySelectorAll('.nav-link').forEach(link => link.classList.toggle('active', link.getAttribute('href') === `#${visible.target.id}`));
  }, {rootMargin: '-20% 0px -65%', threshold: [0, .25, .5]});
  document.querySelectorAll('.step').forEach(step => observer.observe(step));
})();
