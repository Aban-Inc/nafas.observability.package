using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace Nafas.Observability
{
    /// <summary>
    /// Resolves the actual connection string from a <see cref="NafasServerOptions"/>
    /// instance, in priority order:
    ///   1. <see cref="NafasServerOptions.ConnectionString"/>, if set directly.
    ///   2. <see cref="NafasServerOptions.ConnectionStringName"/>, looked up via
    ///      <see cref="IConfiguration.GetConnectionString"/> -- appsettings.json's
    ///      "ConnectionStrings" section, the standard ASP.NET Core convention.
    ///   3. The same name, looked up via classic
    ///      <see cref="ConfigurationManager.ConnectionStrings"/> -- web.config's
    ///      &lt;connectionStrings&gt; section. IConfiguration never sees this on
    ///      its own; apps still on (or bridging from) .NET Framework/System.Web
    ///      commonly keep connection strings there instead of appsettings.json.
    ///   4. For <see cref="DatabaseProvider.Sqlite"/> only, a default local file
    ///      ("Data Source=nafas.db") -- SQLite needs no server to reach, so this
    ///      is the one provider that can have a real zero-config default.
    /// </summary>
    internal static class ConnectionStringResolver
    {
        private const string DefaultSqliteConnectionString = "Data Source=nafas.db";

        public static string Resolve(NafasServerOptions options, IConfiguration? configuration)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return options.ConnectionString!;
            }

            if (!string.IsNullOrWhiteSpace(options.ConnectionStringName))
            {
                var fromConfiguration = configuration?.GetConnectionString(options.ConnectionStringName!);
                if (!string.IsNullOrWhiteSpace(fromConfiguration))
                {
                    return fromConfiguration!;
                }

                // ConfigurationManager reads app.config/web.config on .NET
                // Framework; on modern .NET (no such file) this call is safe
                // and simply returns an empty collection, not an exception --
                // falls through to the error below like any other miss.
                var fromWebConfig = ConfigurationManager.ConnectionStrings[options.ConnectionStringName!];
                if (fromWebConfig != null && !string.IsNullOrWhiteSpace(fromWebConfig.ConnectionString))
                {
                    return fromWebConfig.ConnectionString;
                }

                throw new InvalidOperationException(
                    $"NafasServerOptions.ConnectionStringName was set to \"{options.ConnectionStringName}\", " +
                    "but no connection string by that name was found in IConfiguration's ConnectionStrings " +
                    "section (appsettings.json) or in web.config/app.config's <connectionStrings>. " +
                    "Set NafasServerOptions.ConnectionString directly instead, or add the missing entry.");
            }

            if (options.Provider == DatabaseProvider.Sqlite)
            {
                return DefaultSqliteConnectionString;
            }

            throw new InvalidOperationException(
                $"NafasServerOptions.Provider is {options.Provider}, which has no default connection string. " +
                "Set either NafasServerOptions.ConnectionString or NafasServerOptions.ConnectionStringName.");
        }
    }
}
