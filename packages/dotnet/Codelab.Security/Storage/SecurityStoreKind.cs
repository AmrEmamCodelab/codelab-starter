namespace Codelab.Security.Storage;

/// <summary>Where a security store keeps its state.</summary>
public enum SecurityStoreKind
{
    /// <summary>In process; lost on restart and not shared between replicas.</summary>
    Memory = 0,

    /// <summary>SQL Server tables created by the shipped script (see <see cref="SecuritySqlSchema"/>).</summary>
    SqlServer = 1,
}
