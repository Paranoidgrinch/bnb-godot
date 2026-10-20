using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// ── PLAYING ONE RUN ON SEVERAL MACHINES (multiplayer M4) ─────────────────────────────────────────────────
// One player hosts, up to three join by address. Nothing but ANSWERS crosses the wire: the engine is
// deterministic, so every machine that starts the same run from the same seed, party and profile and is handed
// the same answers in the same order stands in the same place. The host is the one who decides that order.
//
//   client → host   Intent   "my seat would like to answer this"
//   host → all      Entry    answer number n, and how the run stood after it (the check)
//
// The host checks that an intent belongs to the seat that sent it (a member's room answer, a member's card),
// gives it to its own run through RunPlayback.Apply — the very door a click uses — and numbers it. A client
// gives every entry to its run in that order and compares the check; a run that has fallen out of step asks
// for the whole list again (Resync) and replays it from the seed. A joiner, or a player coming back after a
// dropped line, gets that same list.
//
// The map and the walk past a room belong to the party, so they are VOTED: every connected seat votes, the
// most votes win, and a tie goes the host's way.
//
// ⚠ An autoload, so RPCs address the same node path on every peer (/root/Net). Messages are JSON in one RPC,
// one record with a kind — a protocol a person can read in a packet log, and that a version check can guard.
public partial class NetSession : Godot.Node
{
    public static NetSession Instance { get; private set; } = null!;

    public const int DefaultPort = 24642;
    public const int MaxSeats = 4;
    private const int Protocol = 1;

    public enum NetRole { Offline, Host, Client }

    public sealed record Seat(long Peer, string Name, string PlayerId, string? Character, bool Ready, bool Connected);

    public NetRole Role { get; private set; } = NetRole.Offline;
    public bool IsOnline => Role != NetRole.Offline;
    public List<Seat> Seats { get; } = [];
    public int LocalSeat { get; private set; } = -1;
    public bool Started { get; private set; }
    public string Status { get; private set; } = "";

    // The run's answers, in the host's order, from its first: what a joiner or a resync replays.
    private readonly List<string[]> _entries = [];
    public int EntryCount => _entries.Count;
    private StartMessage? _start;

    // A host that stops taking answers after this many — for a probe that has to end both machines on the same
    // answer. Null for a real table.
    public int? AnswerLimit { get; set; }

    // Who this machine says it is at the door; a probe running several players on one machine names them apart.
    public string? LocalName { get; set; }
    public string? LocalId { get; set; }

    // Map and interlude votes by seat ("continue", or a node id), for the markers a map draws.
    public Dictionary<int, string> Votes { get; } = [];

    [Signal]
    public delegate void LobbyChangedEventHandler();

    [Signal]
    public delegate void RunStartedEventHandler();

