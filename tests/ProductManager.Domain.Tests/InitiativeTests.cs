using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Domain.Tests;

public class InitiativeTests
{
    private static Initiative NewInitiative() =>
        new(Guid.NewGuid(), "Modernize Trade Execution", "Cut trade fails", "Ops feels the pain");

    [Fact]
    public void StrategicIntent_IsUnsetByDefault()
    {
        var initiative = NewInitiative();
        Assert.Null(initiative.StrategicIntent);
    }

    [Fact]
    public void SetStrategicIntent_TagsTheInitiative()
    {
        var initiative = NewInitiative();

        initiative.SetStrategicIntent(StrategicIntent.Parity);

        Assert.Equal(StrategicIntent.Parity, initiative.StrategicIntent);
    }

    [Fact]
    public void SetStrategicIntent_CanBeClearedBackToNull()
    {
        var initiative = NewInitiative();
        initiative.SetStrategicIntent(StrategicIntent.Differentiation);

        initiative.SetStrategicIntent(null);

        Assert.Null(initiative.StrategicIntent);
    }
}
