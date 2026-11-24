using System.Runtime.CompilerServices;
using ThixOffice.Core.Infrastructure;

namespace ThixWrite.Core.Tests;

/// <summary>
/// The process is ThixWrite before the first test: the shared document base reads the app (its save type for the recovery file) from
/// <see cref="AppInfo"/>, as in the real app, where Main names it first.
/// </summary>
internal static class TestApp
{
    [ModuleInitializer]
    internal static void Initialize() => AppInfo.Initialize(ThixWriteApp.Definition, typeof(ThixWriteApp).Assembly);
}
