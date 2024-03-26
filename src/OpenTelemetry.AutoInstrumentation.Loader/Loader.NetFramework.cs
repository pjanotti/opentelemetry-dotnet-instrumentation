// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NETFRAMEWORK

using System.Reflection;

namespace OpenTelemetry.AutoInstrumentation.Loader;

/// <summary>
/// A class that attempts to load the OpenTelemetry.AutoInstrumentation .NET assembly.
/// </summary>
internal partial class Loader
{
    private static string ResolveManagedProfilerDirectory()
    {
        var tracerHomeDirectory = ReadEnvironmentVariable("OTEL_DOTNET_AUTO_HOME") ?? string.Empty;
        var tracerFrameworkDirectory = "netfx";
        return Path.Combine(tracerHomeDirectory, tracerFrameworkDirectory);
    }

    private static Assembly? AssemblyResolve_ManagedProfilerDependencies(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        var threadInfo = $"[tid:{Thread.CurrentThread.ManagedThreadId}][{AppDomain.CurrentDomain.FriendlyName}][{AppDomain.CurrentDomain.Id}]";
        Logger.Debug("{0} => Requester [{1}] requested [{2}]", threadInfo, args?.RequestingAssembly?.FullName ?? "<null>", args?.Name ?? "<null>");

        // On .NET Framework, having a non-US locale can cause mscorlib
        // to enter the AssemblyResolve event when searching for resources
        // in its satellite assemblies. Exit early so we don't cause
        // infinite recursion.
        if (string.Equals(assemblyName, "mscorlib.resources", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assemblyName, "System.Net.Http", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Debug("{0} => Skipping assembly resolution for [{1}]", threadInfo, assemblyName);
            return null;
        }

        var path = Path.Combine(ManagedProfilerDirectory, $"{assemblyName}.dll");
        if (File.Exists(path))
        {
            try
            {
                var loadedAssembly = Assembly.LoadFrom(path);
                Logger.Debug<string, string, bool>("{0} => Assembly.LoadFrom(\"{1}\") succeeded={2}", threadInfo, path, loadedAssembly != null);
                return loadedAssembly;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "{0} => Assembly.LoadFrom(\"{1}\") Exception: {2}", threadInfo, path, ex.Message);
            }
        }

        return null;
    }
}
#endif
