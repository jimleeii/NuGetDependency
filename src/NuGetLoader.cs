using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

namespace NuGetDependency;

public class NuGetLoader : IDisposable
{
    // List to store assembly paths for loaded NuGet packages
    private readonly List<string> _assemblyPaths = [];
    // Path to the folder where NuGet packages are stored
    private readonly string _packagesFolder;
    // List of NuGet package sources
    private readonly List<PackageSource> _packageSources = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetLoader"/> class with the specified packages folder.
    /// </summary>
    /// <param name="packagesFolder">The path to the folder where NuGet packages will be stored. Defaults to "packages".</param>
    /// <param name="localSource">The path to a local folder containing NuGet packages. Defaults to null.</param>/// 
    /// <remarks>
    /// This constructor sets up the packages folder and subscribes to the <see cref="AppDomain.AssemblyResolve"/> event
    /// to handle assembly resolution for NuGet packages.
    /// </remarks>
    public NuGetLoader(string packagesFolder = "packages", string? localSource = null)
    {
        _packagesFolder = Path.GetFullPath(packagesFolder);
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

        // Add default NuGet source
        _packageSources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));

        // Add local source if provided
        if (!string.IsNullOrEmpty(localSource))
        {
            if (!Directory.Exists(localSource))
            {
                throw new DirectoryNotFoundException($"Local NuGet source directory not found: {localSource}");
            }
            _packageSources.Add(new PackageSource(localSource, "Local"));
        }
    }

    /// <summary>
    /// Installs a package to the packages folder.
    /// </summary>
    /// <param name="packageId">The ID of the package to install.</param>
    /// <param name="version">The version of the package to install.</param>
    /// <remarks>
    /// This method is asynchronous.
    /// The packages folder is configured when the NuGetLoader is created.
    /// The framework of the currently executing assembly is used to determine which packages to install.
    /// </remarks>
    public async Task InstallPackageAsync(string packageId, string version)
    {
        var settings = Settings.LoadDefaultSettings(null);
        var sourceRepositoryProvider = new SourceRepositoryProvider(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());

        // Add the package sources to the source repository provider
        foreach (var packageSource in _packageSources)
        {
            sourceRepositoryProvider.PackageSourceProvider.AddPackageSource(packageSource);
        }

        var sourceRepositories = sourceRepositoryProvider.GetRepositories().ToList();

        var framework = GetCurrentNuGetFramework();
        var packageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));

        using var cacheContext = new SourceCacheContext();
        var resolutionContext = new ResolutionContext(
            DependencyBehavior.Lowest,
            includePrelease : false,
            includeUnlisted : false,
            VersionConstraints.None);

        var logger = new NullLogger();

        var projectContext = new ProjectContext
        {
            PackageExtractionContext = new PackageExtractionContext(PackageSaveMode.Defaultv3, XmlDocFileSaveMode.None, null, logger),
            OriginalPackagesConfig = new XDocument()
        };
        var packageManager = new NuGetPackageManager(sourceRepositoryProvider, settings, _packagesFolder)
        {
            PackagesFolderNuGetProject = new FolderNuGetProject(_packagesFolder)
        };

        await packageManager.InstallPackageAsync(
            packageManager.PackagesFolderNuGetProject,
            packageIdentity,
            resolutionContext,
            projectContext,
            sourceRepositories, [],
            CancellationToken.None);

        CollectAssemblyPaths(framework);
    }

    /// <summary>
    /// Retrieves the current NuGet framework based on the entry assembly's target framework attribute.
    /// </summary>
    /// <returns>
    /// A <see cref="NuGetFramework"/> object that represents the current framework.
    /// If the target framework cannot be determined, returns the fallback framework "net8.0".
    /// </returns>
    private static NuGetFramework GetCurrentNuGetFramework()
    {
        var targetFramework = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        return targetFramework != null ?
            NuGetFramework.Parse(targetFramework) :
            NuGetFramework.Parse("net8.0"); // Updated fallback framework
    }

    /// <summary>
    /// Collect assembly paths for the given framework from the packages folder.
    /// </summary>
    /// <param name="framework">The framework to collect assembly paths for.</param>
    /// <remarks>
    /// The best matching framework (i.e. the highest version) is selected for each package.
    /// </remarks>
    private void CollectAssemblyPaths(NuGetFramework framework)
    {
        _assemblyPaths.Clear();

        foreach (var packageDir in Directory.EnumerateDirectories(_packagesFolder))
        {
            var libDir = Path.Combine(packageDir, "lib");
            if (!Directory.Exists(libDir)) continue;

            var compatibleFrameworks = Directory.EnumerateDirectories(libDir)
                .Select(d => new
                {
                    Path = d,
                        Framework = NuGetFramework.Parse(Path.GetFileName(d))
                })
                .Where(f => DefaultCompatibilityProvider.Instance.IsCompatible(framework, f.Framework))
                .OrderByDescending(f => f.Framework, new NuGetFrameworkSorter())
                .ToList();

            var bestFramework = compatibleFrameworks.FirstOrDefault();
            if (bestFramework != null)
            {
                _assemblyPaths.AddRange(Directory.GetFiles(bestFramework.Path, "*.dll"));
            }
        }
    }

    /// <summary>
    /// Resolves and loads an assembly from the collected assembly paths.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event data containing the name of the assembly to resolve.</param>
    /// <returns>
    /// The resolved assembly if found; otherwise, null.
    /// </returns>
    /// <remarks>
    /// This method is used as an event handler for the <see cref="AppDomain.AssemblyResolve"/> event 
    /// to load assemblies that are not found by default resolution. It searches through the collected 
    /// assembly paths for a matching assembly name and loads it if found.
    /// </remarks>
    private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);
        var dllPath = _assemblyPaths.FirstOrDefault(p =>
            Path.GetFileNameWithoutExtension(p).Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase));

        return dllPath != null ? Assembly.LoadFrom(dllPath) : null;
    }

    /// <summary>
    /// Releases all resources used by the <see cref="NuGetLoader"/> instance.
    /// </summary>
    /// <remarks>
    /// This method unregisters the AssemblyResolve event handler and suppresses 
    /// finalization to release unmanaged resources and improve performance.
    /// </remarks>
    public void Dispose()
    {
        AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
        GC.SuppressFinalize(this);
    }
}