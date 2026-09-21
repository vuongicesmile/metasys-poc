using System.ServiceModel;
using DataverseSyncWorker.Abstractions;
using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Services;

/// <summary>Phân loại lỗi Dataverse SDK tại boundary DataAccess.</summary>
public sealed class DataverseFailureClassifier : IIntegrationFailureClassifier
{
    // Timeout, lỗi mạng và throttling là lỗi tạm thời nên worker có thể retry.
    public bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException ||
        exception is FaultException<OrganizationServiceFault> fault &&
        (fault.Detail.ErrorDetails.Contains("Retry-After") ||
         fault.Detail.ErrorCode is -2147015902 or -2147015903 or -2147015898);

    // Fault SDK không thuộc nhóm transient thường là lỗi schema, quyền hoặc request không hợp lệ.
    public bool IsPermanent(Exception exception) =>
        exception is FaultException<OrganizationServiceFault> && !IsTransient(exception);
}
