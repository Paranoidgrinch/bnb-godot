# Playtest-Feedback 3 — Arbeitsplan

Stand 2026-10-25. 24 Punkte aus eigenen Testrunden (Version 0.3.0). Jeder Punkt hat ein Tor; er gilt erst als
fertig, wenn das Tor steht. Reihenfolge: erst Bugs, dann der Kampfbildschirm (Log zuerst, weil B4/B5 Platz und
Informationen dorthin verschieben), dann Kämpfe/Belohnungen, zuletzt die zwei großen Content-Durchgänge
(Kartentexte, General-Pool), die je einen eigenen Plan mit Beispielen zur Durchsicht bekommen.

Repos: Core (Engine), bnb-content (Daten/Converter), bnb-godot (UI). Golden-Set wird nach jedem Content-Block neu
aufgenommen, mit Begründung im Commit.

## Befunde vorab (gemessen 2026-10-25)

- Kartentexte: Median 18 Wörter, Schnitt 19,8, 344 von 880 Karten über 20 Wörter (StS2: etwa 10).
- Event-Relikte: 79 Event-Optionen geben ein Relikt, 46 davon ohne jeden Preis. 35 dieser 46 sind Schatzräume
  (`treasure:<akt>-N/open`), die technisch als Events laufen; 11 sind echte Events (u. a. `witness_queue/first`,
  `stamp_wheel/continue`, `sealed_offer/continue`, sieben Akt-IV-Events).
- Flank: der Code fragt nur, wenn ein zweiter lebender Gegner da ist UND genug Authority. Ein Test für Flank fehlt.
- Regelbox: `DecideRules` legt bei Elite/Boss jeden Mechanik-Status von Gegnern UND Held in die Box; Status, die
  der Held in jedem Kampf trägt, sind ausgenommen, Status aus Relikten/Karten des Helden aber nicht.

## A — Bugs

**A1 Flank fragt nicht nach Authority.** Im Probe nachstellen (2 Gegner, 1+ Authority, Karte spielen) und auf
eine Wahl prüfen. Verdacht: `countTargets` über `otherEnemies` ohne Quelle/Ziel im Kontext liefert 0. Alle 27
„You may spend“-Karten mit demselben Test durchgehen. Tor: Test je Karte, rot ohne Fix.

**A2 Relikte im Inventar vertikal verzogen als Text.** Inventar mit 1/10/40 Relikten aufnehmen. Tor: Smoke-
Screenshot; Relikt-Kachel quadratisch, Name lesbar in einer Zeile.

**A3 Das Spiel-Log reparieren.** Heute: `Run.Log.TakeLast(60)` als englische Engine-Sätze. Neu: pro Runde, in
Auflösungsreihenfolge, was gespielt wurde und was daraus folgte („Flank → 10 Schaden an X (4 geblockt) →
Authority −1 → 8 Schaden an Y“), inklusive ausgelöster Relikte/Status und interner Marker (siehe B5). Quelle sind
die Trace-Steps, die SessionEffects schon liest. Tor: Smoke spielt 3 Runden mit 2 Gegnern; jede Karte und jede
Gegneraktion steht im Log mit ihren Folgen in der Reihenfolge der Engine.

## B — Kampfbildschirm

**B1 Karten größer.** Handkarte deutlich größer als 134×190 (Ziel ~170×240 auf 1280×720). Tor: Screenshot bei
5 und 10 Handkarten, nichts überlappt Hero-/Gegnerpanels.

**B2 Fächer: mehr Überlappung, weiter aufgefächert.** Mehr Karten auf der Hand ohne Gedränge; Hover hebt die
Karte vollständig heraus. Tor: 10 Karten passen in die Handzone; Smoke-Hover misst die gehobene Karte ganz sichtbar.

**B3 Zieh-/Ablagestapel kleiner, tiefer in die Ecken.** Platz geht an die Hand. Tor: Screenshot.

**B4 Charakter-Tags weg.** „The Applicant“, die Lieutenant-Marke und künftige solche Tags erscheinen nicht mehr
als Chip (Status, die der Held in jedem Kampf trägt). Tor: Smoke listet die Chips des Helden, keiner davon ist ein
Jeder-Kampf-Status.

**B5 Interne Marker aus der Effektleiste.** „Struck Last Round“, „Struck the Open Guard“ und alle Status, die nur
Bedingungen der Engine auflösen, werden nicht mehr als Chip gezeigt, sondern im Log (A3). Dafür ein Flag am Status
(`hidden`/Präsentation) statt einer Namensliste in der UI. Tor: Test im Converter — jeder Status ohne Spielerwert
ist markiert; Smoke-Liste der sichtbaren Chips.

