using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Ash.Tests.Architecture;

public class ModuleDependencyTests
{
    private static readonly string[] Modules =
        ["Identity", "Reference", "Cases", "Documents", "Notifications", "Audit", "Reports", "Chat"];

    private static Assembly Load(string module) => Assembly.Load($"Ash.Modules.{module}");

    [Theory]
    [MemberData(nameof(OtherModulePairs))]
    public void Module_does_not_depend_on_internals_of_other_module(string module, string other)
    {
        var result = Types.InAssembly(Load(module))
            .That().ResideInNamespaceStartingWith($"Ash.Modules.{module}")
            .ShouldNot().HaveDependencyOnAny(InternalNamespaces(other))
            .GetResult();

        var offenders = result.FailingTypeNames ?? [];
        offenders.ShouldBeEmpty($"{module} must not depend on internals of {other}; use Ash.Modules.{other}.Contracts");
    }

    [Theory]
    [InlineData("Ash.SharedKernel")]
    [InlineData("Ash.Infrastructure")]
    [InlineData("Ash.AspNetCore")]
    public void Building_block_does_not_depend_on_any_module(string buildingBlock)
    {
        var result = Types.InAssembly(Assembly.Load(buildingBlock))
            .ShouldNot().HaveDependencyOn("Ash.Modules")
            .GetResult();

        var offenders = result.FailingTypeNames ?? [];
        offenders.ShouldBeEmpty($"{buildingBlock} must not reference any module, including Contracts");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_do_not_depend_on_Ash_Infrastructure(string module)
    {
        var result = Types.InAssembly(Assembly.Load($"Ash.Modules.{module}.Contracts"))
            .ShouldNot().HaveDependencyOn("Ash.Infrastructure")
            .GetResult();

        var offenders = result.FailingTypeNames ?? [];
        offenders.ShouldBeEmpty($"Ash.Modules.{module}.Contracts must only use Ash.SharedKernel (events: Ash.SharedKernel.Events)");
    }

    /// <summary>An unused project reference does not show up in the compiled assembly, so the .csproj files are checked too.</summary>
    [Fact]
    public void No_Contracts_project_references_Ash_Infrastructure()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ash.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull("Ash.sln not found above the test output folder");
        var projects = Directory.GetFiles(Path.Combine(root.FullName, "Modules"), "*.Contracts.csproj", SearchOption.AllDirectories);
        projects.ShouldNotBeEmpty();

        var offenders = projects
            .Where(p => File.ReadAllText(p).Contains("Ash.Infrastructure.csproj", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();
        offenders.ShouldBeEmpty("Contracts projects may reference Ash.SharedKernel only");
    }

    public static IEnumerable<object[]> ModuleNames() => Modules.Select(m => new object[] { m });

    private static string[] InternalNamespaces(string module) =>
    [
        $"Ash.Modules.{module}.Domain",
        $"Ash.Modules.{module}.Application",
        $"Ash.Modules.{module}.Infrastructure",
        $"Ash.Modules.{module}.Endpoints",
        $"Ash.Modules.{module}.{module}Module",
    ];

    public static IEnumerable<object[]> OtherModulePairs()
    {
        foreach (var m in Modules)
            foreach (var o in Modules.Where(x => x != m))
                yield return [m, o];
    }
}
