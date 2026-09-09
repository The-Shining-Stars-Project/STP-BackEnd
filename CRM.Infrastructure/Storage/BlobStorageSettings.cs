namespace CRM.Infrastructure.Storage;

/// <summary>
/// Bound from the "BlobStorage" section of configuration. Both credentials are optional and
/// the section may be absent entirely: the API then registers <see cref="UnconfiguredFileStorage"/>
/// and boots normally, which is what lets the code ship before the storage account exists.
/// </summary>
public class BlobStorageSettings
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// https://&lt;account&gt;.blob.core.windows.net — the preferred way to point at the account.
    /// The API authenticates with its App Service managed identity (DefaultAzureCredential),
    /// so there is no key to store or rotate. The identity needs the "Storage Blob Data
    /// Contributor" role on the account. Azure setting: BlobStorage__AccountUrl.
    /// </summary>
    public string? AccountUrl { get; set; }

    /// <summary>
    /// Alternative to AccountUrl: the account's full connection string. It embeds the account
    /// key, so it is a secret — an App Service setting (BlobStorage__ConnectionString) or local
    /// user secrets, never a committed file. Wins over AccountUrl when both are set, so a
    /// developer can point local runs at Azurite ("UseDevelopmentStorage=true") without
    /// touching the deployed configuration.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The one container that holds every file the CRM stores; files are namespaced by blob
    /// name prefix ("scripts/...") rather than by container. Created on first upload if missing.
    /// </summary>
    public string ContainerName { get; set; } = "crm-files";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) || !string.IsNullOrWhiteSpace(AccountUrl);
}