**B6 Elite-Regeln raus aus der Mitte.** Keine Textwand mehr zwischen Held und Gegnern; Elites mit 2+ Gegnern
stehen wieder nebeneinander. Helden-Effekte erscheinen nie als Kampfregel. Form: siehe Entscheidung 1. Tor:
`--smoke-elite` und `--smoke-crowd` für jedes Elite-Encounter mit 2+ Gegnern: Gegner in einer Reihe.

**B7 Elite-Regeln klar formulieren.** Jede Elite-/Boss-Regel als ein Satz in Spielersprache, ohne Fiktion als
Regelträger (Beispiel: Portcullis Judicator). Flavour darf bleiben, aber getrennt. Tor: Liste aller Regeltexte
zur Durchsicht; jede Regel unter 20 Wörtern.

**B8 Zauberstab neu.** Focus und Exhaustion als Heldenressourcen neben dem Energie-Blitz unter dem Charakter.
Darüber, über den Effekten, ein Stab mit 4 Plätzen. Karte gedrückt halten und auf den Stab ziehen = Inscribe.
Swap und Withdraw ebenfalls per Gedrückt-Halten und Ziehen (auf anderen Platz = Swap, aus dem Stab heraus =
Withdraw). Cast per Klick auf den Stab. Tor: `--smoke-wand` mit Screenshot; jede der fünf Aktionen per Maus
ausführbar.

**B9 Kessel neu.** Gleiches Prinzip wie B8, Form siehe Entscheidung 4. Tor: Screenshot, Hinzufügen und Brauen
per Maus.

## C — Menüs

**C1 Emojis und Cliparts aus allen Menüs.** Alle Strings in `scripts/` und im Content nach Emoji/Symbolzeichen
durchsuchen (⚡ als Kostensymbol ausgenommen, falls es als Icon gezeichnet wird). Tor: Grep-Test über die UI-
Strings findet keine Emoji-Codepoints.

## D — Kämpfe

**D1 Ambush in Runde 1 höchstens 20 Schaden.** Alle Mehr-Gegner-Encounter: Summe der Angriffsabsichten in Runde 1
≤ 20. Tor: Converter-Test über alle Encounter mit 2+ Gegnern.

**D2 Keine Gegner, die nur blocken.** Kein Gegner blockt zwei Runden hintereinander, ohne in einer davon
etwas anderes zu tun (Angriff, Debuff, Buff mit Wirkung). Tor: Converter-Test über alle Absichtszyklen; Liste der
geänderten Gegner im Commit.

## E — Belohnungen und Events

**E1 Elite-Kartenbelohnung besonderer.** Form siehe Entscheidung 2. Tor: Messung über viele Seeds.

**E2 Schatzräume ehrlich darstellen.** Prüfen, ob Schatzräume für den Spieler wie Events aussehen. Wenn ja:
eigene Darstellung (Truhe), nicht der Event-Screen. Tor: Screenshot.

**E3 Event-Relikte mit Preis.** Die 11 echten Events mit Relikt ohne Preis bekommen ein Risiko oder einen
Preis (HP, Max-HP, Gold, Fluch, nächster Kampf schwerer, Zufall). Danach gilt: kein Event-Relikt ohne Preis.
Tor: Converter-Test.

## F — Content-Durchgänge (je eigener Plan, erst Regeln + Beispiele zur Durchsicht)

**F1 Kartentexte kürzen.** Ziel: Median ≤ 12 Wörter, keine Karte über 20, jeder Text passt ohne Verkleinerung
in die Textbox. Mittel: Schlüsselwörter für wiederkehrende Bausteine, Bedingungen kürzer, Erklärungen in den
Tooltip statt auf die Karte. Mechanik bleibt gleich. Tor: Test misst Wortzahl und Box-Passung jeder Karte.

**F2 General-Pool komplett neu.** Die vier Charaktere interagieren kaum mit den General-Karten. Neuer Pool, in
dem jede Karte für mindestens zwei Charaktere eine Rolle hat, mit Bench-Messung je Charakter. Eigener Plan
(`GENERAL_POOL_PLAN.md`) mit Entscheidungen vorab.

## Entscheidungen

Getroffen vom Spieler am 2026-10-25:

1. **Elite-Regel (B6):** Badge am Gegner, also Regel-Icon mit Kurzname im Gegnerpanel und vollem Text per Hover.
2. **Elite-Belohnung (E1):** alle angebotenen Karten sind verbessert, und es gibt mehr Auswahl (5 statt 3).
3. **Content (F1, F2):** direkt umsetzen, ohne Durchsicht von Beispielen vorab. Der Spieler sieht sich das
   Ergebnis an.
4. **Kessel (B9):** ein Kesselbild mit 3 Plätzen am Helden über den Effekten. Eine Karte hineinziehen bedeutet
   „Into the pot“, ein Klick auf den Kessel bedeutet „Brew“, der Zustand wird als Leuchten gezeigt.
