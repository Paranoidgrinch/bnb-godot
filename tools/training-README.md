# Balance-Training — Runner züchten und lesen

## ⚠⚠ ZWEI FRAGEN — und seit B6 ist die ECHTE die Voreinstellung

```bash
tools/train.py                      # clearance: ECHTES Leben, gewertet wird "hat er Akt IV geräumt?"
tools/train.py --question damage    # die alte Frage: 9999 HP, gewertet wird genommener Schaden
```

**`--question clearance` (Standard).** Der Runner läuft mit dem Leben, das das Spiel autoriert hat, und stirbt
auch daran. Gewertet wird pro Seed, ob er durch den Ziel-Akt gekommen ist — Standard **Akt IV**, weil das
Versprechen des Entwurfs lautet: *jeder Seed ist bis zum Ende von Akt IV schaffbar*. Akt V ist die Kirsche.
Gleichstand bricht nach gelaufener Strecke und Restleben, nie nach genommenem Schaden.

**Warum die alte Frage nicht reicht:** sie belohnt einen Läufer, der nie angreift. Seit der Bewerter Größen
sieht (B3), findet die Zucht diesen Ausweg sofort — der beste Runner der Schaden-Zucht lernte
**`WDamage = −1,84`**. Wer nichts tötet, wird nicht zurückgeschlagen; er braucht nur länger. Auf die
Räum-Frage gezüchtet, räumt so einer gar nichts.

## Der Champion (`--champion`, B5)

```bash
tools/train.py --champion --target-act 1        # Champions züchten
roguedeck-bot ... --champion --policy p.json    # einen laufen lassen
```
Er entscheidet jeden Zug, indem er den Kampf **gabelt**, die Karte auf der Kopie spielt, die Gegner antworten
lässt und anschaut, was übrig ist. Sein einziger Knopf ist **`Aggression`** (0 = den Zug überleben, 1 = den
Gegner leeren); was er NIMMT, bewertet er weiter wie jeder Politik-Läufer. Kostet ~4,5× einen normalen Lauf
(83–107 s statt ~21 s unsterblich) und ist dafür messbar der bessere Spieler: **488 statt 790 Schaden bis zum
Akt-I-Boss**, drei von drei ganzen Spielen gewonnen.

**Er plant den GANZEN ZUG, nicht die nächste Karte.** Gesucht wird über Reihenfolgen: an jedem Punkt darf der
Zug aufhören (und wird dann bewertet) oder noch eine Karte nehmen. Bezahlbar ist das, weil ein Kampf ein
*deterministisches Rätsel* ist — der Zufall hängt an Seed und Schrittzahl, die Gegnerabsicht ist eine Funktion
des Zustands, und `CombatStateHasher` gibt jeder Stellung einen Fingerabdruck: zwei Reihenfolgen, die auf
derselben Stellung landen, werden nur einmal durchsucht. Gemessen: **~170 Gabeln für einen Drei-Karten-Zug**,
ein Akt-I-Lauf geht von ~6 s auf ~20 s.

⚠ **„Kämpfen ist damit automatisiert" gilt genau einen Zug tief.** Innerhalb des Zuges ist die Suche
erschöpfend; darüber hinaus wird die Stellung weiterhin geschätzt (das Rennen), nicht zu Ende gespielt.

⚠ Bewertet wird ein Zug als **Rennen**, nicht als Kontostand: *dieser Zug hat so viel von ihnen genommen und
so viel von mir gekostet — wer ist bei dem Tempo zuerst unten?* Die erste Fassung rechnete Kontostand und
hörte auf, Karten zu spielen: der allererste Gegner des Spiels bestraft Handeln (nichts tun kostete 0 Leben,
ein Sechs-Schaden-Stich kostete 15), und auf einer Skala, auf der Stillstehen gratis ist, war das richtig.

**⚠⚠ Stand 2026-09-19: auch der Champion räumt Akt I auf einem echten Körper nicht** — er stirbt in Raum
13–18 von 22. Aber der Abstand ist jetzt gezählt: ~5 Leben je Kampf früh in Akt I, ~18 zurück je Rastplatz.
Die nächste benannte Lücke ist **der Läufer kann keinen Trank trinken** (er benutzt nie ein Consumable).

## Welche Seeds keiner schafft

```bash
tools/unbeaten.py --runs 50 --target-act 4 --policies ~/Desktop/bnb-balance-training/*/best-policy.json
```
Spielt jede Politik über jeden Seed, echtes Leben, und berichtet, **welche Seeds kein Läufer durch den
Ziel-Akt bringt — mit dem Raum, in dem der weiteste Versuch gestorben ist.** Das ist der Bericht, den V-7
haben wollte. Solange der beste Runner Akt I nicht räumt, listet er alles und sagt damit nur, dass der
Läufer noch kein Spieler ist.

