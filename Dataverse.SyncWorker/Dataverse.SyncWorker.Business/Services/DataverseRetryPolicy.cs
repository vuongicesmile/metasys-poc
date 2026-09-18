using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Services;

public static class DataverseRetryPolicy
{
    public static bool IsTransient(Exception exception) => exception is HttpRequestException or TimeoutException ||
        exception is FaultException<OrganizationServiceFault> fault &&
        (fault.Detail.ErrorDetails.Contains("Retry-After") ||
         fault.Detail.ErrorCode is -2147015902 or -2147015903 or -2147015898);
}
