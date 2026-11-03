using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace EnterpriseRemote.UnitTests;

public static class TestAssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        string hostServiceDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EnterpriseRemote.Host.Service.dll");
        if (File.Exists(hostServiceDll))
        {
            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(hostServiceDll);
            }
            catch { }
        }
    }
}