## Die alte Frage im Detail (`--question damage`)
Jeder Runner startet mit **9999 HP** (nichts kann ihn töten) und läuft durch das ganze Spiel. Gewertet wird
**wie viel Schaden er bis zur Ankunft am Boss eines bestimmten Akts insgesamt genommen hat** — wenig = guter
Runner. Wer dort nie ankommt, ist schlechter als jeder, der ankommt, egal wie wenig er unterwegs eingesteckt
hat.

**Welcher Akt das ist, sagt `--target-act`** — Standard ist der letzte, den das Spiel hat (seit V-0 **Akt V**;
die Zahl steht als `LAST_ACT` oben in `tools/train.py` und wandert mit dem Content). Früher war Akt III fest
verdrahtet, weil Akt III das Ende war; die `sim-fitness:`-Zeile nennt inzwischen selbst keinen Akt mehr,
sondern nur noch die Tabelle über alle.

**Akt V ist seit V-6 kein Platzhalter mehr** (2026-09-09: Nisaba, Inanna, Nanshe, Nanna-Sin, Utu und Enlil
sind ausgeschrieben). Die frühere Warnung, man messe sinnvoll weiter mit `--target-act 4`, ist damit erledigt:
der Standard `--target-act 5` züchtet gegen das fertige Spiel.

**Aufsummierter Schaden, nicht Rest-HP.** Es gibt keine Vollheilung nach einem Akt — aber der Content heilt
sehr wohl (Relikte, Rastplätze, und die Tür `perpetual_borrower`/settle in Akt II heilt *auf voll*). Rest-HP
würde damit einen Runner belohnen, der zufällig durch eine Heiltür gelaufen ist, statt zu messen, was das
Spiel kostet. Beispiel aus einem echten Run (Seed 1000): Rest-HP sagte 540 verloren — tatsächlich genommen
hatte er **1075** Schaden, 535 davon waren weggeheilt worden.

In `sim-fitness:` steht deshalb beides plus die Aufschlüsselung pro Akt:
```
deepestActBoss=3  damageTaken=1182  healed=535  actBossDamage=1:137,2:538,3:1075  actBossHp=1:9862,2:9461,3:9459
```
`actBossDamage` ist kumulativ — Akt I kostete 137, Akt II weitere 401, Akt III bis zum Boss weitere 537.
`deepestActBoss` ist der tiefste Akt, dessen Boss-Raum der Run überhaupt betreten hat; der Trainer liest
seinen Wert aus `actBossDamage[--target-act]` und wertet ein fehlendes Feld als „nicht angekommen".

Damit ist die Zahl gleichzeitig die Balance-Antwort: *was kostet dieses Spiel einen Spieler, der es gut spielt?*

## Ein Runner ist eine Policy
17 Gewichte (`BotPolicy` in `RogueDeck.Bot`; bis R3 hieß das `SimPolicy` und saß in `scripts/RunSimulator.cs`)
entscheiden alles, was ein Spieler entscheidet:
- **WDamage / WBlock / WStatus / WDraw / WResource / WCost** — was eine Karte wert ist, nach dem, was ihr
  Programm tut (Schaden, Block, Status, Ziehen, Ressourcen) und was sie kostet. **Seit B1 wiegen dieselben
  sechs Gewichte auch, was der Runner ANNIMMT** — Belohnungskarte, Relikt, Regal im Laden —, nicht mehr nur,
  was er aus der Hand spielt. Ein Relikt wird ohne den Kostenterm gewogen (es wird nicht aus einem Zug bezahlt).
- **EndTurnBelow** — ab welchem Kartenwert der Zug lieber beendet wird.
- **TargetLowestHp** — 1 = den Schwächsten erledigen, 0 = auf den Stärksten dreschen.
- **PathCombat / PathElite / PathShop / PathRest / PathEvent / PathTreasure** — welchen Raum er wählt.
- **RewardSkip** — wie wählerisch er ist. ⚠ **Die Bedeutung hat sich mit B1 geändert:** vorher hieß > 0,5
  „lehne alles Ablehnbare ab", jetzt ist es eine Schwelle auf den DECK-RANG — 0 nimmt alles, 1 nimmt nur, was
  besser ist als jede Karte im Deck. Gewichte, die vor B1 gezüchtet wurden, meinen mit dieser Zahl also etwas
  anderes als der Runner heute; sie müssen neu gezüchtet werden. **Nur Karten werden je abgelehnt** — ein
  Relikt und ein Angebot ohne Identität (Gold, Heilung) werden immer genommen.
