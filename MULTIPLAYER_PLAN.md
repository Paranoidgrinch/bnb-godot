# Multiplayer Plan: bis zu 4 Spieler in einem Run

**Status:** vorgeschlagen 2026-10-19. Danach kommt der dritte Charakter (Sorcerer's Apprentice).

Ein Spieler hostet, bis zu drei weitere verbinden sich, jeder spielt einen eigenen Charakter in einer gemeinsamen
Party. Betroffen sind alle drei Code-Repos: RogueDeck-Core (Party-Pfad reparieren, Räume pro Mitglied, Skalierung),
bnb-content (Party-Start aus Charakteren, Balance) und bnb-godot (Netzwerk, Lobby, Oberfläche).

## Entscheidungen (User, 2026-10-19)

1. **Verbindung: direkte IP.** Godot ENet, der Host nennt IP:Port (LAN, Portfreigabe, Tailscale/ZeroTier). Ein
   Relay mit Lobby-Code kann später dazukommen; das Protokoll wird so gebaut, dass nur der Transport wechselt.
2. **Karte: Abstimmung.** Jeder markiert einen Raum, die Mehrheit gewinnt, bei Gleichstand entscheidet der Host.
3. **Events: jeder für sich.** Alle sehen dasselbe Event, jeder wählt seine Option, die Wirkung trifft nur ihn.
   Kartenbelohnungen, Shop (eigenes Gold), Lagerfeuer und Schätze ebenso pro Spieler.
4. **Skalierung: Gegner-HP.** Faktor je weiterem Spieler, Startwert 0,75 (2 Spieler x1,75, 4 Spieler x3,25),
   danach per Bench getunt.

Aus `RogueDeck-Core/docs/party-deckbuilding.md` (2026-07-08) gelten weiter: gleichzeitige Spielerphase (jeder
beendet seinen Zug selbst, danach handeln die Gegner), alles pro Mitglied (Deck, Relikte, Tränke, Gold), ein
Mitglied auf 0 HP ist nur für den Kampf raus, der Run endet erst, wenn alle unten sind. Annahme ohne Rückfrage:
derselbe Charakter darf mehrfach gewählt werden (bei zwei Charakteren und vier Spielern geht es nicht anders).

## Bestand

Gebaut im Juli (party A1 bis D, C1, C2): `RunState.Party` mit 1 bis 4 `PartyMember`, gleichzeitige Spielerphase
(`SimultaneousTurnProcessor`, `PartyCombat`), ein Deck pro Mitglied, Relikte im Kampf pro Mitglied, Gegnerzielwahl
(`PartyEnemyTargeting`), mitgliedsbezogene Run-Effekte (`PushActiveMember`), `PartyInteractiveCombatDriver` im
`RunPlayback`, Save mit `Party`.

Geprüft am 2026-10-19 mit einem Probe-Run auf dem echten Content (Bürokrat + Hexe):

- **Kampf startet** korrekt: beide Helden mit 5 Karten und 3 Energie, Karten wirken auf den Gegner.
- **Zugende setzt den Kampf zurück.** Beendet ein Mitglied seinen Zug, steht der Kampf wieder am Anfang von
  Runde 1. Ursache: `PartyInteractiveCombatDriver.EndTurnFor` meldet "Zug übergeben" schon beim ersten Mitglied,
  `RunPlayback` setzt daraufhin die Replay-Basis auf einen Kampf-Snapshot, und `Drive` baut den Party-Kampf immer
  frisch auf, statt aus dem Snapshot weiterzumachen. Eingeführt mit `16b5ae2` (2026-09-05, Basis im Kampf).
- **Der Bot kann keinen Party-Kampf spielen** (`RunBot` bleibt im ersten Kampf stehen). Ohne ihn gibt es keine
  Simulation, keinen Marathon-Smoke und keinen Bench für Party-Runs.
- **Nach dem Kampf gilt alles nur für Mitglied 0.** Belohnungen, Events, Shop und Lagerfeuer laufen einmal für
  `run.Primary`. Die anderen bekommen nichts.
- **Die Party ist fest im Blueprint** (`RunStart.StartingParty`), nicht aus einer Charakterwahl. `RunMemberData`
  kennt weder `CombatActions` noch `CombatStatuses` (Passive der Hexe) noch den Charakter selbst, und
  `RunState.CharacterId` ist einer pro Run, also auch der Kartenpool (`RunCharacter.Exclusive`).

## Architektur: Lockstep über Antworten

Die Engine ist deterministisch, ein Run ist Seed + Start + Antwortfolge (genau das, was `RunRecorder` schon
speichert). Darum wird kein Spielzustand übertragen, nur Antworten:

- **Der Host ist die Autorität.** Ein Client schickt eine Absicht ("Mitglied 2 spielt Karte X auf Ziel Y"). Der
  Host prüft sie an seiner eigenen Session (passt sie zu einem offenen Prompt dieses Mitglieds?), hängt sie an die
  Antwortfolge und sendet sie mit laufender Nummer an alle. Jeder Rechner wendet die Folge in derselben
  Reihenfolge an und kommt zum selben Zustand.
- **Beitritt und Wiederverbinden:** Der Host schickt Content-Hash, Engine-Version, Seed, Party und die bisherige
  Antwortfolge. Passt Hash oder Version nicht, wird der Beitritt mit Begründung abgelehnt.
- **Desync-Erkennung:** Nach jedem Raum vergleicht jeder Client seinen `RunRecorder.Fingerprint` mit dem des
  Hosts. Bei Abweichung lädt der Client die Antwortfolge neu, und es entsteht ein Bugreport mit beiden Fingerprints.
- **Save:** Nur der Host speichert. Ein gespeicherter Multiplayer-Run wird vom Host wieder geöffnet, die Spieler
  verbinden sich neu und bekommen ihre Plätze (Zuordnung über eine Spieler-ID, wie in `RunLog`).

## Stand (2026-10-20)

| Phase | Stand |
|---|---|
| M0 | Fertig. Zugende setzt den Kampf nicht mehr zurück, Party-Kämpfe lassen sich aus einem Snapshot fortsetzen (Snapshot trägt jetzt die Teamphase). Der Würfel-Bot spielt Party-Kämpfe inklusive Charakter-Aktionen. Der Recorder schreibt alle Mitglieder in den Fingerprint und den Sitz zu jeder Kartenwahl. |
| M1 | Fertig. `RunPlayback.Start(..., party: [...])` baut die Party aus Charakter-IDs. Jedes Mitglied trägt Charakter, Aktionen und Passive, auch über Speichern/Laden. Mitglieder bekommen die Start-Status und Ressourcen der Begegnung (u. a. `the_applicant`). Angebote richten sich nach dem Charakter des aktiven Mitglieds. |
| M2 | Fertig. Event, Shop, Werkbank und Kampfbelohnung laufen pro Mitglied, jedes mit eigenem Zufallsstrom. Jedes Mitglied verbraucht nur seine eigenen Antworten (`ReplayEntry.Seat`), die Ankunftsreihenfolge ist egal. Die Session hält eine offene Frage pro Mitglied (`PendingSeats`, `PendingFor`, `PickFor`, ...). |
| M3 | Engine fertig. `Balance.PartyEnemyHealthPerExtraMember` (BnB: 0,75) und `Balance.PartyEnemyTargeting` (BnB: `Random`). Zufallsziel ist zustandslos pro Runde und Gegner und vorab abfragbar (`PartyCombat.IntendedTarget`). Validator prüft beide Werte. |
| M4 | Netzwerk-Kern fertig: `NetSession` (Autoload `Net`), Host/Join über ENet, UPnP-Versuch, Lobby, Start mit Host-Profil, Intent/Entry mit Prüfsumme, Resync, Wiedereinstieg über Spieler-ID, Abstimmung für Karte und Weitergehen. `RunPlayback.Apply(entry)` gibt jede Antwort durch dieselbe Tür wie ein Klick. |
| M5 | Grundgerüst fertig. Titel: "Play together" öffnet die Lobby (hosten, per Adresse beitreten, Charakter, Bereit, Start mit Seed). Der Kampf läuft über `ICombatView`/`ICombatSeat`: der eigene Held wie im Solo-Spiel, die Mitspieler daneben (HP, Block, Energie, "choosing…"/"turn ended"), jede Gegnerabsicht mit Ziel ("→ you" / "→ Name"), "Waiting for …" nach dem eigenen Zugende. Räume zeigen die eigene Frage (`ViewSeat`), sonst "Waiting for …". Karte und Weitergehen sind Abstimmungen (Stimmen als Textzeile über der Karte). HUD, Inventar und Relikte zeigen den eigenen Helden. Verlassen über das Menü verlässt den Tisch. |
| M6 | `tools/smoke-mp.sh`: 2 bis 4 Godot-Prozesse spielen 300 bis 800 Antworten im Gleichschritt, ohne Resync. `--screen` zeichnet dabei in jedem Prozess den echten Run-Bildschirm (Ausnahmen gelten als Fehler), `--shots` lässt den Host im Fenster laufen und speichert je ein Bild von Kampf, Karte, Belohnung, Event und Shop. Dazu `PartyLockstepTests` in der Engine. |

Beobachtung Balance: Mit dem Würfel-Bot und 0,75 sind Partys noch deutlich stärker (Solo stirbt im Schnitt nach ~6 Kämpfen, 2er-Party nach ~10 bis 14, 4er-Party nach ~28). Gegnerschaden verteilt sich auf mehr Helden. Echte Spieltests entscheiden, ob der Faktor steigen oder auch Schaden skalieren soll.

Seit dem zweiten Durchgang (2026-10-20) erledigt:
- Relikte der Mitglieder reagieren auf Party-Ereignisse und wirken im Kampf ihres Besitzers; Eröffnungen ("nächster Kampf beginnt mit ...") gehören dem Mitglied, das sie bekommen hat, auch über Speichern.
- Verbrauchsgegenstände im Party-Kampf benutzt, wer sie trägt.
- Eine Kartenfrage im Kampf beantwortet nur der Held, dem die Karte gehört; die anderen sehen "Waiting for X to choose".
- Schadensvorschau beim Zielen und Schritt-Berichte (Treffer-Animationen, Log) auch im Party-Kampf.
- Stimmen als Marker unter den Kartenknoten.
- Verbindung: 30 s Geduld statt ENet-Standard, automatisches Wiederverbinden des Clients, fehlende Antworten werden nachgeschickt, ein Client meldet sich nach 10 s Stille.
- Gespeicherte Tische: der Host speichert den Run, wann immer niemand mitten in einer Frage steckt; "Continue the saved table" wartet auf dieselben Spieler (Spieler-ID) und setzt für alle fort.
- Smoke: `--drop` (Verbindungsabbruch und Rückkehr), `--resume` (zweite Sitzung aus dem Spielstand).

Bekannte Lücken:
- Kampf-Beiträge von Relikten in Code-Form (`CombatContributions`) werden nur für den ersten Helden projiziert; BnB benutzt keine.
- Mehrspieler-Runs werden nicht aufgezeichnet (kein Run-Log-Upload).
- Kein Relay: Spieler in verschiedenen Netzen brauchen Portfreigabe oder ein VPN wie Tailscale.

## Phasen

Jede Phase ist ein eigener Commit mit grünen Tests. Ein Solo-Run muss in jeder Phase byte-gleich bleiben
(Golden Runs, `--smoke-marathon`).

### M0 Party-Pfad reparieren (RogueDeck-Core)

- **M0a Zugende.** "Zug übergeben" erst, wenn die ganze Spielerphase vorbei ist und die Gegner gehandelt haben.
  `PartyInteractiveCombatDriver.Drive` lernt, aus einem Kampf-Snapshot weiterzumachen (wie der Solo-Treiber).
  Test: Party-Kampf über drei Runden mit Zugende jedes Mitglieds, ohne Rücksetzer, mit Save/Resume mitten im Kampf.
- **M0b Bot für Partys.** `RunBot` spielt jedes Mitglied (gleicher Planer, ein Zug pro Mitglied). Danach laufen
  `--smoke-marathon`, `tools/simulate.sh` und der Act-Bench auch mit Party.
- **M0c Recorder.** Antworten tragen das Mitglied (`CombatPlayEntry.Member` gibt es schon, Picks und Shop noch
  nicht). Replay-Test für einen Party-Run.

### M1 Party aus der Charakterwahl (Core + bnb-content)

- `RunPlayback.Start` nimmt eine Liste von Charakter-IDs (1 bis 4). Jedes Mitglied bekommt den Start seines
  Charakters: HP, Deck, Gold, Startrelikte, `CombatActions`, `CombatStatuses`.
- `PartyMember.CharacterId`. Kartenpools, Upgrades und alles, was heute `run.CharacterId` liest, fragen das
  Mitglied. `RunState.CharacterId` bleibt als Rückfall auf Mitglied 0.
- Hexe als Mitglied 1: Queue-Zone, Hexed und Misfortune pro Kämpfer prüfen. Je Charakter ein Test, der beide
  Charaktere in einer Party kämpfen lässt.

### M2 Räume pro Mitglied (Core, das größte Stück)

Damit vier Spieler gleichzeitig shoppen können, darf Spieler 3 nicht warten müssen, bis Spieler 1 fertig ist.
Vorschlag:

- Beim Betreten eines Raums wird das Angebot jedes Mitglieds sofort ausgewürfelt, in Mitgliedsreihenfolge
  (Kartenbelohnung, Shop-Regal, Event-Optionen, Schatz). Danach hängt nichts mehr von der Reihenfolge ab, weil
  jeder nur sein eigenes Gold und Deck verändert.
- Die Session parkt nicht mehr an genau einem Prompt, sondern an **einem offenen Prompt pro Mitglied**
  (`PendingFor(member)`). Antworten werden pro Mitglied verbraucht. Der Raum ist erledigt, wenn alle fertig sind.
- Die Raumwahl auf der Karte bleibt eine einzige Antwort (`NodePickEntry`); die Abstimmung findet im
  Netzwerk-Layer statt, der Host trägt das Ergebnis ein.
- Mitglieder, die im letzten Kampf gefallen sind, bekommen trotzdem ihre Belohnung (Vorschlag, siehe offene
  Punkte).

Risiko: Das ist ein Eingriff in `InteractiveRunSession` und in die Node-Resolver (Event, Shop, Rest, Treasure,
Reward). Eine Party mit einem Mitglied muss exakt wie heute laufen.

### M3 Skalierung und Zielwahl (Core + bnb-content)

- `Balance.PartyEnemyHealthPerExtraMember` (Standard 0, für BnB 0,75), angewendet bei der Projektion des Kampfs.
- Gegnerzielwahl: Die Absicht zeigt, wen der Gegner angreift. Startregel: zufällig, deterministisch über den
  Kampf-Seed, nur lebende Mitglieder. Bosse mit gezielten Mechaniken prüfen.
- Bench für 2 und 4 Spieler, sobald M0b steht.

### M4 Netzwerk (bnb-godot)

- `NetSession` (Autoload) über Godot `ENetMultiplayerPeer`. Standardport 24642, UPnP-Versuch über Godots `UPNP`,
  bei Fehlschlag Hinweis auf Portfreigabe/Tailscale.
- Nachrichten: `Hello` (Name, Spieler-ID, Version, Content-Hash), `Lobby` (Plätze, Charakter, bereit), `Start`
  (Seed, Party), `Intent` (Client an Host), `Entry` (Host an alle, mit Nummer), `Vote`, `Fingerprint`, `Resync`,
  `Chat` (optional).
- Der Host verarbeitet Absichten nacheinander auf dem Hauptthread; `GameHost` bekommt eine Schicht, die lokale
  Klicks nicht direkt an `RunPlayback` gibt, sondern über `NetSession` (im Solo direkt durchgereicht).
- Verbindungsabbruch: Das Mitglied des Spielers wartet ("getrennt"). Der Host kann nach einer Wartezeit den Zug
  für ihn beenden, damit die anderen nicht hängen.

### M5 Oberfläche (bnb-godot)

- Titelbildschirm: "Hosten" und "Beitreten" (IP:Port, Name). Lobby mit vier Plätzen, Charakterwahl pro Platz,
  Bereit-Häkchen, Start nur durch den Host.
- Kampf: bis zu vier Helden in der Arena, die eigene Hand unten, die anderen mit HP, Block, Energie, Status und
  "Zug beendet". Die Absicht des Gegners zeigt sein Ziel. Die sechs festen Regionen des Kampfbildschirms bleiben,
  die Heldenreihe wird dafür kompakt (Prüfung wie `--smoke-crowd`, bei 4 Helden und 5 Gegnern).
- Karte: Stimmen als Marker mit Spielerfarbe an den Räumen.
- Belohnung, Shop, Event, Lagerfeuer: jeder sieht seins, dazu eine Leiste "wartet auf: Spieler 2, Spieler 4".
- Archiv, Run-Log und Bugreport: pro Spieler lokal; der Run-Log enthält die Party.

### M6 Tests und Werkzeuge

- Headless-Smoke mit zwei Godot-Instanzen auf localhost (`--smoke-mp`): Host und Client spielen über den Bot
  einen Akt, beide Fingerprints müssen nach jedem Raum gleich sein.
- Engine-Test: vier unabhängige Treiber geben verschränkte Antworten, das Ergebnis hängt nur von der Reihenfolge
  am Host ab.
- `ingest.py` in bnb-runs liest Party-Recordings (eine Zeile pro Run, Spalte mit der Party).

## Offene Punkte (später entscheiden)

- Bekommt ein im Kampf gefallenes Mitglied seine Belohnung? (Vorschlag: ja, sonst rutscht es weiter ab.)
- Karten, die einen Verbündeten treffen (Block geben, heilen): heute zielen BnB-Karten nur auf sich selbst oder
  Gegner. Neue Karten dafür wären Content für später.
- Zeitlimit pro Zug für Spieler, die nicht reagieren.
- Relay mit Lobby-Code auf dem 24/7-Rechner.
