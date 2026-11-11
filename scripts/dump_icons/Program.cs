using System;
using System.IO;
using System.Linq;
using System.Reflection;

class P
{
    static void Main(string[] args)
    {
        var dll = Path.GetFullPath(args[0]);
        var coreDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var pkgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var paths = Directory.GetFiles(coreDir, "*.dll")
            .Concat(Directory.GetFiles(pkgDir, "*.dll", SearchOption.AllDirectories))
            .Concat(new[] { dll })
            .Distinct().ToList();
        paths.Add(typeof(object).Assembly.Location);
        var resolver = new System.Reflection.PathAssemblyResolver(paths);
        using var mlc = new System.Reflection.MetadataLoadContext(resolver, typeof(object).Assembly.GetName().Name);
        var asm = mlc.LoadFromAssemblyPath(dll);
        var t = asm.GetType("Wpf.Ui.Controls.SymbolRegular");
        if (t == null) { t = asm.GetTypes().First(x => x.Name == "SymbolRegular"); }
        foreach (var n in Enum.GetNames(t)) Console.WriteLine(n);
    }
}