- **RestBelow** — unter welchem Anteil vom vollen Leben eine heilende Tür allem anderen vorgezogen wird.
  ⚠⚠ Es gibt dieses Gen, weil ein Rastplatz „leave" sagt wie ein Laden — und der Läufer deshalb **in der
  ganzen Geschichte dieses Projekts kein einziges Mal gerastet hat**. Auf 9999 HP kostet das nichts; erst die
  echte Frage (B6) hat es sichtbar gemacht.
- **Aggression** — nur der Champion liest es (siehe unten).
- **ShopBuy / EventLate** — wie eifrig Gold ausgegeben wird (**was** gekauft wird, entscheidet seit B1 derselbe
  Bewerter, bei Gleichstand das Billigere), und welche Tür — Türen weiterhin nach ihrer POSITION, nicht nach
  ihrer Wirkung; das ist B2.

## Training starten
```bash
cd ~/bnb-godot
tools/train.py                                       # 5 Generationen × 8 Runner × 2 Seeds
tools/train.py --generations 10 --population 12 --seeds 3 --jobs 8
tools/train.py --target-act 3                         # zu einem FRÜHEREN Akt-Boss messen
tools/train.py --resume ~/Desktop/bnb-balance-training/<stamp>    # vom bisher Besten weiterzüchten
tools/train.py --health 200 --generations 2 --population 3 --seeds 1   # nur zum Ausprobieren, schnell
```
Jede Generation: die besten `--survivors` (Standard 3) überleben unverändert, der Rest sind ihre Mutationen
(`--sigma` = Mutationsgröße). Alle Runner einer Generation spielen **dieselben Content-Seeds**, damit der
Vergleich fair ist.

**Dauer:** seit R1–R5 und der Umstellung des Trainers auf den Konsolen-Läufer (2026-09-18) kostet ein
unsterblicher Lauf durch alle fünf Akte **rund 20–40 s** statt 10–20 min. Gemessen: 4 Generationen × 8 Runner
× 2 Seeds = 64 Läufe in **~5 min** bei `--jobs 6`.

**Wie gezüchtet wird:** ein `roguedeck-bot`-Prozess spielt ALLE Seeds eines Runners; mehrere Runner laufen
nebeneinander. `--jobs` zählt weiterhin gleichzeitige LÄUFE, nicht Prozesse. **`--godot`** züchtet wie früher
durch das Spiel — ein Godot je Lauf, durch das Replay-Modell. Das ist der Rückweg, falls je bezweifelt wird,
dass beide Wirte denselben Lauf gehen; `tools/golden.sh` ist das, was es behauptet.

⚠⚠ **Die Fitness misst nicht, was du glaubst.** Sie zählt *genommenen Schaden auf dem Weg zum Boss, bei
9999 HP* — und seit der Bewerter (B3) Größen sehen kann, findet die Suche den Ausweg sofort: der beste Runner
der letzten Zucht hat **`WDamage = −1,84`** gelernt, also „greif lieber nicht an". Wer nichts tötet, wird
nicht zurückgeschlagen; er braucht nur länger. Bis **B6** die Frage austauscht (echtes Leben, echter Tod,
gewertet wird *hat er Akt IV geschafft*), züchtet man gegen diesen Ausweg an.

## Wo alles rauskommt
```
~/Desktop/bnb-balance-training/
├── ANLEITUNG.md                    ← diese Datei
└── 20260829-2030/
    ├── best-policy.json            ← der beste Runner bisher (wird nach jeder Generation aktualisiert)
    ├── leaderboard.csv             ← jede Generation, jeder Runner, sein Score — für ein Diagramm
    └── gen-00/
        ├── g0-p3.json              ← die Policy
        ├── g0-p3-seed1000.log      ← ihr vollständiges Run-Log (wie bei den normalen Sim-Runs)
        └── ranking.json            ← die Rangliste dieser Generation
```
`score` = mittlerer aufsummierter Schaden bis zum Boss des Ziel-Akts (`--target-act`). Ein Score über 1.000.000 heißt: dort nie angekommen.

## Den besten Runner ansehen
```bash
godot --headless -- --sim --sim-seed 1000 --sim-immortal \
  --sim-policy ~/Desktop/bnb-balance-training/<stamp>/best-policy.json
```
Im Log steht pro Raum `cost=` — genau wie viel Leben dieser Raum gekostet hat. Das ist die Balance-Kurve:
```bash
grep -h "^\[" gen-*/g*-seed*.log | sed 's/.*ROOM \(act [0-9]*\) [^ ]* (\([^)]*\)).* cost=\([0-9-]*\).*/\1 \2 \3/' \
  | awk '{s[$1" "$2]+=$3; n[$1" "$2]++} END{for (k in s) printf "%-45s %6.1f hp (%d×)\n", k, s[k]/n[k], n[k]}' \
  | sort -k2 -rn | head -30
```
Das sagt dir, **welcher Encounter die meisten HP kostet** — gemittelt über alle Runner, die ihn gesehen haben.
