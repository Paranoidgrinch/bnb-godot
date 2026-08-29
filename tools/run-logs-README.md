# Run-Simulator — wo die Logs landen und wie du Runs startest

## Wo alles rauskommt
```
~/Desktop/bnb-run-logs/                     ← genau dieser Ordner
├── ANLEITUNG.md                            ← diese Datei (wird bei jedem Batch aufgefrischt)
└── 20260829-191520/                        ← ein Ordner pro Batch, benannt nach Startzeit
    ├── run-0100.log                         ← ein Log pro Run, benannt nach seinem Seed
    ├── run-0101.log
    ├── build.log                            ← der dotnet-Build vor dem Batch
    └── summary.txt                          ← ZUERST hier reinschauen
```
Ein Run = ein eigener Godot-Prozess. Stürzt einer ab, kostet das genau diesen einen Run, und sein Log endet
an der Absturzstelle.

## Runs starten
Immer aus `~/bnb-godot`:
```bash
cd ~/bnb-godot

tools/simulate.sh                    # 20 Runs, 400 HP, 4 Prozesse gleichzeitig
tools/simulate.sh 100                # 100 Runs
tools/simulate.sh 100 --jobs 8       # ... 8 gleichzeitig (schneller, mehr CPU)
tools/simulate.sh 50 --immortal      # 9999 HP: niemand stirbt → tiefste Content-Abdeckung (Akt II/III)
tools/simulate.sh 50 --real          # die echten Werte des Spiels (die meisten sterben in Akt I)
tools/simulate.sh 50 --health 250    # eigener Wert
tools/simulate.sh 30 --seed-from 500 # Seeds 500..529 statt 1..30
tools/simulate.sh 30 --out ~/woanders   # anderer Zielordner
```
Faustzahlen: ~50 s pro Run bei 400 HP, ~4 min bei `--immortal` (der läuft bis zum Ende von Akt III).
Bei `--jobs 8` also z.B. 100 Runs à 400 HP in gut 10 Minuten.

**Einen einzelnen Run** (Ausgabe direkt im Terminal):
```bash
godot --headless -- --sim --sim-seed 42 --sim-health 400
godot --headless -- --sim --sim-seed 42 --sim-immortal
```

## Einen Run exakt nachspielen
Der Seed ist die vollständige Reproduktion — gleiche Seed + gleiche Health = Zug für Zug derselbe Run:
```bash
godot --headless -- --sim --sim-seed 107 --sim-health 400
```
Der Seed steht im Dateinamen (`run-0107.log`) und in der ersten Zeile jedes Logs.

## summary.txt lesen
1. **eine Zeile pro Run** — Seed, Exit-Code, Ergebnis (`result=Victory/Defeat`, Räume, Kämpfe, HP, Sekunden)
2. **outcomes** — wie oft Sieg/Niederlage
3. **runs worth reading** — NUR das ist verdächtig: ein Engine-Fehler, ein Step der geworfen hat, ein Zug der
   nicht endet, eine Wand, eine Exception. Exit-Code 0 = sauber, 1 = anschauen.
4. **content the batch touched** — welche Räume/Encounter wie oft dran waren, wie viele verschiedene Karten
   gespielt, Event-Entscheidungen getroffen und Angebote gezogen wurden. Das ist das Varianz-Maß:
   wiederholt sich die Liste über mehrere Batches, hast du den Content gesehen.

Zwei Meldungen sind ausdrücklich KEIN Fund und werden gefiltert: eine Karte, die die Regeln ablehnen
(ein Zufallsspieler spielt eben auch Flüche), und eine Karte, die anhält um ihre eigene Frage zu stellen.

## Ein einzelnes Log lesen
```
sim: seed=11 character=… hp=400/400 deck=12 relics=…      ← Kopf: womit der Run angefangen hat
[  5.5s   51] ROOM act 1 r2c0 (city_normal_seal_01) combat hp=29/70 gold=73 deck=12 relics=0
  FIGHT act 1 r2c0 (city_normal_seal_01) vs wax_notary(48)
    play paper_cut -> wax_notary (hp 29, hand 5)
    end turn 1 (hp 29, hand 4)
  fight ends: hp=16/70 after 6 turns
  fork -> r3c1 shop (of shop combat)                       ← welche Wege es gab, welcher genommen wurde
  choice [shop] -> buy-deskward (of buy-… reroll leave)
  pick [reward-card] -> Privy Seal (of 3)
    | …                                                    ← die Erzählung der Engine selbst
sim-result: seed=11 result=Defeat acts=1 rooms=22 fights=12 hp=0/400 problems=0 error=none …
```

## Aufräumen
Die Ordner sind reiner Text und klein (~50–500 KB pro Batch). Alte einfach löschen:
```bash
rm -rf ~/Desktop/bnb-run-logs/20260829-*
```
