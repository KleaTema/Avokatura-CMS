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
