using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// THE TABLE, PROVED HEADLESS (multiplayer M6): `--smoke-mp-host <port>` and `--smoke-mp-join <port>` in two
// processes on one machine (tools/smoke-mp.sh). Each one is a player who answers for its OWN seat only — its
// member's cards, its member's rooms, its vote — through NetSession exactly as a click would, and never for
// anybody else. After `--mp-answers` answers, or when the run ends, both print how their run stands; the script
// says whether the two lines are the same line. If they are, two machines played one run.
public partial class MpSmoke : Godot.Node
{
    private readonly bool _hosting;
    private readonly int _port;
    private readonly int _seed;
    private readonly int _limit;
    private readonly string _character;
    private readonly int _seats;
    private readonly bool _screen;
    private readonly int _dropAt;
    private readonly bool _resume;
    private readonly string? _id;
    private bool _dropped;
    private bool _onScreen;
    private string? _shotKind;
    private int _shots;
    private double _shotWait;
    private bool _readied;
    private bool _done;
    private int _waitingFor = -1;
    private string? _voted;
    private int _votedAt = -1;
    private double _idle;
    private readonly HashSet<CardInstanceId> _tried = [];

    // `screen`: play with the run screen drawn, so every state of a party run is put on the screen once — the
    // check that the party's fight, rooms and waits draw at all.
    // `dropAt`: a guest that pulls its own cable at that answer and must find its way back to the table.
    public MpSmoke(bool hosting, int port, int seed, int limit, string character, int seats, bool screen = false,
        int dropAt = -1, bool resume = false, string? id = null)
    {
        _dropAt = dropAt;
        _resume = resume;
        _id = id;
        _screen = screen;
        _hosting = hosting;
        _port = port;
        _seed = seed;
        _limit = limit;
        _character = character;
        _seats = seats;
    }

    public override void _Ready()
    {
        var net = NetSession.Instance;
        net.AnswerLimit = _limit;
        NetSession.TableSavePath = "user://smoke-table-save.json";
        net.LocalName = _hosting ? "Host" : $"Guest {OS.GetProcessId()}";
        net.LocalId = _id ?? $"smoke-{OS.GetProcessId()}";
        // `resume`: the host opens the table it saved last time, and its players come back to their seats.
        var error = _hosting
            ? _resume ? net.HostSavedTable(_port) : net.Host(_port)
            : net.Join("127.0.0.1", _port);
        if (error is not null)
            Finish($"could not open the table: {error}");
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;
        var net = NetSession.Instance;
        // One long frame — a screen built for the first time while three other games share the machine — is not
        // a minute of nothing happening; only steady frames count towards giving up.
        _idle += Math.Min(delta, 0.5);
        if (_idle > 60)
        {
            Finish($"nothing moved for a minute (entries={net.EntryCount}, status «{net.Status}», {Standing()})");
            return;
        }

        if (!net.Started)
        {
            if (!_readied && net.LocalSeat >= 0 && !_resume)
            {
                net.ChooseCharacter(_character, ready: true);
                _readied = true;
            }
            if (_hosting && net.Seats.Count == _seats && net.CanStart)
                net.StartRun(_seed);
            return;
        }

        if (!_hosting && !_dropped && _dropAt >= 0 && net.EntryCount >= _dropAt)
        {
            _dropped = true;
            _waitingFor = -1;
            net.DropLineForTest();
            return;
        }
        var play = GameHost.Instance.Play;
        if (play?.Session is not { } session)
            return;
        if (_screen && !_onScreen)
        {
            _onScreen = true;
            GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
        }
        if (_screen && !DisplayServer.GetName().Contains("headless") && Shoot(play, session, delta))
            return;
        if (session.Error is not null)
        {
            Finish($"the run broke: {session.Error}");
            return;
        }
        if (session.IsComplete || net.EntryCount >= _limit)
        {
            Finish(null);
            return;
        }
        // One answer in flight at a time: wait for the host to number the last one before deciding again.
        if (_waitingFor >= 0 && net.EntryCount <= _waitingFor)
            return;
        _waitingFor = -1;

        var seat = net.LocalSeat;
        var me = NetSession.MemberOf(seat);
        var before = net.EntryCount;
        if (play.PartyCombatDriver is { Current: { IsOver: false } fight } driver)
        {
            // A question a card asked is its chooser's to answer (the host's, when it is nobody's in particular), so
            // two machines never race to it.
            var mineToAnswer = driver.PendingChooser is { } chooser
                ? chooser.value == NetSession.CombatantOf(seat)
                : _hosting;
            if (driver.PendingCardChoice is { } cards)
            {
                if (mineToAnswer)
                    net.Submit(new CardPicksEntry([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]));
            }
            else if (driver.PendingOptionChoice is not null)
            {
                if (mineToAnswer)
                    net.Submit(new OptionPicksEntry([0]));
            }
            else
            {
                var mine = new CombatantId(NetSession.CombatantOf(seat));
                if (fight.ActiveMembers().Contains(mine))
                {
                    var enemy = fight.State.Combatants.First(c => c.TeamId == StandardCombatIds.EnemyTeam && c.IsAlive).Id;
                    if (fight.EnergyOf(mine) > 0 && fight.HandOf(mine).FirstOrDefault(c => _tried.Add(c.Id)) is { } card)
                        net.Submit(new CombatPlayEntry(mine, card.Id, enemy));
                    else
                    {
                        _tried.Clear();
                        net.Submit(new CombatEndTurnEntry(mine));
                    }
                }
            }
        }
        else if (session.PendingFor(me) is { } prompt)
        {
            ReplayEntry answer = prompt.Entities is { } entities
                ? new EntityPicksEntry(entities.AllowSkip ? [] : [0]) { Seat = me }
                : new EventPickEntry(prompt.Choices[^1].Id) { Seat = me };
            net.Submit(answer);
        }
        else if (session.PendingSeats.Count == 0 && (session.IsAwaitingInterlude || session.IsAwaitingNodeChoice))
        {
            // The party's step: every seat votes once per question, and the odd seats disagree with the even ones
            // so the tally and its tie-break are walked too.
            var choice = session.IsAwaitingInterlude
                ? "continue"
                : session.PendingNodeChoices[seat % session.PendingNodeChoices.Count].Id.Value;
            if (_voted != choice || _votedAt != net.EntryCount)
            {
                _voted = choice;
                _votedAt = net.EntryCount;
                net.Vote(choice);
            }
        }

        if (net.Role == NetSession.NetRole.Client && net.EntryCount == before && _voted is null)
            _waitingFor = before;
        if (net.EntryCount != before)
        {
            _idle = 0;
            _voted = null;
        }
    }

