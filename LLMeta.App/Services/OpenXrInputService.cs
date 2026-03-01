using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;

namespace LLMeta.App.Services;

public sealed class OpenXrInputService
{
    public Result CreateAndDestroyInstance()
    {
        var xr = XR.GetApi();
        var applicationInfo = CreateApplicationInfo();
        var instanceCreateInfo = new InstanceCreateInfo
        {
            Type = StructureType.InstanceCreateInfo,
            ApplicationInfo = applicationInfo,
        };

        var instance = new Instance();
        var result = xr.CreateInstance(ref instanceCreateInfo, ref instance);
        if (result != Result.Success)
        {
            return result;
        }

        xr.DestroyInstance(instance);
        return result;
    }

    private static unsafe ApplicationInfo CreateApplicationInfo()
    {
        var applicationInfo = new ApplicationInfo
        {
            ApplicationVersion = 1,
            EngineVersion = 1,
            ApiVersion = (ulong)new Version64(1, 0, 0),
        };

        var appName = applicationInfo.ApplicationName;
        var appNameSpan = new Span<byte>(appName, (int)XR.MaxApplicationNameSize);
        _ = SilkMarshal.StringIntoSpan("LLMeta.App", appNameSpan, NativeStringEncoding.UTF8);

        var engineName = applicationInfo.EngineName;
        var engineNameSpan = new Span<byte>(engineName, (int)XR.MaxEngineNameSize);
        _ = SilkMarshal.StringIntoSpan("LLMeta.OpenXR", engineNameSpan, NativeStringEncoding.UTF8);

        return applicationInfo;
    }
}

