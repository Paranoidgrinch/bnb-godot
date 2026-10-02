# Playtest-Feedback 2 — Arbeitsplan

Stand 2026-10-02. 21 Punkte aus Spieler-Feedback. Jeder Punkt hat ein Tor; er gilt erst als fertig, wenn das Tor
steht. Reihenfolge: erst Bugs und UI (klein, sofort spürbar), dann Belohnungen/Shop, dann Karte, dann der große
Content-Durchgang (Upgrades, Kosten, Identität, Paperwork-Abstand), zuletzt Events. Die Content-Blöcke werden mit
dem Act-I-Bench / `--audit` gemessen, nicht geschätzt.

**Entscheidungen des Spielers (2026-10-02):** Rast vor JEDEM Akt-Boss · Upgrade = Gesamtwert ×1,5 (ein Teil darf
eine neue Facette sein) · Content-Pass: erst Regeln + 10–15 Beispielkarten zur Durchsicht, dann der Rest · Commit
je Schritt, Push je fertigem Block.

Repos: Core (Engine), bnb-content (Daten/Converter), bnb-godot (UI). Golden-Set wird nach jedem Block neu
aufgenommen, mit Begründung im Commit.

## A — Bugs

**A1 Warding Wax / Ward Wax stapelt falsch.** Status nachstellen: 1×, 2×, 4× auflegen, Block zu Zugbeginn messen
gegen den Compendium-Text („4 Ward Wax → 4 Block“). Tor: Test misst Block bei 1/2/4 Stapeln (rot ohne Fix).

**A2 Queue-Auflösung rechnet Schaden falsch.** Queue-Karten in einem Kampf nachstellen, Ergebnis gegen Kartentext.
Tor: Test je gefundenem Fehler.

**A3 Schadensrechner (Vorschau) weicht ab.** Viele Kämpfe simulieren (Bot, alle Akte), pro Zug Vorhersage
(Gegner-Schaden auf den Helden, Schaden der Hand) gegen tatsächliche HP danach loggen; jede Abweichungsklasse
einzeln finden und fixen (A2 ist vermutlich eine davon). Tor: Mess-Werkzeug zeigt 0 Abweichungen über N Kämpfe
— oder jede verbleibende ist begründet (Zufall, verdeckte Absicht).

## B — UI

**B1 Rechtsklick zeigt Upgrade überall.** Heute: Shop, Belohnung, Stapel. Fehlt: Archiv, Hand im Kampf, Inventar,
Lagerfeuer/Upgrade-Auswahl, Event-Kartenwahl. Tor: Smoke prüft den Flip auf jedem dieser Screens.

**B2 Archiv ohne Blitz.** Kosten in der Ecke nur als Zahl. Tor: Screenshot.

**B3 Scroll-Position bleibt.** `RebuildScreen` leert `_main` → Scroll springt nach oben. Scroll-Wert merken und
nach dem Neuaufbau wiederherstellen (pro Screen, nicht über Screenwechsel hinweg). Tor: Smoke — Liste
runterscrollen, Karte ansehen/auswählen, Position gleich.

**B4 Abbrechen beim Upgraden.** Shop-Upgrade, Lagerfeuer, Events: „Zurück“ statt Zwang. Abgebrochen = nicht
bezahlt, Angebot bleibt (wie schon bei der Shop-Entfernung). Tor: Test im Core (kein Gold weg, Service bleibt) +
Smoke.

## C — Belohnungen & Shop

**C1 Ein Belohnungsschritt weniger.** Nach dem Sieg: Gold sofort gutgeschrieben, Karten sofort sichtbar, unten
„Skip“. Kein Zwischenmenü. Tor: Smoke zählt die Klicks Sieg → Karte (1) bzw. Sieg → Skip (1).

**C2 Boss-Relikt wählen (1 aus 3), nichts erzwungen.** Boss-Belohnung auf EINEM Screen: Gold (automatisch), 3
Karten, 3 Relikte; der Spieler nimmt keine/eine Karte und kein/ein Relikt — jede Kombination. Gilt sinngemäß
auch für das Elite-Relikt (ablehnbar). Tor: Test — alle vier Kombinationen möglich; Smoke.