    // A PICTURE OF EACH KIND OF MOMENT, the first time the run stands in it: a party fight, a room of its own, a
    // wait on the others, the vote on the map. Taken half a second after the moment arrives so the screen has
    // drawn it, and the bot holds its answer meanwhile. True while a picture is being waited for.
    private bool Shoot(RogueDeck.Sandbox.Composition.RunPlayback play, InteractiveRunSession session, double delta)
    {
        var me = NetSession.MemberOf(NetSession.Instance.LocalSeat);
        var kind = play.PartyCombatDriver?.Current is { } fight
                ? fight.ActiveMembers().Contains(new CombatantId(NetSession.CombatantOf(NetSession.Instance.LocalSeat))) ? "fight" : "fight-waiting"
            : session.PendingFor(me) is { ShopShelf: not null } ? "shop"
            : session.PendingFor(me) is { Entities: not null } ? "reward"
            : session.PendingFor(me) is not null ? "event"
            : session.IsWaitingOnOthers ? "room-waiting"
            : session.IsAwaitingNodeChoice ? "map"
            : null;
        if (kind is null || _shots >= 12)
            return false;
        if (_shotKind != kind)
        {
            if (_taken.Contains(kind))
                return false;
            _shotKind = kind;
            _shotWait = 0;
            return true;
        }
        _shotWait += delta;
        if (_shotWait < 0.6)
            return true;
        GetViewport().GetTexture().GetImage().SavePng($"user://smoke-mp-{NetSession.Instance.LocalSeat}-{kind}.png");
        GD.Print($"smoke-mp-shot: {ProjectSettings.GlobalizePath($"user://smoke-mp-{NetSession.Instance.LocalSeat}-{kind}.png")}");
        _taken.Add(kind);
        _shots++;
        _shotKind = null;
        return false;
    }

    private readonly HashSet<string> _taken = [];

    // What the run is waiting for, in words — for a probe that stopped.
    private static string Standing()
    {
        var play = GameHost.Instance.Play;
        if (play?.Session is not { } session)
            return "no run";
        var fight = play.PartyCombatDriver?.Current;
        return $"fight={(fight is null ? "none" : $"r{fight.Round} active=[{string.Join(",", fight.ActiveMembers())}]")} "
            + $"cardChoice={play.PartyCombatDriver?.PendingCardChoice?.Count.ToString() ?? "-"} "
            + $"optionChoice={play.PartyCombatDriver?.PendingOptionChoice?.Count.ToString() ?? "-"} "
            + $"seats=[{string.Join(",", session.PendingSeats.Select(p => p.Seat + ":" + (p.Situation?.Id ?? p.Entities?.Purpose)))}] "
            + $"choice={session.PendingSituation?.Id ?? "-"} entities={session.PendingEntities?.Purpose ?? "-"} "
            + $"interlude={session.IsAwaitingInterlude} node={session.IsAwaitingNodeChoice} "
            + $"votes=[{string.Join(",", NetSession.Instance.Votes.Select(v => v.Key + ":" + v.Value))}]";
    }

    // Each process prints one line; the script compares them.
    private void Finish(string? fault)
    {
        _done = true;
        var net = NetSession.Instance;
        var play = GameHost.Instance.Play;
        GD.Print(fault is null
            ? $"smoke-mp: seat={net.LocalSeat} entries={net.EntryCount} "
                + $"result={play?.Session?.Run.Result} check={(play is null ? "—" : NetSession.Check(play))}"
            : $"smoke-mp: seat={net.LocalSeat} FAILED {fault}");
        if (_screen)
            GD.Print($"smoke-mp-screen: seat={net.LocalSeat} scene={GetTree().CurrentScene?.Name} "
                + $"draws={SessionScreen.Draws} party-fights-drawn={SessionScreen.PartyFightsDrawn} "
                + $"waits-drawn={SessionScreen.WaitsDrawn}");
        // The host stays a moment so the last entry reaches everyone before the line closes.
        GetTree().CreateTimer(_hosting ? 2.0 : 0.5).Timeout += () => GetTree().Quit(fault is null ? 0 : 1);
    }
}
