export type Sample = { id: string; title: string; path: string; language: string; note: string; code: string; explain: string[] };
export const samples: Sample[] = [
  {
    id: 'store', title: '01 · Business service', language: 'C#',
    path: 'Dataverse.Plugin/FMCentralBms.Plugins/Services/PointIssueStore.cs',
    note: 'File mới do bạn tạo. Dùng SDK đang có, không thêm Newtonsoft.Json. Các giá trị Choice phải khớp schema ở bài 03.',
    code: `using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    internal sealed class PointIssueStore
    {
        private const string Table = "fmc_pointissue";
        private readonly IOrganizationService service;
        public PointIssueStore(IOrganizationService service)
        {
            this.service = service;
        }

        private static string Clean(string value, string field, int max, bool required)
        {
            value = (value ?? "").Trim();
            if ((required && value.Length == 0) || value.Length > max)
                throw new InvalidPluginExecutionException(field + " is invalid.");
            return value;
        }

        private static void CheckChoices(int priority, int status)
        {
            if (priority < 100000000 || priority > 100000002)
                throw new InvalidPluginExecutionException("Invalid priority.");
            if (status < 100000000 || status > 100000002)
                throw new InvalidPluginExecutionException("Invalid work status.");
        }

        public Entity Read(Guid id)
        {
            if (id == Guid.Empty)
                throw new InvalidPluginExecutionException("IssueId is required.");
            return service.Retrieve(Table, id, new ColumnSet(
                "fmc_name", "fmc_pointid", "fmc_description",
                "fmc_priority", "fmc_workstatus", "versionnumber"));
        }

        public Guid Create(Guid pointId, string title, string description, int priority)
        {
            if (pointId == Guid.Empty)
                throw new InvalidPluginExecutionException("PointId is required.");
            title = Clean(title, "Title", 200, true);
            description = Clean(description, "Description", 4000, false);
            CheckChoices(priority, 100000000);
            // Validate target existence and caller Read access.
            service.Retrieve("fmc_bmspoint", pointId,
                new ColumnSet("fmc_bmspointid"));
            var row = new Entity(Table);
            row["fmc_name"] = title;
            row["fmc_pointid"] = new EntityReference("fmc_bmspoint", pointId);
            row["fmc_description"] = description;
            row["fmc_priority"] = new OptionSetValue(priority);
            row["fmc_workstatus"] = new OptionSetValue(100000000); // Draft
            return service.Create(row);
        }

        private Entity Current(Guid id, string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(expectedVersion))
                throw new InvalidPluginExecutionException("Read this issue again before saving.");
            var row = Read(id);
            if (string.IsNullOrWhiteSpace(row.RowVersion) || row.RowVersion != expectedVersion)
                throw new InvalidPluginExecutionException("Issue changed. Refresh and try again.");
            return row;
        }

        public void Update(Guid id, string title, string description,
            int priority, int status, string expectedVersion)
        {
            Current(id, expectedVersion);
            CheckChoices(priority, status);
            var row = new Entity(Table, id) { RowVersion = expectedVersion };
            row["fmc_name"] = Clean(title, "Title", 200, true);
            row["fmc_description"] = Clean(description, "Description", 4000, false);
            row["fmc_priority"] = new OptionSetValue(priority);
            row["fmc_workstatus"] = new OptionSetValue(status);
            service.Execute(new UpdateRequest {
                Target = row,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }

        public void DeleteDraft(Guid id, string expectedVersion)
        {
            var row = Current(id, expectedVersion);
            if (row.GetAttributeValue<OptionSetValue>("fmc_workstatus")?.Value != 100000000)
                throw new InvalidPluginExecutionException("Only Draft issues can be deleted.");
            var target = new EntityReference(Table, id) { RowVersion = expectedVersion };
            service.Execute(new DeleteRequest {
                Target = target,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            });
        }
    }
}`,
    explain: [
      'Create ép trạng thái Draft ở server. Caller không thể gửi Open để bỏ qua bước tạo nháp.',
      'Read lấy RowVersion. Update/Delete vừa kiểm tra bản đang xem, vừa dùng IfRowVersionMatches để chặn race xảy ra sau Retrieve.',
      'PointId không nằm trong Update: phiếu đã tạo không tự chuyển sang point khác.',
      'Bài tập cho phép chuyển qua lại Draft/Open/Resolved. Nếu muốn cấm mở lại hoặc trả về Draft, cần state machine riêng; chưa được áp dụng ở đây.',
    ],
  },
  {
    id: 'plugin', title: '02 · Custom API handler', language: 'C#',
    path: 'Dataverse.Plugin/FMCentralBms.Plugins/Plugins/PointIssueApi.cs',
    note: 'Một Plugin Type xử lý 4 message; gán cùng type này cho 4 Custom API. Tái sử dụng PluginServices của repo.',
    code: `using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    public sealed class PointIssueApi : IPlugin
    {
        private static T Input<T>(IPluginExecutionContext context, string key)
        {
            if (!context.InputParameters.Contains(key) ||
                !(context.InputParameters[key] is T))
                throw new InvalidPluginExecutionException("Missing or invalid " + key);
            return (T)context.InputParameters[key];
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            PluginServices.ResolveWithFactory(serviceProvider,
                out var trace, out var context, out var factory);
            var store = new PointIssueStore(PluginServices.CreateOrgService(factory, context));
            trace?.Trace("PointIssue START {0} Correlation={1}",
                context.MessageName, context.CorrelationId);
            switch (context.MessageName)
            {
                case "fmc_CreatePointIssue":
                    context.OutputParameters["IssueId"] = store.Create(
                        Input<Guid>(context, "PointId"),
                        Input<string>(context, "Title"),
                        Input<string>(context, "Description"),
                        Input<int>(context, "Priority"));
                    break;
                case "fmc_GetPointIssue":
                    var row = store.Read(Input<Guid>(context, "IssueId"));
                    context.OutputParameters["IssueId"] = row.Id;
                    context.OutputParameters["PointId"] = row.GetAttributeValue<EntityReference>("fmc_pointid").Id;
                    context.OutputParameters["Title"] = row.GetAttributeValue<string>("fmc_name") ?? "";
                    context.OutputParameters["Description"] = row.GetAttributeValue<string>("fmc_description") ?? "";
                    context.OutputParameters["Priority"] = row.GetAttributeValue<OptionSetValue>("fmc_priority").Value;
                    context.OutputParameters["WorkStatus"] = row.GetAttributeValue<OptionSetValue>("fmc_workstatus").Value;
                    context.OutputParameters["RowVersion"] = row.RowVersion ?? "";
                    break;
                case "fmc_UpdatePointIssue":
                    store.Update(Input<Guid>(context, "IssueId"),
                        Input<string>(context, "Title"), Input<string>(context, "Description"),
                        Input<int>(context, "Priority"), Input<int>(context, "WorkStatus"),
                        Input<string>(context, "RowVersion"));
                    context.OutputParameters["Updated"] = true;
                    break;
                case "fmc_DeleteDraftPointIssue":
                    store.DeleteDraft(Input<Guid>(context, "IssueId"),
                        Input<string>(context, "RowVersion"));
                    context.OutputParameters["Deleted"] = true;
                    break;
                default:
                    throw new InvalidPluginExecutionException("Unsupported Point Issue message.");
            }
            trace?.Trace("PointIssue PASS {0}", context.MessageName);
        }
    }
}`,
    explain: ['InputParameters có tên và type khớp tuyệt đối với API contract, không phải tên column.', 'CreateOrgService dùng context.UserId: quyền của caller vẫn được Dataverse kiểm tra.', 'Không ghi toàn bộ Title/Description vào trace. Không dùng service SYSTEM để làm lỗi quyền “biến mất”.', 'Không catch rồi báo thành công. Lỗi SDK/validation phải đi về UI; bật trace tạm thời khi debug và hoàn nguyên cấu hình sau test.'],
  },
  {
    id: 'model', title: '03 · React model', language: 'TypeScript',
    path: 'dataverse/app-source/fmc-bms-demo/src/models/pointIssue.ts',
    note: 'Tên output API dùng PascalCase; tên column Dataverse dùng fmc_. Hai contract khác nhau.',
    code: `export type PointIssueInput = {
    PointId: string;
    Title: string;
    Description: string;
    Priority: number;
};
export type PointIssue = PointIssueInput & {
    IssueId: string;
    WorkStatus: number;
    RowVersion: string;
};
export type PointIssueUpdate = Omit<PointIssue, "PointId">;
export const ISSUE_DRAFT = 100000000;
export const ISSUE_OPEN = 100000001;
export const ISSUE_RESOLVED = 100000002;`,
    explain: ['Dùng named exports vì build.cjs gộp source của generative page.', 'RowVersion là string; không đổi sang Number vì đây là token so sánh phiên bản, không dùng để tính toán.'],
  },
  {
    id: 'api', title: '04 · React API service', language: 'TypeScript',
    path: 'dataverse/app-source/fmc-bms-demo/src/services/pointIssueService.ts',
    note: 'Chạy trong Dataverse host có Xrm, không chạy thật trong website docs localhost. Không nhúng access token/client secret.',
    code: `import type { PointIssue, PointIssueInput, PointIssueUpdate } from "../models/pointIssue";

type PointIssueXrm = {
    Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } };
};
function pointIssueRoot(): string {
    const xrm = (window as unknown as { Xrm?: PointIssueXrm }).Xrm;
    const org = xrm?.Utility?.getGlobalContext?.().getClientUrl?.();
    if (!org) throw new Error("Open this page inside the Dataverse app.");
    return org.replace(/\\/$/, "") + "/api/data/v9.2/";
}
function pointIssueGuid(value: string): string {
    const clean = value.replace(/[{}]/g, "");
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(clean))
        throw new Error("Invalid record ID.");
    return clean;
}
async function callPointIssue<T>(path: string, body?: object): Promise<T> {
    const response = await fetch(pointIssueRoot() + path, {
        method: body === undefined ? "GET" : "POST",
        credentials: "same-origin",
        headers: {
            Accept: "application/json", "Content-Type": "application/json",
            "OData-MaxVersion": "4.0", "OData-Version": "4.0"
        },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    const text = await response.text();
    let result: any;
    try { result = text ? JSON.parse(text) : {}; }
    catch { throw new Error("Unexpected Dataverse response (HTTP " + response.status + ")."); }
    if (!response.ok) throw new Error(result.error?.message || "HTTP " + response.status);
    return result as T;
}
export function createPointIssue(input: PointIssueInput) {
    return callPointIssue<{ IssueId: string }>("fmc_CreatePointIssue", {
        ...input, PointId: pointIssueGuid(input.PointId)
    });
}
export function getPointIssue(id: string) {
    return callPointIssue<PointIssue>("fmc_GetPointIssue(IssueId=" + pointIssueGuid(id) + ")");
}
export function updatePointIssue(input: PointIssueUpdate) {
    return callPointIssue<{ Updated: boolean }>("fmc_UpdatePointIssue", {
        ...input, IssueId: pointIssueGuid(input.IssueId)
    });
}
export function deleteDraftPointIssue(id: string, rowVersion: string) {
    return callPointIssue<{ Deleted: boolean }>("fmc_DeleteDraftPointIssue", {
        IssueId: pointIssueGuid(id), RowVersion: rowVersion
    });
}`,
    explain: ['C/U/D gửi POST; Get gửi GET và Guid không có dấu nháy đơn trong OData v4.', 'Cùng pattern host như spoChangeService.ts hiện có. Nếu host không expose Xrm, cần giải quyết host integration, không hard-code URL/token.', 'Khi lỗi mạng sau Create, chưa biết server đã tạo hay chưa. Không tự retry Create: kiểm tra danh sách trước; production nên thêm ClientRequestId và alternate key cho idempotency.'],
  },
  {
    id: 'hook', title: '05 · React hook', language: 'TypeScript',
    path: 'dataverse/app-source/fmc-bms-demo/src/hooks/usePointIssue.ts',
    note: 'Bản học tối thiểu: thao tác trên một issue; giữ lỗi refresh tách khỏi thông báo mutation để tránh gửi Create lại.',
    code: `import { useRef, useState } from "react";
import type { PointIssue, PointIssueInput, PointIssueUpdate } from "../models/pointIssue";
import { createPointIssue, getPointIssue, updatePointIssue, deleteDraftPointIssue } from "../services/pointIssueService";

export function usePointIssue() {
    const [issue, setIssue] = useState<PointIssue | null>(null);
    const [busy, setBusy] = useState(false);
    const [message, setMessage] = useState("");
    const [error, setError] = useState("");
    const lock = useRef(false);
    async function run(work: () => Promise<void>) {
        if (lock.current) return;
        lock.current = true; setBusy(true); setError(""); setMessage("");
        try { await work(); }
        catch (e) { setError(e instanceof Error ? e.message : "Request failed."); }
        finally { lock.current = false; setBusy(false); }
    }
    async function reload(id: string) { setIssue(await getPointIssue(id)); }
    async function reloadAfterWrite(id: string) {
        setIssue(null); // Không cho sửa bằng RowVersion cũ nếu refresh thất bại.
        try { await reload(id); }
        catch { setError("Đã ghi dữ liệu nhưng đọc lại lỗi. Mở lại IssueId: " + id); }
    }
    return {
        issue, busy, message, error,
        read: (id: string) => run(async () => { setIssue(null); await reload(id); }),
        create: (input: PointIssueInput) => run(async () => {
            const created = await createPointIssue(input);
            setMessage("Đã tạo: " + created.IssueId);
            await reloadAfterWrite(created.IssueId);
        }),
        update: (input: PointIssueUpdate) => run(async () => {
            const result = await updatePointIssue(input);
            if (!result.Updated) throw new Error("Update was not confirmed.");
            setMessage("Đã lưu issue."); await reloadAfterWrite(input.IssueId);
        }),
        remove: () => run(async () => {
            if (!issue) return;
            const result = await deleteDraftPointIssue(issue.IssueId, issue.RowVersion);
            if (!result.Deleted) throw new Error("Delete was not confirmed.");
            setIssue(null); setMessage("Đã xóa bản nháp.");
        })
    };
}`,
    explain: ['useRef khóa đồng bộ trước khi render lại: double click không gửi hai request cùng lúc.', 'Sau mutation đọc lại để lấy RowVersion mới. Nếu chỉ refresh lỗi, thông báo rõ dữ liệu đã được ghi.', 'Đây là single-record hook; danh sách/paging và hủy request khi đổi route là phần mở rộng khi đưa vào UI hoàn chỉnh.'],
  },
  {
    id: 'component', title: '06 · React form CRUD', language: 'TSX',
    path: 'dataverse/app-source/fmc-bms-demo/src/components/PointIssuePanel.tsx',
    note: 'Form học CRUD có đủ C/R/U/D. Sau khi chạy đúng, thay HTML controls bằng Fluent UI của app và bản dịch theo useLanguage.',
    code: `import { useEffect, useState } from "react";
import { usePointIssue } from "../hooks/usePointIssue";
import { ISSUE_DRAFT, ISSUE_OPEN, ISSUE_RESOLVED } from "../models/pointIssue";

export function PointIssuePanel({ pointId }: { pointId: string }) {
    const api = usePointIssue();
    const [lookupId, setLookupId] = useState("");
    const [title, setTitle] = useState("");
    const [description, setDescription] = useState("");
    const [priority, setPriority] = useState(100000000);
    const [workStatus, setWorkStatus] = useState(ISSUE_DRAFT);
    useEffect(() => {
        if (!api.issue) return;
        setLookupId(api.issue.IssueId); setTitle(api.issue.Title);
        setDescription(api.issue.Description); setPriority(api.issue.Priority);
        setWorkStatus(api.issue.WorkStatus);
    }, [api.issue]);
    const valid = title.trim().length > 0 && title.trim().length <= 200;
    return <section aria-label="Point Issue">
        <h2>Point Issue</h2>
        <p>Point tạo mới: {pointId}</p>
        <label>Issue ID <input value={lookupId}
            onChange={e => setLookupId(e.target.value)} /></label>
        <button disabled={api.busy || !lookupId} onClick={() => api.read(lookupId)}>Đọc issue</button>
        <label>Tiêu đề <input maxLength={200} value={title}
            onChange={e => setTitle(e.target.value)} /></label>
        <label>Mô tả <textarea maxLength={4000} value={description}
            onChange={e => setDescription(e.target.value)} /></label>
        <label>Ưu tiên <select value={priority} onChange={e => setPriority(Number(e.target.value))}>
            <option value={100000000}>Low</option><option value={100000001}>Normal</option>
            <option value={100000002}>High</option>
        </select></label>
        <label>Trạng thái <select value={workStatus} onChange={e => setWorkStatus(Number(e.target.value))}>
            <option value={ISSUE_DRAFT}>Draft</option><option value={ISSUE_OPEN}>Open</option>
            <option value={ISSUE_RESOLVED}>Resolved</option>
        </select></label>
        <button disabled={api.busy || !valid || !pointId}
            onClick={() => api.create({ PointId: pointId, Title: title, Description: description, Priority: priority })}>
            Tạo mới (Draft)
        </button>
        <button disabled={api.busy || !valid || !api.issue}
            onClick={() => api.issue && api.update({
                IssueId: api.issue.IssueId, RowVersion: api.issue.RowVersion,
                Title: title, Description: description, Priority: priority, WorkStatus: workStatus
            })}>Lưu issue đang đọc</button>
        <button disabled={api.busy || api.issue?.WorkStatus !== ISSUE_DRAFT}
            onClick={() => { if (window.confirm("Xóa vĩnh viễn bản nháp này?")) void api.remove(); }}>
            Xóa bản nháp
        </button>
        {api.issue && <p>Đang đọc {api.issue.IssueId} · Point {api.issue.PointId}</p>}
        <p role="status">{api.busy ? "Đang xử lý…" : api.message}</p>
        {api.error && <p role="alert">{api.error}</p>}
    </section>;
}`,
    explain: ['Create luôn tạo phiếu mới, không sửa phiếu đang đọc. Tên nút nói rõ điều này.', 'Delete dùng trạng thái đã đọc từ server, không dựa vào dropdown chưa Save. Backend vẫn kiểm tra lại.', 'Issue ID thủ công giúp test API Read trước. Bước UI tiếp theo: chọn row từ danh sách issue để gọi read(id).', 'Ở Dashboard, truyền GUID fmc_bmspointid của point được chọn, không truyền ObjectId hoặc EquipmentCode.'],
  },
  {
    id: 'integrate', title: '07 · Gắn vào Dashboard', language: 'TSX · đoạn chèn',
    path: 'dataverse/app-source/fmc-bms-demo/src/Dashboard.tsx',
    note: 'Đoạn chèn vào component hiện có, không thay cả file. GUID phải đến từ record thật mà user đã chọn.',
    code: `// Thêm named import:
import { PointIssuePanel } from "./components/PointIssuePanel";

// Trong GeneratedComponent, cạnh các useState đang có:
const [issuePointId, setIssuePointId] = useState<string | null>(null);

// Trong cell/action của row point hiện có (row là ReadablePoint):
<Button onClick={() => setIssuePointId(row.fmc_bmspointid)}>
    Ghi nhận sự cố
</Button>

// Trong JSX của Dashboard, ngoài DataGrid, tại vị trí panel mong muốn:
{issuePointId && <PointIssuePanel key={issuePointId} pointId={issuePointId} />}

// Tùy chọn danh sách: sau khi thêm table vào data sources và generate RuntimeTypes:
// dataApi.queryTable("fmc_pointissue", {
//   select: ["fmc_pointissueid", "fmc_name", "fmc_workstatus"],
//   orderBy: "createdon desc", pageSize: 25
// });`,
    explain: ['Đổi tên row theo biến đang dùng trong renderCell của bạn. Đây là fragment, không phải file thay thế.', 'key={issuePointId} reset form khi đổi point. Không sửa trực tiếp trung-tam-van-hanh.tsx vì đó là output build.', 'Query list dùng dataApi, mở chi tiết dùng fmc_GetPointIssue: tách read model của grid khỏi nghiệp vụ Custom API.', 'Không lọc client một trang rồi gọi là tất cả issue của point. Thêm filter/paging theo khả năng host đã kiểm chứng hoặc query phía server.'],
  },
];
