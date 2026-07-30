using System.Runtime.CompilerServices;
using DiffEngine;

namespace HearthCalendar.Tests;

public static class VerifyTestSettings
{
    [ModuleInitializer]
    public static void Initialize()
    {
        DiffRunner.Disabled = true;
    }
}
