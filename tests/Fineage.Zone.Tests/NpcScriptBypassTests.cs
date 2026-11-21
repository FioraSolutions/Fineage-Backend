using Fineage.Zone.NpcHtml;
using Xunit;
namespace Fineage.Zone.Tests;
public sealed class NpcScriptBypassTests {
 [Fact] public async Task Unknown_script_shows_unavailable_and_logs_once() {
  var kit = new NetTestKit(); var (session, _) = await kit.EnterAsync();
  Assert.Null(kit.World.NpcScriptBypassForTest(session, 30154, "NotARealMobiusScript", "", out var html));
  Assert.Contains("not implemented", html, StringComparison.OrdinalIgnoreCase);
  Assert.Equal(1, kit.World.UnknownNpcScriptLogCount);}
}
