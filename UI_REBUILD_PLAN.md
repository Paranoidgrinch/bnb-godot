# UI-Umbau: die obere Leiste fällt weg

Vom Spieler beschrieben am 2026-09-28. Ziel: mehr Platz in der Höhe für Held und Gegner.

## Was wegfällt
- Die komplette rote Leiste oben (`_topBar` in `SessionScreen.cs`): doppelte HP, Gold, doppeltes Deck, Seed,
  Log-Button.
- Unten die Buttons „Discard“ und „Deck“.

## Was neu kommt oder umzieht
| Was | Wohin |
|---|---|
| Gold | schlichte Anzeige oben |
| Deck | unten wird „Deck“ zu **Inventory** (Taste `I`); das Menü hat zwei Reiter: **Relikte** und **Karten** |
| Seed | ins **Pausenmenü**; ein Klick kopiert ihn in die Zwischenablage |
| Nachziehstapel | links unten wie bisher; **Klick zeigt die Karten, die noch darin liegen — sortiert, nicht in ihrer echten Reihenfolge** |
| Ablagestapel | **rechts unten, spiegelbildlich** zum Nachziehstapel; Klick zeigt die abgelegten Karten |
| Log | Button **mittig unter den Handkarten, neben „End Turn“** |
| gewonnene Höhe | an Held und Gegner |

Tastenbelegung: `I` Inventory (frei). `D`/`X` (heute Deck/Ablage) öffnen künftig Nachzieh- bzw. Ablagestapel,
`L` bleibt das Log.

## Hauptmenü
- Buttons untereinander statt nebeneinander (vorher schnitt die Reihe „Quit“ ab) — ✔ umgesetzt 2026-09-28.

## Offen
- **Relikte im Kampf:** Sie stehen heute in der oberen Leiste (`RelicStrip`) und leuchten auf, wenn sie wirken
  (Playtest-Punkt, d818c0f). Fällt die Leiste weg, brauchen sie einen Platz — Vorschlag: eine kleine Reihe neben
  der Gold-Anzeige oben, sonst nur im Inventory (dann ohne Aufleuchten).

## Tor
Screenshot-Sonde vorher/nachher (Kampf, Karte, Inventory, Pausenmenü); Golden-Set unverändert (die UI spielt
nicht mit); Tastatur- und Maus-Bedienung jedes neuen Elements einmal durchgespielt.
