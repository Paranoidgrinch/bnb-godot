using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// WHERE THIS MACHINE'S PLAYER SITS IN THE FIGHT (multiplayer M5). Every screen reads the fight through
// `play.Seat`: in a solo run that is the solo driver itself; in a party run it is the seat of this machine's
// member, whose moves go to the table (NetSession.Submit) rather than straight onto the fight.
public static class CombatSeats
{
    extension(RunPlayback play)
    {
        public ICombatSeat? Seat => GameHost.Instance.SeatOf(play);
    }
}
