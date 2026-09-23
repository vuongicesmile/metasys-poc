using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public sealed partial class DataverseProvisioner
{
    private const string BuildingCodeColumn = "fmc_buildingcode";

    /// <summary>Adds the normalized code columns and fills them only from parent lookups.</summary>
    public async Task ProvisionBuildingCode()
    {
        var client = connection.Get(); // Enforces ExpectedOrganizationId before any write.
        await ReadBmsRelationMetadata(false); // Verify the existing solution, publisher and lookup contract first.
        foreach (var table in new[] { EquipmentTable, PointTable })
        {
            var metadata = await RelationMetadata(table);
            var existing = metadata.Attributes.SingleOrDefault(a => a.LogicalName == BuildingCodeColumn);
            if (existing is null)
            {
                await client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = table,
                    Attribute = new StringAttributeMetadata
                    {
                        SchemaName = BuildingCodeColumn,
                        DisplayName = new Label("Building Code", 1033),
                        Description = new Label("Normalized code resolved from the related BMS Building record.", 1033),
                        MaxLength = 50,
                        FormatName = StringFormatName.Text,
                        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                    },
                    SolutionUniqueName = Solution
                });
                Console.WriteLine($"Created {table}.{BuildingCodeColumn}.");
            }
            else ValidateBuildingCodeColumn(table, existing);
            metadata = await RelationMetadata(table);
            ValidateBuildingCodeColumn(table, metadata.Attributes.Single(a => a.LogicalName == BuildingCodeColumn));
            await AddRelationComponent(metadata.MetadataId!.Value, 1);
        }

        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>fmc_bmsequipment</entity><entity>fmc_bmspoint</entity></entities></importexportxml>"
        });
        await ConfigureBmsRelationUi();
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>fmc_bmsequipment</entity><entity>fmc_bmspoint</entity></entities></importexportxml>"
        });
        await VerifyBmsRelationUi();

        var equipment = await BackfillEquipmentBuildingCode();
        var points = await BackfillPointBuildingCode();
        var result = new
        {
            observedAtUtc = DateTime.UtcNow,
            organizationId = options.ExpectedOrganizationId,
            solution = Solution,
            column = BuildingCodeColumn,
            equipment,
            points,
            legacyBuildingTextPreserved = true
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ValidateBuildingCodeColumn(string table, AttributeMetadata attribute)
    {
        if (attribute is not StringAttributeMetadata text || text.MaxLength != 50)
            throw new InvalidOperationException($"{table}.{BuildingCodeColumn} must be a 50-character text column; found {attribute.AttributeTypeName?.Value}.");
    }

    private async Task<object> BackfillEquipmentBuildingCode()
    {
        Console.WriteLine("Backfilling Equipment Building Code from fmc_buildingid...");
        var query = new QueryExpression(EquipmentTable)
        {
            ColumnSet = new ColumnSet(BuildingCodeColumn, "versionnumber"),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
        };
        var building = query.AddLink(BuildingTable, "fmc_buildingid", "fmc_bmsbuildingid", JoinOperator.Inner);
        building.EntityAlias = "parentbuilding";
        building.Columns = new ColumnSet("fmc_buildingcode");
        return await BackfillBuildingCode(query, EquipmentTable, "parentbuilding.fmc_buildingcode");
    }

    private async Task<object> BackfillPointBuildingCode()
    {
        Console.WriteLine("Backfilling Point Building Code from Equipment -> Building lookups...");
        var query = new QueryExpression(PointTable)
        {
            ColumnSet = new ColumnSet(BuildingCodeColumn, "versionnumber"),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
        };
        var equipment = query.AddLink(EquipmentTable, "fmc_equipmentid", "fmc_bmsequipmentid", JoinOperator.Inner);
        equipment.EntityAlias = "parentEquipment";
        equipment.Columns = new ColumnSet("fmc_buildingid");
        var building = equipment.AddLink(BuildingTable, "fmc_buildingid", "fmc_bmsbuildingid", JoinOperator.Inner);
        building.EntityAlias = "parentbuilding";
        building.Columns = new ColumnSet("fmc_buildingcode");
        return await BackfillBuildingCode(query, PointTable, "parentbuilding.fmc_buildingcode");
    }

    private async Task<object> BackfillBuildingCode(QueryExpression query, string table, string buildingCodeAlias)
    {
        var client = connection.Get();
        var scanned = 0;
        var updated = 0;
        var unchanged = 0;
        var unresolved = 0;
        while (true)
        {
            var page = await client.RetrieveMultipleAsync(query);
            var pageUpdated = 0;
            foreach (var row in page.Entities)
            {
                scanned++;
                var code = row.GetAttributeValue<AliasedValue>(buildingCodeAlias)?.Value as string;
                if (string.IsNullOrWhiteSpace(code))
                {
                    unresolved++;
                    continue;
                }
                if (string.Equals(row.GetAttributeValue<string>(BuildingCodeColumn), code, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }
                var rowVersion = row.RowVersion ?? Convert.ToString(row.GetAttributeValue<long?>("versionnumber"), System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(rowVersion))
                    throw new InvalidOperationException($"Missing row version for {table}/{row.Id}; refusing an unguarded migration update.");
                await client.ExecuteAsync(new UpdateRequest
                {
                    Target = new Entity(table, row.Id)
                    {
                        RowVersion = rowVersion,
                        [BuildingCodeColumn] = code
                    },
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                });
                updated++;
                pageUpdated++;
            }
            Console.WriteLine($"{table}: scanned {scanned}, updated {updated}, unchanged {unchanged}, unresolved {unresolved} (page updated {pageUpdated}).");
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        return new { scanned, updated, unchanged, unresolved };
    }
}