**C3 Karten-Entfernung wird global teurer.** Preis steigt nach jeder Nutzung, über Shops hinweg (Zähler im
RunState, Preis = Basis + Schritt × Nutzungen). Tor: Test — zweiter Shop zeigt den erhöhten Preis.

**C4 Bereits verbesserte Karten in Belohnungen.** Chance je Akt: I 10 %, II 20 %, III 30 %, IV 40 %. Tor: Messung
über viele Seeds trifft die Raten (±2 %).

## D — Karte (Map)

**D1 Legacy-Generator archivieren.** RuleBased aus der Neuer-Lauf-Auswahl und den Einstellungen nehmen; alte
Spielstände mit RuleBased laden weiter. Code bleibt für Tests/`--legacy`-Probe. Tor: Neuer-Lauf-Panel ohne Wahl.

**D2 Lagerfeuer vor dem Boss auf jedem Pfad.** Reihe vor dem Boss = Rast, für jeden Pfad. Tor: `--lanes` über
300 Seeds je Akt: 100 % der Pfade.

**D3 Mindestens 4 echte Entscheidungen pro Pfad und Akt.** „Echt“ = eine Gabelung, deren Türen in Räume
unterschiedlicher Art führen (Kampf/Elite/Rast/Shop/Event/Schatz), nicht zwei gleiche Kämpfe. Heute: Schnitt
4,9 (Akt I), aber das MINIMUM pro Pfad liegt bei 2. Tor: `--lanes` misst Minimum pro Pfad ≥ 4, alle Akte.

## E — Kämpfe

**E1 Paperwork erst ab Runde 3.** Jeder Gegner, der dem Helden Paperwork gibt: Absichtszyklus so drehen, dass das
erst ab Runde 3 passiert. Tor: Messung über alle Encounter — kein Paperwork auf dem Helden in Runde 1–2.

## F — Karten (großer Content-Durchgang, gemessen)

**F1 Upgrades +50 % und mit Richtung.** Upgrade ≈ 1,5× Grundwert. Statt nur die Hauptzahl: Teil des Zuwachses als
neue Facette (zieht 1, Exhaust weg, Kosten −1, Nebeneffekt, Synergie-Hook). Regelwerk als Tabelle, dann alle
Karten durch. Tor: Bench-Wert jeder `+`-Karte ≥ 1,4× Basis; ≥ 40 % der Upgrades ändern mehr als eine Zahl.

**F2 Effekte neben Paperwork nachziehen.** Paperwork-Abstand messen (Card-Value-Run), die anderen
Schlüsselwörter/Effektfamilien anheben, bis sie im selben Band liegen. Paperwork bleibt. Tor: Bench — Median
jeder Familie innerhalb ±15 % von Paperwork.

**F3 Schwächere Kopien bekommen eine Identität.** Paare finden (gleiche Effektfamilie, gleiche Kosten, strikt
schwächer), jede bekommt einen eigenen Dreh. Tor: Liste der Paare, jedes mit neuem Unterschied.

**F4 Mehr 2-Kosten-Karten.** Einige heute schon starke 1-Kosten-Karten → 2 Kosten und deutlich stärker; die
bestehenden 2-Kosten-Karten attraktiver. Tor: Bench — 2-Kosten-Karten pro Energie ≥ 1-Kosten-Median.

## G — Events

**G1 Kampf-Boni über mehrere Kämpfe.** „Nächster Kampf: nächster Debuff ignoriert“ & Co. → „die nächsten X
Kämpfe“, Bonus wie Malus. Tor: Liste umgestellter Events; Test, dass der Effekt X Kämpfe hält.

**G2 Glücksspiel-Events.** Ein paar langweilige Events durch Gamble-Events ersetzen (hohes Risiko, hoher
Gewinn, Ausgang sichtbar per Zufall). Entwürfe zuerst in diese Datei, dann bauen. Tor: Events im Spiel, Tests
für beide Ausgänge.

## Status

| Punkt | Stand |
|---|---|
| alle | offen |
