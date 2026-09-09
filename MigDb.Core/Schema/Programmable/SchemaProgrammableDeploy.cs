using MigDb.Core.Entities;

namespace MigDb.Core.Schema.Programmable;

public sealed record SchemaProgrammableDeploy(SchemaProgrammableEntity Programmable, MigrationProgrammableEntity Journal);
