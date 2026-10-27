using System.Reflection;
using Shouldly;

namespace Ash.Tests.Architecture;

public class DbContextTests
{
    private const string DbContextName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string ModuleBaseName = "Ash.Infrastructure.Persistence.ModuleDbContextBase";

    private static readonly string[] Modules =
        ["Identity", "Reference", "Cases", "Documents", "Notifications", "Audit", "Reports", "Chat"];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_DbContexts_inherit_ModuleDbContextBase(string module)
    {
        var assembly = Assembly.Load($"Ash.Modules.{module}");

        var offenders = assembly.GetTypes()
            .Where(t => t.IsClass && BaseTypes(t).Any(b => b.FullName == DbContextName))
            .Where(t => BaseTypes(t).All(b => b.FullName != ModuleBaseName))
            .Select(t => t.FullName!)
            .ToList();

        offenders.ShouldBeEmpty($"DbContexts in {module} must inherit ModuleDbContextBase (UTC DateTime conversion, shared transaction)");
    }

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            yield return b;
    }

    public static IEnumerable<object[]> ModuleNames() => Modules.Select(m => new object[] { m });
}
