using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// ── THE TABLE (multiplayer M5) ───────────────────────────────────────────────────────────────────────────
// Every answer the player gives outside a fight goes through here. In a solo run it is the session's own method,
// exactly as before. In a party run it is an answer FOR THIS MACHINE'S MEMBER, handed to the table
// (NetSession.Submit) so the host can number it — and the party's own steps, walking on past a room and choosing
// the next one, are this player's VOTE rather than the step itself.
public partial class SessionScreen
{
    private static bool AtTable => GameHost.Instance.IsMultiplayer;

    // How often this process drew the run screen, a party fight, and a wait on the others — what the
    // multiplayer probe reads to know the screens were really built (tools/smoke-mp.sh --screen).
    public static int Draws { get; private set; }
    public static int PartyFightsDrawn { get; private set; }
    public static int WaitsDrawn { get; private set; }

    // The member this machine answers for, in a party run; null in a solo run.
    private static string? MySeat(InteractiveRunSession session) =>
        !AtTable ? null
        : NetSession.Instance.IsOnline ? NetSession.MemberOf(NetSession.Instance.LocalSeat)
        : session.PendingSeat;

    // The run pointed at this machine's member while the page is drawn (see Rebuild); null in a solo run.
    private static RunState.MemberScope? MyMemberScope(InteractiveRunSession? session)
    {
        if (!AtTable || session is null || !NetSession.Instance.IsOnline)
            return null;
        var mine = NetSession.MemberOf(NetSession.Instance.LocalSeat);
        return session.Run.Party.FirstOrDefault(m => m.Id.Value == mine) is { } member
            ? session.Run.PushActiveMember(member)
            : null;
    }

    // A screen at a table shows this machine's own question (InteractiveRunSession.ViewSeat).
    private static void FaceMySeat(InteractiveRunSession session)
    {
        if (AtTable && NetSession.Instance.IsOnline)
            session.ViewSeat = NetSession.MemberOf(NetSession.Instance.LocalSeat);
    }

    private void AnswerChoice(InteractiveRunSession session, string choiceId)
    {
        if (AtTable)
            NetSession.Instance.Submit(new EventPickEntry(choiceId) { Seat = MySeat(session) });
        else
            session.Pick(choiceId);
    }

    private void AnswerEntities(InteractiveRunSession session, IReadOnlyList<int> indices)
    {
        if (AtTable)
            NetSession.Instance.Submit(new EntityPicksEntry(indices) { Seat = MySeat(session) });
        else
            session.PickEntities(indices);
    }

    private void AnswerConsumable(InteractiveRunSession session, RogueDeck.Run.ConsumableInstanceId id)
    {
        if (AtTable)
            NetSession.Instance.Submit(new ParkConsumableEntry(id) { Seat = MySeat(session) });
        else
            session.UseConsumable(id);
    }

    // The next room: the player's vote at a table, the step itself alone.
    private void AnswerNode(InteractiveRunSession session, string nodeId)
    {
        if (AtTable && NetSession.Instance.IsOnline)
            NetSession.Instance.Vote(nodeId);
        else
            session.PickNode(nodeId);
    }

    // Past a room. At a table every machine votes to go on as it gets there, so the party walks on once the
    // last of them has seen the room through; voted once per answer, not once per redraw.
    private int _continueVotedAt = -1;

    private void AnswerContinue(InteractiveRunSession session)
    {
        if (AtTable && NetSession.Instance.IsOnline)
        {
            if (_continueVotedAt == NetSession.Instance.EntryCount)
                return;
            _continueVotedAt = NetSession.Instance.EntryCount;
            NetSession.Instance.Vote("continue");
        }
        else
            session.Continue();
    }

    // This machine's member is through with the room and the others are still choosing.
    private void RenderWaitingOnOthers(InteractiveRunSession session)
    {
        WaitsDrawn++;
        var names = session.PendingSeats.Select(p => SeatName(p.Seat)).ToList();
        Title($"Waiting for {string.Join(", ", names)} …");
        var note = new Label
        {
            Text = "Everyone takes this room for themselves. The party walks on when the last of you is done.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        _main.AddChild(note);
    }

    // A seat as the table names it: the player, and who they play.
    private static string SeatName(string member)
    {
        var net = NetSession.Instance;
        var seat = member.StartsWith("member#", StringComparison.Ordinal)
            && int.TryParse(member["member#".Length..], out var at) ? at : -1;
        return net.IsOnline && seat >= 0 && seat < net.Seats.Count ? net.Seats[seat].Name : member;
    }

    // Who has voted for which room, for the map's markers: node id → the voters' names.
    private static Dictionary<string, List<string>> VotesByNode()
    {
        var net = NetSession.Instance;
        var byNode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (seat, vote) in net.Votes)
            if (vote != "continue" && seat < net.Seats.Count)
            {
                if (!byNode.TryGetValue(vote, out var voters))
                    byNode[vote] = voters = [];
                voters.Add(net.Seats[seat].Name);
            }
        return byNode;
    }
}
