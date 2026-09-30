using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;


namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    private void RunMigrations(IOrganizationService service)
    {
        // Each version is checked independently so an applied 001 does not skip 002.
        RunMigration(service, "001", "Initial migration", ApplyMigration001);
        RunMigration(service, "002", "Second migration", ApplyMigration002);
        RunMigration(service, "003", "Create FM Request table", ApplyMigration003);
        RunMigration(service, "004", "FM workflow lifecycle, routing and audit", ApplyMigration004);
        RunMigration(service, "005", "FM overdue notification before delayed escalation", ApplyMigration005);
    }

    private static void RunMigration(IOrganizationService service, string version, string name,
        Action<IOrganizationService> apply)
    {
        var stage = "CHECK";
        try
        {
            Console.WriteLine($"[migration {version}] CHECK: looking for an applied receipt.");
            if (MigrationAlreadyApplied(service, version))
            {
                Console.WriteLine($"[migration {version}] SKIP: already applied.");
                return;
            }

            stage = "APPLY";
            Console.WriteLine($"[migration {version}] APPLY: {name}.");
            apply(service);

            stage = "RECORD";
            Console.WriteLine($"[migration {version}] RECORD: writing the applied receipt.");
            var receiptId = RecordMigration(service, version, name);
            Console.WriteLine($"[migration {version}] DONE: receipt {receiptId}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[migration {version}] FAIL at {stage}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private static bool MigrationAlreadyApplied(IOrganizationService service, string version)
    {
        var query = new QueryExpression(DataverseSchema.SchemaMigration.TableLogicalName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1
        };
        query.Criteria.AddCondition(DataverseSchema.SchemaMigration.VersionLogicalName,
            ConditionOperator.Equal, version);
        return service.RetrieveMultiple(query).Entities.Count > 0;
    }

    private static void ApplyMigration001(IOrganizationService service)
    {
        // Baseline migration: successful completion requires no schema writes.
        Console.WriteLine("[migration 001] Baseline migration; no schema actions.");
    }

    private static void ApplyMigration002(IOrganizationService service)
    {
        // Placeholder: no schema change has been defined for this version.
        Console.WriteLine("[migration 002] No schema actions are defined yet.");
    }

    private void ApplyMigration003(
        IOrganizationService service)
    {
        const string table =
            DataverseSchema.FmRequest.TableLogicalName;

        // 1. Tạo table + primary name nếu table chưa có
        if (!TableExists(service, table))
        {
            var tableMetadata = new EntityMetadata
            {
                SchemaName =
                    DataverseSchema.FmRequest.TableSchemaName,

                DisplayName =
                    new Label("FM Request", 1033),

                DisplayCollectionName =
                    new Label("FM Requests", 1033),

                OwnershipType =
                    OwnershipTypes.UserOwned,

                IsActivity = false
            };

            var primaryName = new StringAttributeMetadata
            {
                SchemaName =
                    DataverseSchema.FmRequest.PrimaryNameSchemaName,

                DisplayName =
                    new Label("Name", 1033),

                RequiredLevel =
                    new AttributeRequiredLevelManagedProperty(
                        AttributeRequiredLevel.ApplicationRequired
                    ),

                MaxLength = 200
            };

            service.Execute(new CreateEntityRequest
            {
                SolutionUniqueName = Solution,
                Entity = tableMetadata,
                PrimaryAttribute = primaryName
            });

            Console.WriteLine(
                "[migration 003] Created FM Request table."
            );
        }

        // 2. Description
        if (!ColumnExists(
            service,
            table,
            DataverseSchema.FmRequest.DescriptionLogicalName))
        {
            service.Execute(new CreateAttributeRequest
            {
                EntityName = table,
                SolutionUniqueName = Solution,

                Attribute = new MemoAttributeMetadata
                {
                    SchemaName =
                        DataverseSchema.FmRequest.DescriptionSchemaName,

                    DisplayName =
                        new Label("Description", 1033),

                    MaxLength = 4000,

                    RequiredLevel =
                        new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.None
                        )
                }
            });

            Console.WriteLine(
                "[migration 003] Created Description column."
            );
        }

        // 3. Status choice
        if (!ColumnExists(
            service,
            table,
            DataverseSchema.FmRequest.StatusLogicalName))
        {
            var statusOptions = new OptionSetMetadata
            {
                IsGlobal = false,
                OptionSetType = OptionSetType.Picklist
            };

            statusOptions.Options.Add(
                new OptionMetadata(
                    new Label("Draft", 1033),
                    DataverseSchema.FmRequestStatus.Draft
                )
            );

            statusOptions.Options.Add(
                new OptionMetadata(
                    new Label("Submitted", 1033),
                    DataverseSchema.FmRequestStatus.Submitted
                )
            );

            statusOptions.Options.Add(
                new OptionMetadata(
                    new Label("Approved", 1033),
                    DataverseSchema.FmRequestStatus.Approved
                )
            );

            statusOptions.Options.Add(
                new OptionMetadata(
                    new Label("Rejected", 1033),
                    DataverseSchema.FmRequestStatus.Rejected
                )
            );

            service.Execute(new CreateAttributeRequest
            {
                EntityName = table,
                SolutionUniqueName = Solution,

                Attribute = new PicklistAttributeMetadata
                {
                    SchemaName =
                        DataverseSchema.FmRequest.StatusSchemaName,

                    DisplayName =
                        new Label("Status", 1033),

                    RequiredLevel =
                        new AttributeRequiredLevelManagedProperty(
                            AttributeRequiredLevel.ApplicationRequired
                        ),

                    OptionSet = statusOptions
                }
            });

            Console.WriteLine(
                "[migration 003] Created Status column."
            );
        }

        Console.WriteLine(
            "[migration 003] FM Request schema ready."
        );
    }

    private static Guid RecordMigration(IOrganizationService service, string version, string name)
    {
        var migration = new Entity(DataverseSchema.SchemaMigration.TableLogicalName)
        {
            [DataverseSchema.SchemaMigration.PrimaryNameLogicalName] = name,
            [DataverseSchema.SchemaMigration.VersionLogicalName] = version,
            [DataverseSchema.SchemaMigration.AppliedOnLogicalName] = DateTime.UtcNow
        };
        return service.Create(migration);
    }
}
