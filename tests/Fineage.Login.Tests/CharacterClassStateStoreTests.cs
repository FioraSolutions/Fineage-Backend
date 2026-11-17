using Fineage.Accounts;
using Fineage.Login;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>Block G1: the subclass / noblesse class state survives the memory snapshot (the Postgres mirror input).</summary>
public sealed class CharacterClassStateStoreTests
{
    [Fact]
    public void Class_state_round_trips_through_the_snapshot()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("subs", "secret1").Ok);
        var created = login.CreateHuman("subs", "Subber", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        int id = created.Value!.Id;

        var state = new CharacterClassState
        {
            BaseClassId = 2,
            ClassIndex = 1,
            Noble = true,
            Slots = [new StoredClassSlot { Index = 0, ClassId = 2, Level = 76, Exp = 5, Sp = 7 }]
        };
        Assert.True(store.TrySaveClassState("subs", id, state));
        state.Noble = false; // the store keeps its own copy

        var back = MemoryAccountStore.FromSnapshot(store.ExportSnapshot()).FindById("subs", id)!;
        Assert.True(back.Noble);
        Assert.Equal(2, back.BaseClassId);
        Assert.Equal(1, back.ClassState!.ClassIndex);
        var slot = Assert.Single(back.ClassState.Slots);
        Assert.Equal((0, 2, 76, 5L, 7), (slot.Index, slot.ClassId, slot.Level, slot.Exp, slot.Sp));
    }
}
