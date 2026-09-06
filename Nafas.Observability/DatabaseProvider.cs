namespace Nafas.Observability
{
    /// <summary>
    /// Free-tier storage backends. Postgres/MySQL/Oracle/ClickHouse are Pro-tier,
    /// distributed later through a separate paid package feed (see ../CLAUDE.md's
    /// shared-conventions section) -- not implemented in this project at all, so
    /// they're deliberately not options here.
    /// </summary>
    public enum DatabaseProvider
    {
        /// <summary>
        /// Default. Zero-config -- a local file, no server to install or manage.
        /// </summary>
        Sqlite,

        /// <summary>
        /// The other free-tier option, for teams that already run SQL Server and
        /// would rather point at it than manage a SQLite file. Requires
        /// <see cref="NafasServerOptions.ConnectionString"/> (or
        /// <see cref="NafasServerOptions.ConnectionStringName"/>) and, optionally,
        /// <see cref="NafasServerOptions.Schema"/>.
        /// </summary>
        SqlServer,
    }
}