    [Signal]
    public delegate void StatusChangedEventHandler();

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionLost;
        Multiplayer.ServerDisconnected += OnConnectionLost;
    }

    // ── A LINE THAT DROPS ────────────────────────────────────────────────────────────────────────────────
    // A machine that stops answering for a few seconds — building a run, loading a screen, a laptop's Wi-Fi
    // waking up — must not lose its seat over it: the connection is given half a minute before it counts as
    // gone (ENet's default is about five seconds). And a client that does lose the host while a run is on goes
    // back to the same address by itself; the host knows its player id, gives it its seat back and the whole run
    // with it.
    private const int PatienceMs = 30_000;
    private string? _address;
    private int _port;
    private int _reconnects;
    public const int MaxReconnects = 10;

    private void Patient(long peer)
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.GetPeer((int)peer) is { } line)
            line.SetTimeout(0, PatienceMs, PatienceMs * 2);
    }

    private void OnConnectionLost()
    {
        if (Role != NetRole.Client)
            return;
        if (Started && _address is { } address && _reconnects < MaxReconnects)
        {
            _reconnects++;
            Report($"Lost the host — reconnecting ({_reconnects}/{MaxReconnects}) …");
            GetTree().CreateTimer(1.0).Timeout += () => Reconnect(address, _port);
            return;
        }
        Report(Started ? "Lost the host." : "Could not reach the host.");
    }

    // For a probe: the line goes dead under this machine's feet, as a pulled cable would.
    public void DropLineForTest()
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer line)
            line.Close();
        OnConnectionLost();
    }

    // Back to the same table, keeping the run on screen until the host sends it again.
    private void Reconnect(string address, int port)
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer old)
            old.Close();
        var peer = new ENetMultiplayerPeer();
        if (peer.CreateClient(address, port) != Error.Ok)
        {
            OnConnectionLost();
            return;
        }
        Multiplayer.MultiplayerPeer = peer;
    }

    // ── HOSTING AND JOINING ──────────────────────────────────────────────────────────────────────────────

    public string? Host(int port = DefaultPort)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, MaxSeats - 1);
        if (error != Error.Ok)
        {
            Report($"Could not open port {port}: {error}.");
            return Status;
        }
        Multiplayer.MultiplayerPeer = peer;
        Role = NetRole.Host;
        Seats.Add(new Seat(1, LocalName ?? PlayerIdentity.Name ?? "Host", LocalId ?? PlayerIdentity.Id,
            Character: null, Ready: false, Connected: true));
        LocalSeat = 0;
        Report($"Hosting on port {port}.");
        OpenPort(port);
        BroadcastLobby();
        return null;
    }

    public string? Join(string address, int port = DefaultPort)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            Report($"Could not connect to {address}:{port}: {error}.");
            return Status;
        }
        Multiplayer.MultiplayerPeer = peer;
        Role = NetRole.Client;
        _address = address;
        _port = port;
        _reconnects = 0;
        Report($"Connecting to {address}:{port} …");
        return null;
    }

    public void Leave()
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer)
            peer.Close();
        Multiplayer.MultiplayerPeer = null;
        Role = NetRole.Offline;
        Seats.Clear();
        LocalSeat = -1;
        Started = false;
        _entries.Clear();
        _start = null;
        Votes.Clear();
        _address = null;
        _saved = null;
    }

    // A home router will usually not let a stranger in; ask it to (UPnP) and say so either way. Off the main
    // thread, because discovery waits on the network for a couple of seconds.
    private void OpenPort(int port) => System.Threading.Tasks.Task.Run(() =>
    {
        var upnp = new Upnp();
        var found = upnp.Discover();
        var mapped = found == (int)Upnp.UpnpResult.Success
            && upnp.AddPortMapping(port, port, "Bureaucrats and Broomsticks", "UDP") == (int)Upnp.UpnpResult.Success;
        var address = mapped ? upnp.QueryExternalAddress() : null;
        CallDeferred(nameof(Report), mapped
            ? $"Hosting on port {port}. Friends join at {address}:{port}."
            : $"Hosting on port {port}. The router did not open it — forward UDP {port}, or play over a VPN such as Tailscale.");
    });

    private void Report(string status)
    {
        Status = status;
        GD.Print($"net: {status}");
        EmitSignal(SignalName.StatusChanged);
    }

    // ── THE LOBBY ────────────────────────────────────────────────────────────────────────────────────────

    public void ChooseCharacter(string characterId, bool ready)
    {
        if (Role == NetRole.Host)
        {
            Seats[0] = Seats[0] with { Character = characterId, Ready = ready };
            BroadcastLobby();
        }
        else if (Role == NetRole.Client)
            SendToHost(new NetMessage { Kind = "seat", Character = characterId, Ready = ready });
    }

    public bool CanStart => Role == NetRole.Host && !Started && Seats.Count > 0
        && Seats.All(s => s.Connected && s.Ready && s.Character is not null);

    // ── THE TABLE'S SAVE (host only) ──────────────────────────────────────────────────────────────────────
    // A run of an evening is hours long, and a table does not finish in one sitting. The host writes the run down
    // whenever nobody is in the middle of a question — between rooms, or in a fight with no card asking anything —
    // with who sat in which seat. Opened again, the seats wait for their players by player id, and the run
    // resumes for everyone from that save.
    // A probe keeps its tables out of the player's (tools/smoke-mp.sh).
    public static string TableSavePath { get; set; } = "user://table-save.json";

    public static bool HasTableSave => Godot.FileAccess.FileExists(TableSavePath);

    public static TableSave? ReadTableSave()
    {
        if (!HasTableSave)
            return null;
        try
        {
            return JsonSerializer.Deserialize<TableSave>(Godot.FileAccess.GetFileAsString(TableSavePath), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveTable()
    {
        if (Role != NetRole.Host || _start is null || GameHost.Instance.Play is not { Session: { } session } play)
            return;
        if (session.IsComplete)
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TableSavePath));
            return;
        }
        if (session.PendingSeats.Count > 0 || session.IsAwaitingChoice || session.IsAwaitingEntities
            || play.PartyCombatDriver is { PendingCardChoice: not null } or { PendingOptionChoice: not null })
            return;
        if (play.SaveJson() is not { } json)
            return;
        var table = new TableSave(json, _start.Party, [.. Seats.Select(s => s.PlayerId)], [.. Seats.Select(s => s.Name)],
            _start.Meta);
        using var file = Godot.FileAccess.Open(TableSavePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(table, Json));
    }

    // Open the saved table: every seat is laid for the player who sat there, the host's own taken at once.
    public string? HostSavedTable(int port = DefaultPort)
    {
        if (ReadTableSave() is not { } table)
            return "There is no saved table.";
        if (Host(port) is { } error)
            return error;
        _saved = table;
        Seats.Clear();
        for (var index = 0; index < table.Party.Length; index++)
            Seats.Add(new Seat(index == 0 ? 1 : 0, index == 0 ? LocalName ?? PlayerIdentity.Name ?? table.Names[index] : table.Names[index],
                index == 0 ? LocalId ?? PlayerIdentity.Id : table.PlayerIds[index], table.Party[index], Ready: true,
                Connected: index == 0));
        Report($"Waiting for {string.Join(", ", table.Names.Skip(1))} to take their seats again.");
        BroadcastLobby();
        return null;
    }

    private TableSave? _saved;

    // The host starts the run for everyone: one seed, one party in seat order, one profile.
    public void StartRun(int seed)
    {
        if (!CanStart)
            return;
        var host = GameHost.Instance;
        _start = _saved is { } saved
            ? new StartMessage(0, saved.Party, saved.Meta, saved.Save)
            : new StartMessage(seed, [.. Seats.Select(s => s.Character!)], host.Meta.Snapshot());
        _saved = null;
        Started = true;
        _entries.Clear();
        Votes.Clear();
        BeginRun(_start);
        foreach (var (seat, index) in Seats.Select((s, i) => (s, i)).Skip(1))
            if (seat.Connected)
                Send(seat.Peer, new NetMessage { Kind = "start", Start = _start, Seat = index, Entries = [] });
    }

    private void BeginRun(StartMessage start)
    {
        if (start.Save is { } save)
            GameHost.Instance.ResumePartyRun(save, start.Meta);
        else
            GameHost.Instance.StartPartyRun(start.Seed, start.Party, start.Meta);
        // This machine's screen shows this machine's member's questions, from the first one on.
        if (GameHost.Instance.Play?.Session is { } session)
            session.ViewSeat = MemberOf(LocalSeat);
        EmitSignal(SignalName.RunStarted);
    }

    // ── ANSWERS ──────────────────────────────────────────────────────────────────────────────────────────

    // The seat's member, as the run names it ("member#2"), and as the fight names it ("hero" for the first).
    public static string MemberOf(int seat) => $"member#{seat}";
    public static string CombatantOf(int seat) => seat == 0 ? "hero" : $"member#{seat}";

    // What this machine's player would like to answer. Offline it is simply given; a host checks and gives it;
    // a client asks the host.
    public void Submit(ReplayEntry entry)
    {
        var play = GameHost.Instance.Play;
        if (play is null)
            return;
        switch (Role)
        {
            case NetRole.Offline:
                play.Apply(entry);
                break;
            case NetRole.Host:
                TakeIntent(LocalSeat, RunAnswerCodec.Encode(entry));
                break;
            case NetRole.Client:
                SendToHost(new NetMessage { Kind = "intent", Answer = RunAnswerCodec.Encode(entry) });
                break;
        }
    }

    // A vote for the party's next step: "continue" past a room, or the id of the next room. ⚠ A vote is cast AT
    // an answer number and only counts for the question standing there: a "continue" that crosses the wire while
    // the party has already walked on to the map must not be counted on the map, nor clear the votes that are.
    public void Vote(string choice)
    {
        if (Role == NetRole.Client)
            SendToHost(new NetMessage { Kind = "vote", Vote = choice, Index = _entries.Count });
        else if (Role == NetRole.Host)
            TakeVote(LocalSeat, choice, _entries.Count);
    }

    private void TakeIntent(int seat, string[] wire)
    {
        if (GameHost.Instance.Play is not { } play || _entries.Count >= (AnswerLimit ?? int.MaxValue))
            return;
        ReplayEntry entry;
        try
        {
            entry = RunAnswerCodec.Decode(wire);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException)
        {
            return;
        }
        if (!Belongs(seat, entry))
            return;
        if (play.Apply(entry))
            Publish(wire);
    }

    // Whose answer may this be? A room answer is its seat's, a card or an end of turn its member's; the party's
    // own steps go through the vote. The host may answer for a seat whose player has dropped out, so a run is
    // never stranded waiting on a closed laptop.
    private bool Belongs(int seat, ReplayEntry entry)
    {
        bool Mine(int owner) => owner == seat || (seat == LocalSeat && Role == NetRole.Host
            && owner >= 0 && owner < Seats.Count && !Seats[owner].Connected);
        int SeatOfMember(string? member) => member is { } m && m.StartsWith("member#", StringComparison.Ordinal)
            && int.TryParse(m["member#".Length..], out var at) ? at : -1;
        int SeatOfCombatant(string? combatant) => combatant == "hero" ? 0 : SeatOfMember(combatant);
        return entry switch
        {
            NodePickEntry or InterludeContinueEntry => false,
            CombatPlayEntry play => Mine(SeatOfCombatant(play.Member?.value)),
            CombatActionEntry action => Mine(SeatOfCombatant(action.Member?.value)),
            CombatEndTurnEntry end => Mine(SeatOfCombatant(end.Member?.value)),
            // A question a card asked in the fight: its chooser's — the hero whose cards are offered, or whose move
            // raised it — and anyone's when the fight asked nobody in particular.
            CardPicksEntry or OptionPicksEntry =>
                GameHost.Instance.Play?.PartyCombatDriver?.PendingChooser is not { } chooser
                || Mine(SeatOfCombatant(chooser.value)),
            // A consumable is its carrier's to drink.
            CombatConsumableEntry use => Mine(CarrierOf(use.Instance)),
            _ => Mine(SeatOfMember(entry.Seat)),
        };
    }

    // The seat whose member carries this consumable, or -1.
    private static int CarrierOf(RogueDeck.Run.ConsumableInstanceId instance)
    {
        var party = GameHost.Instance.Play?.Session?.Run.Party ?? [];
        for (var seat = 0; seat < party.Count; seat++)
            if (party[seat].FindConsumable(instance) is not null)
                return seat;
        return -1;
    }

    private void TakeVote(int seat, string choice, int at)
    {
        // A vote cast at an answer this machine is already past: the voter has not heard of some of them. They
        // are sent again — a client takes an answer it already has as the duplicate it is.
        if (Role == NetRole.Host && Started && at >= 0 && at < _entries.Count && seat > 0 && seat < Seats.Count)
            for (var index = at; index < _entries.Count; index++)
                Send(Seats[seat].Peer, new NetMessage { Kind = "entry", Index = index, Answer = _entries[index] });
        if (Role != NetRole.Host || GameHost.Instance.Play?.Session is not { } session
            || _entries.Count >= (AnswerLimit ?? int.MaxValue) || at != _entries.Count)
            return;
        // Only an answer to the question the party stands at: "continue" past a room, or one of the rooms open.
        var fits = choice == "continue"
            ? session.IsAwaitingInterlude
            : session.IsAwaitingNodeChoice && session.PendingNodeChoices.Any(n => n.Id.Value == choice);
        if (!fits)
            return;
        Votes[seat] = choice;
        BroadcastVotes();

        var voters = Seats.Select((s, i) => (s, i)).Where(x => x.s.Connected).Select(x => x.i).ToList();
        if (!voters.All(Votes.ContainsKey))
            return;
        // Most votes win; a tie goes the host's way, and failing that, the first of them in seat order.
        var tally = voters.GroupBy(v => Votes[v]).Select(g => (Choice: g.Key, Count: g.Count())).ToList();
        var best = tally.Max(t => t.Count);
        var winners = tally.Where(t => t.Count == best).Select(t => t.Choice).ToList();
        var winner = Votes.TryGetValue(0, out var mine) && winners.Contains(mine) ? mine : winners[0];
        Votes.Clear();
        BroadcastVotes();

        ReplayEntry? step = winner == "continue"
            ? session.IsAwaitingInterlude ? new InterludeContinueEntry() : null
            : session.IsAwaitingNodeChoice ? new NodePickEntry(winner) : null;
        if (step is not null && GameHost.Instance.Play!.Apply(step))
            Publish(RunAnswerCodec.Encode(step));
    }

    // Number the answer the host just gave and hand it to everyone, with how the run stood after it.
    private void Publish(string[] wire)
    {
        _entries.Add(wire);
        Votes.Clear();
        var message = new NetMessage
        {
            Kind = "entry", Index = _entries.Count - 1, Answer = wire, Check = Check(GameHost.Instance.Play!),
        };
        foreach (var seat in Seats.Skip(1).Where(s => s.Connected))
            Send(seat.Peer, message);
        SaveTable();
    }

    // How the run stands, in one line two machines can compare: the room and every member's numbers, and the
    // fight down to its hash.
    public static string Check(RunPlayback play)
    {
        if (play.Session is not { } session)
            return "";
        var line = RunRecorder.Fingerprint(session.Run);
        if (play.PartyCombatDriver?.Current is { } fight)
            line += " | " + RogueDeck.Core.Combat.CombatStateHasher.ComputeHash(
                RogueDeck.Core.Combat.CombatStateSnapshotter.CreateSnapshot(fight.State))[..16];
        return line;
    }

    // ── WHAT ARRIVES ─────────────────────────────────────────────────────────────────────────────────────

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Receive(string json)
    {
        NetMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<NetMessage>(json, Json);
        }
        catch (JsonException)
        {
            return;
        }
        if (message is null)
            return;
        var from = Multiplayer.GetRemoteSenderId();
        if (Trace)
            GD.Print($"net-trace: in {message.Kind} from {from} index={message.Index} entries={_entries.Count} seat={LocalSeat}");
        if (Role == NetRole.Host)
            OnHostReceives(from, message);
        else if (Role == NetRole.Client && from == 1)
            OnClientReceives(message);
    }

    private void OnHostReceives(long peer, NetMessage message)
    {
        var seat = Seats.FindIndex(s => s.Peer == peer && s.Connected);
        switch (message.Kind)
        {
            case "hello":
                Welcome(peer, message);
                break;
            // A seat laid from a saved table keeps the hero it was saved with.
            case "seat" when seat > 0 && !Started && _saved is null:
                Seats[seat] = Seats[seat] with { Character = message.Character, Ready = message.Ready };
                BroadcastLobby();
                break;
            case "intent" when seat > 0 && Started && message.Answer is { } answer:
                TakeIntent(seat, answer);
                break;
            case "vote" when seat > 0 && Started && message.Vote is { } vote:
                TakeVote(seat, vote, message.Index ?? -1);
                break;
            case "resync" when seat > 0 && Started:
                SendWholeRun(peer, seat);
                break;
            case "sync" when seat > 0 && Started && message.Index is { } has:
                for (var index = Math.Max(0, has); index < _entries.Count; index++)
                    Send(peer, new NetMessage { Kind = "entry", Index = index, Answer = _entries[index] });
                Send(peer, new NetMessage { Kind = "votes", VoteTable = new Dictionary<int, string>(Votes) });
                break;
        }
    }

    // A player at the door. The same build and the same game document, or they could never stay in step; the
    // same player id as a seat that dropped out takes that seat back, run and all.
    private void Welcome(long peer, NetMessage hello)
    {
        var version = ProjectSettings.GetSetting("application/config/version").AsString();
        if (hello.Protocol != Protocol || hello.Version != version || hello.Content != GameHost.Instance.ContentHash)
        {
            Send(peer, new NetMessage { Kind = "refused", Text = $"The host plays version {version}; update to the same build." });
            return;
        }
        var returning = Seats.FindIndex(s => s.PlayerId == hello.PlayerId);
        if (returning > 0)
        {
            Seats[returning] = Seats[returning] with { Peer = peer, Connected = true, Name = hello.Name ?? Seats[returning].Name };
            Report($"{Seats[returning].Name} is back.");
            if (Started)
                SendWholeRun(peer, returning);
            BroadcastLobby();
            return;
        }
        if (_saved is not null)
        {
            Send(peer, new NetMessage { Kind = "refused", Text = "This table is waiting for the players of its saved run." });
            return;
        }
        if (Started || Seats.Count >= MaxSeats)
        {
            Send(peer, new NetMessage { Kind = "refused", Text = Started ? "The run has already begun." : "The table is full." });
            return;
        }
        Seats.Add(new Seat(peer, hello.Name ?? "Player", hello.PlayerId ?? peer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Character: null, Ready: false, Connected: true));
        Report($"{Seats[^1].Name} joined.");
        BroadcastLobby();
    }

    private void SendWholeRun(long peer, int seat) =>
        Send(peer, new NetMessage { Kind = "start", Start = _start, Seat = seat, Entries = [.. _entries] });

    private void OnClientReceives(NetMessage message)
    {
        switch (message.Kind)
        {
            case "refused":
                Report(message.Text ?? "The host turned us away.");
                Leave();
                break;
            case "lobby" when message.Seats is { } seats:
                Seats.Clear();
                Seats.AddRange(seats);
                LocalSeat = message.Seat ?? LocalSeat;
                EmitSignal(SignalName.LobbyChanged);
                break;
            case "votes" when message.VoteTable is { } table:
                _heard = 0;
                Votes.Clear();
                foreach (var (seat, vote) in table)
                    Votes[seat] = vote;
                EmitSignal(SignalName.LobbyChanged);
                break;
            case "start" when message.Start is { } start:
                // The whole run from its seed: a fresh start, or a way back in after falling out of step.
                _heard = 0;
                _start = start;
                Started = true;
                LocalSeat = message.Seat ?? LocalSeat;
                _entries.Clear();
                Votes.Clear();
                BeginRun(start);
                foreach (var wire in message.Entries ?? [])
                    if (!Follow(wire, check: null))
                        break;
                break;
            case "entry" when message.Answer is { } answer && message.Index is { } index:
                _heard = 0;
                if (index < _entries.Count)
                    break; // sent again for somebody who had missed it; this machine has it
                if (index > _entries.Count)
                {
                    AskForResync($"answer {index} arrived while {_entries.Count} was due");
                    break;
                }
                Follow(answer, message.Check);
                break;
        }
    }

    // Give one of the host's answers to this machine's run. Out of step — the run would not take it, or it
    // stands somewhere else afterwards — and the whole run is asked for again.
    private bool Follow(string[] wire, string? check)
    {
        if (GameHost.Instance.Play is not { } play)
            return false;
        var entry = RunAnswerCodec.Decode(wire);
        if (!play.Apply(entry))
        {
            AskForResync($"answer {_entries.Count} ({string.Join(" ", wire)}) did not fit this run");
            return false;
        }
        _entries.Add(wire);
        if (check is not null && Check(play) != check)
        {
            AskForResync($"after answer {_entries.Count - 1} this run stands at «{Check(play)}», the host's at «{check}»");
            return false;
        }
        return true;
    }

    private int _resyncs;
    public int Resyncs => _resyncs;

    // SILENCE IS A QUESTION TOO. A client that has heard nothing from the host for a while says how many answers
    // it has; the host sends whatever it is missing, and the votes as they stand — which is also how a client
    // that is merely waiting on a slow thinker knows the table is still there. Cheap: nothing is replayed.
    private double _heard;
    private const double SilenceBeforeAsking = 10;

    public override void _Process(double delta)
    {
        if (Role != NetRole.Client || !Started || GameHost.Instance.Play?.Session is not { IsComplete: false })
            return;
        _heard += Math.Min(delta, 0.5);
        if (_heard < SilenceBeforeAsking)
            return;
        _heard = 0;
        SendToHost(new NetMessage { Kind = "sync", Index = _entries.Count });
    }

    private void AskForResync(string why)
    {
        _resyncs++;
        GD.PushWarning($"net: out of step — {why}; asking the host for the whole run again");
        Report("Out of step with the host — catching up.");
        SendToHost(new NetMessage { Kind = "resync" });
    }

    // ── THE WIRE ─────────────────────────────────────────────────────────────────────────────────────────

    private void OnPeerConnected(long peer)
    {
        Patient(peer);
        if (Role == NetRole.Host)
            GD.Print($"net: peer {peer} connected");
    }

    private void OnPeerDisconnected(long peer)
    {
        if (Role != NetRole.Host)
            return;
        var seat = Seats.FindIndex(s => s.Peer == peer);
        if (seat < 0)
            return;
        if (Started)
        {
            // The seat stays: the run is theirs to come back to, and the host can answer for them meanwhile.
            Seats[seat] = Seats[seat] with { Connected = false };
            Votes.Remove(seat);
        }
        else
            Seats.RemoveAt(seat);
        Report($"A player left.");
        BroadcastLobby();
    }

    private void OnConnectedToServer()
    {
        Patient(1);
        Report(Started ? "Back at the table." : "Connected — waiting for the host.");
        SendHello();
    }

    private void SendHello() => SendToHost(new NetMessage
    {
        Kind = "hello",
        Protocol = Protocol,
        Version = ProjectSettings.GetSetting("application/config/version").AsString(),
        Content = GameHost.Instance.ContentHash,
        Name = LocalName ?? PlayerIdentity.Name ?? "Player",
        PlayerId = LocalId ?? PlayerIdentity.Id,
    });

    private void BroadcastLobby()
    {
        for (var index = 1; index < Seats.Count; index++)
            if (Seats[index].Connected)
                Send(Seats[index].Peer, new NetMessage { Kind = "lobby", Seats = [.. Seats], Seat = index });
        EmitSignal(SignalName.LobbyChanged);
    }

    private void BroadcastVotes()
    {
        var message = new NetMessage { Kind = "votes", VoteTable = new Dictionary<int, string>(Votes) };
        foreach (var seat in Seats.Skip(1).Where(s => s.Connected))
            Send(seat.Peer, message);
        EmitSignal(SignalName.LobbyChanged);
    }

    private void SendToHost(NetMessage message) => Send(1, message);

    // BNB_NET_TRACE=1 prints every message in and out — for following a table that stopped.
    private static readonly bool Trace = System.Environment.GetEnvironmentVariable("BNB_NET_TRACE") is not null;

    private void Send(long peer, NetMessage message)
    {
        if (Trace)
            GD.Print($"net-trace: out {message.Kind} to {peer} index={message.Index}");
        if (Multiplayer.MultiplayerPeer is null)
            return;
        RpcId(peer, MethodName.Receive, JsonSerializer.Serialize(message, Json));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // The one shape every message has; Kind says which fields mean anything.
    public sealed record NetMessage
    {
        public string Kind { get; init; } = "";
        public int? Protocol { get; init; }
        public string? Version { get; init; }
        public string? Content { get; init; }
        public string? Name { get; init; }
        public string? PlayerId { get; init; }
        public string? Text { get; init; }
        public string? Character { get; init; }
        public bool Ready { get; init; }
        public List<Seat>? Seats { get; init; }
        public int? Seat { get; init; }
        public StartMessage? Start { get; init; }
        public List<string[]>? Entries { get; init; }
        public int? Index { get; init; }
        public string[]? Answer { get; init; }
        public string? Check { get; init; }
        public string? Vote { get; init; }
        public Dictionary<int, string>? VoteTable { get; init; }
    }

    // `Save`: a run taken up again from the host's save rather than begun from the seed.
    public sealed record StartMessage(int Seed, string[] Party, MetaStateData Meta, string? Save = null);

    // What the host keeps of a table between evenings: the run as it stood, and who sat where.
    public sealed record TableSave(string Save, string[] Party, string[] PlayerIds, string[] Names, MetaStateData Meta);
}
