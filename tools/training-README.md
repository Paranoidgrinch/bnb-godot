# Balance-Training — Runner züchten und lesen

## Die Frage, auf die trainiert wird
Jeder Runner startet mit **9999 HP** (nichts kann ihn töten) und läuft durch das ganze Spiel. Gewertet wird
**wie viel Leben er bis zur Ankunft am Akt-III-Boss verloren hat** — wenig verloren = guter Runner. Wer dort
nie ankommt, ist schlechter als jeder, der ankommt, egal wie wenig er unterwegs verloren hat.

Damit ist die Zahl gleichzeitig die Balance-Antwort: *was kostet dieses Spiel einen Spieler, der es gut spielt?*

## Ein Runner ist eine Policy
17 Gewichte (`SimPolicy` in `scripts/RunSimulator.cs`) entscheiden alles, was ein Spieler entscheidet:
- **WDamage / WBlock / WStatus / WDraw / WResource / WCost** — was eine Karte wert ist, nach dem, was ihr
  Programm tut (Schaden, Block, Status, Ziehen, Ressourcen) und was sie kostet.
- **EndTurnBelow** — ab welchem Kartenwert der Zug lieber beendet wird.
- **TargetLowestHp** — 1 = den Schwächsten erledigen, 0 = auf den Stärksten dreschen.
- **PathCombat / PathElite / PathShop / PathRest / PathEvent / PathTreasure** — welchen Raum er wählt.
- **RewardSkip / ShopBuy / EventLate** — Belohnung ablehnen, Gold ausgeben, welche Tür.

## Training starten
```bash
cd ~/bnb-godot
tools/train.py                                       # 5 Generationen × 8 Runner × 2 Seeds
tools/train.py --generations 10 --population 12 --seeds 3 --jobs 8
tools/train.py --resume ~/Desktop/bnb-balance-training/<stamp>    # vom bisher Besten weiterzüchten
tools/train.py --health 200 --generations 2 --population 3 --seeds 1   # nur zum Ausprobieren, schnell
```
Jede Generation: die besten `--survivors` (Standard 3) überleben unverändert, der Rest sind ihre Mutationen
(`--sigma` = Mutationsgröße). Alle Runner einer Generation spielen **dieselben Content-Seeds**, damit der
Vergleich fair ist.

**Dauer:** ein unsterblicher Run geht durch alle drei Akte — rechne mit 4–10 min pro Run. 8 Runner × 2 Seeds
= 16 Runs pro Generation; bei `--jobs 8` also ~15 min je Generation. Fang klein an.

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
`score` = mittlerer HP-Verlust bis zum Akt-III-Boss. Ein Score über 1.000.000 heißt: dort nie angekommen.

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
