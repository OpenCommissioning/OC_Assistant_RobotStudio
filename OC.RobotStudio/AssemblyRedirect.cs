using System.Reflection;
using System.Runtime.CompilerServices;

namespace OC.RobotStudio;

internal static class AssemblyRedirects
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        var protectedData = typeof(System.Security.Cryptography.ProtectedData).Assembly;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            new AssemblyName(args.Name).Name == "System.Security" ? protectedData : null;
    }
}
