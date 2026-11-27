using System.Runtime.CompilerServices;

namespace Fineage.Zone.Tests;

/// <summary>
/// Unit/integration tests keep the pre-parity free-grant trainer mode so enter bursts still carry NPC tree
/// skills (Wind Strike, etc.). Production / local Zone default is Mobius SP learn (#1049); tests that need
/// that mode set <c>World.TrainerSpLearn = true</c> explicitly.
/// </summary>
internal static class TestHostDefaults
{
    [ModuleInitializer]
    internal static void UseFreeTrainerGrantsUnlessTestOverrides()
    {
        Environment.SetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN", "0");
    }
}
