using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dataverse.SyncWorker.DataAccess.Services
{
    public sealed class FmRequestService
    {
        private readonly DataverseConnection connection;

        public FmRequestService(DataverseConnection connection)
        {
            this.connection = connection;
        }

        public Guid CreateFmRequest(string name, string description)
        {
            var service = connection.Get();

            var request = new Entity(DataverseSchema.FmRequest.TableLogicalName);

            request[
                DataverseSchema.FmRequest.PrimaryNameLogicalName
            ] = name;

            request[
                DataverseSchema.FmRequest.DescriptionLogicalName
            ] = description;

            request[
                DataverseSchema.FmRequest.StatusLogicalName
            ] = new OptionSetValue(
                DataverseSchema.FmRequestStatus.Draft
            );

            return service.Create(request);
        }

        public void SubmitFmRequest(Guid requestId)
        {
            var service = connection.Get();

            // Lấy request hiện tại để check status
            var request = service.Retrieve(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId,
                new ColumnSet(
                    DataverseSchema.FmRequest.StatusLogicalName
                )
            );

            var status = request.GetAttributeValue<OptionSetValue>(
                DataverseSchema.FmRequest.StatusLogicalName
            );

            // Chỉ Draft mới được Submit
            if (status?.Value != DataverseSchema.FmRequestStatus.Draft)
            {
                throw new InvalidOperationException(
                    "Only Draft requests can be submitted."
                );
            }

            // Chỉ update field cần thay đổi
            var update = new Entity(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId
            );

            update[
                DataverseSchema.FmRequest.StatusLogicalName
            ] = new OptionSetValue(
                DataverseSchema.FmRequestStatus.Submitted
            );

            service.Update(update);
        }


        public void ApproveFmRequest(Guid requestId)
        {
            var service = connection.Get();

            var request = service.Retrieve(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId,
                new ColumnSet(
                    DataverseSchema.FmRequest.StatusLogicalName
                )
            );

            var status = request.GetAttributeValue<OptionSetValue>(
                DataverseSchema.FmRequest.StatusLogicalName
            );

            if (status?.Value !=
                DataverseSchema.FmRequestStatus.Submitted)
            {
                throw new InvalidOperationException(
                    "Only Submitted requests can be approved."
                );
            }

            var update = new Entity(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId
            );

            update[
                DataverseSchema.FmRequest.StatusLogicalName
            ] = new OptionSetValue(
                DataverseSchema.FmRequestStatus.Approved
            );

            service.Update(update);
        }

        public void RejectFmRequest(Guid requestId)
        {
            var service = connection.Get();

            var request = service.Retrieve(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId,
                new ColumnSet(
                    DataverseSchema.FmRequest.StatusLogicalName
                )
            );

            var status = request.GetAttributeValue<OptionSetValue>(
                DataverseSchema.FmRequest.StatusLogicalName
            );

            if (status?.Value !=
                DataverseSchema.FmRequestStatus.Submitted)
            {
                throw new InvalidOperationException(
                    "Only Submitted requests can be rejected."
                );
            }

            var update = new Entity(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId
            );

            update[
                DataverseSchema.FmRequest.StatusLogicalName
            ] = new OptionSetValue(
                DataverseSchema.FmRequestStatus.Rejected
            );

            service.Update(update);
        }

        public void UpdateDraftFmRequest(
            Guid requestId,
            string name,
            string description)
        {
            var service = connection.Get();

            var request = service.Retrieve(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId,
                new ColumnSet(
                    DataverseSchema.FmRequest.StatusLogicalName
                )
            );

            var status = request.GetAttributeValue<OptionSetValue>(
                DataverseSchema.FmRequest.StatusLogicalName
            );

            if (status?.Value != DataverseSchema.FmRequestStatus.Draft)
            {
                throw new InvalidOperationException(
                    "Only Draft requests can be updated."
                );
            }

            var update = new Entity(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId
            );

            update[
                DataverseSchema.FmRequest.PrimaryNameLogicalName
            ] = name;

            update[
                DataverseSchema.FmRequest.DescriptionLogicalName
            ] = description;

            service.Update(update);
        }

        public void DeleteDraftFmRequest(Guid requestId)
        {
            var service = connection.Get();

            var request = service.Retrieve(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId,
                new ColumnSet(
                    DataverseSchema.FmRequest.StatusLogicalName
                )
            );

            var status = request.GetAttributeValue<OptionSetValue>(
                DataverseSchema.FmRequest.StatusLogicalName
            );

            if (status?.Value != DataverseSchema.FmRequestStatus.Draft)
            {
                throw new InvalidOperationException(
                    "Only Draft requests can be deleted."
                );
            }

            service.Delete(
                DataverseSchema.FmRequest.TableLogicalName,
                requestId
            );
        }


    }

        

}
