using Microsoft.SqlServer.Dac;

namespace MigDb.Core.Schema;

/// <summary>
/// The DacFx object type split migdb works in: programmables (source folder
/// authoritative, deployed through the programmable pipeline) and structure
/// (tables and everything else, deployed through migrations).
/// Shared by anything that scopes a compare or a publish.
/// </summary>
public static class SchemaObjectTypes
{
    /// <summary>
    /// Procedures, views, functions and triggers
    /// </summary>
    public static readonly ObjectType[] Programmable =
    [
        ObjectType.StoredProcedures,
        ObjectType.Views,
        ObjectType.ScalarValuedFunctions,
        ObjectType.TableValuedFunctions,
        ObjectType.DatabaseTriggers,
        ObjectType.ServerTriggers,
    ];

    /// <summary>
    /// Everything that is not a programmable
    /// </summary>
    public static readonly ObjectType[] Structure = [.. Enum.GetValues<ObjectType>().Except(Programmable)];

    /// <summary>
    /// Nothing excluded - the whole model
    /// </summary>
    public static readonly ObjectType[] None = [];

    public static readonly ObjectType[] Infrastructure =
    [
        ObjectType.DatabaseOptions,
        ObjectType.MasterKeys,
        ObjectType.DatabaseEncryptionKeys,
        ObjectType.Files,
        ObjectType.Filegroups,
        ObjectType.EventSessions,
        ObjectType.EventNotifications,
        ObjectType.Routes,
        ObjectType.Services,
        ObjectType.Queues,
        ObjectType.Contracts,
        ObjectType.MessageTypes,
        ObjectType.BrokerPriorities,
        ObjectType.RemoteServiceBindings,
        ObjectType.ErrorMessages,
    ];

    public static readonly ObjectType[] Security =
    [
        ObjectType.Users,
        ObjectType.Logins,
        ObjectType.Permissions,
        ObjectType.RoleMembership,
        ObjectType.DatabaseRoles,
        ObjectType.ApplicationRoles,
        ObjectType.ServerRoles,
        ObjectType.ServerRoleMembership,
        ObjectType.Credentials,
        ObjectType.DatabaseScopedCredentials,
        ObjectType.LinkedServerLogins,
        ObjectType.Audits,
        ObjectType.DatabaseAuditSpecifications,
        ObjectType.ServerAuditSpecifications,
    ];

    public static readonly ObjectType[] Unmanaged = [.. Security, .. Infrastructure];

    /// <summary>
    /// The object types to exclude so only <paramref name="scope"/> is left in
    /// </summary>
    /// <param name="scope">The scope to keep</param>
    /// <returns>Object types to exclude</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scope"/> is an unrecognised scope</exception>
    public static ObjectType[] ExcludedFor(SchemaDeployScope scope) => scope switch
    {
        SchemaDeployScope.All => None,
        SchemaDeployScope.Schema => Programmable,
        SchemaDeployScope.Programmables => Structure,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown deploy scope"),
    };
}
