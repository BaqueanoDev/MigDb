using MigDb.Core.Entities;

namespace MigDb.Core.Migration.Revert;

public sealed record ProgrammableRewind(SchemaProgrammableEntity Programmable, byte[]? PreviousHash)
{
    public bool IsDrop => PreviousHash is null;
}
