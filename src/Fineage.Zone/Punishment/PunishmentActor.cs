using Fineage.Zone.Simulation;

namespace Fineage.Zone.Punishment;

public sealed class PunishmentActor : GlobalActor
{
  public const string StageName = "punishment";

  readonly PunishmentManager _manager;

  public PunishmentActor(PunishmentManager manager)
    : base(StageName)
  {
    _manager = manager ?? throw new ArgumentNullException(nameof(manager));
  }

  public void Step(long nowUnixMs) => _manager.TickExpirations(nowUnixMs);
}