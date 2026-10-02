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

## F — Vorschlag zur Durchsicht (Regeln + Stichprobe)

**Messbasis** (Audit 2026-09-28, 4 Runden gegen Dummy, Wert pro Energie, Median je Familie): Paperwork 10 · Doubt 10 ·
Queue 10 · Lien 9 · Citation 9 · Blood Ink 8 · Seal 7 · Ward Wax 7 · Censure 6 · Archive/Junk 5 · reiner Schaden/Block 5.
Upgrades heute: +1 bis +3 auf die Hauptzahl (≈ +25 %), fast nie eine neue Facette.

**Regeln**
- R1 Upgrade = Gesamtwert ×1,5. Mindestens 40 % der Upgrades bekommen eine Facette: zieh 1 · Kosten −1 · Exhaust weg /
  Retain · +1 Schlüsselwort-Stapel passend zur Identität · Bedingung wird bedingungslos · kleiner Nebeneffekt.
- R2 Familien unter Paperwork werden angehoben, bis ihr Median ≥ 8,5/E (−15 %) liegt: Seal, Ward Wax, Censure,
  Archive/Junk, reiner Schaden/Block. Paperwork bleibt wie es ist.
- R3 Eine Karte, die strikt schwächer als eine gleich teure Karte derselben Familie ist, bekommt einen eigenen Dreh
  (anderes Ziel, andere Bedingung, andere Facette) statt nur größerer Zahlen.
- R4 2-Kosten-Karten liefern ≥ 15 % mehr pro Energie als der 1-Kosten-Median ihrer Familie. Ein paar heute sehr
  starke 1-Kosten-Karten werden 2-Kosten-Karten mit deutlich mehr Wirkung.

**Stichprobe** (heute → neu Basis → neu Upgrade)

| Karte | heute (Basis / +) | neu Basis | neu + |
|---|---|---|---|
| Paper Cut (Starter) | 6 Schaden / 8 | unverändert | 9 Schaden |
| Cower Behind a Desk (Starter) | 5 Block / 7 | unverändert | 8 Block |
| Strong Binder (Starter) | 7 Block, 1 Doubt / 9, 2 | unverändert | 10 Block, 2 Doubt |
| Petty Objection (R3: schwächer als der Starter Strong Binder) | 5 Block, 1 Doubt / 6, 2 | 5 Block, 1 Doubt, **zieh 1** | 7 Block, 2 Doubt, zieh 1 |
| Inkblot Verdict (R3) | 8 Schaden, +2 wenn Paperwork / 10 | 8 Schaden, **+ Schaden = Paperwork des Ziels (max 8)** | 10 Schaden, + Paperwork (max 12) |
| Cauldron Copy | 9 Schaden, +1 Duplicate Copy / 12 | 10 Schaden, +1 Duplicate Copy (R2) | 13 Schaden, +1 Duplicate Copy, **zieh 1** |
| Deferred Hex (Queue) | Queue: 13 / 16 | unverändert | Queue: 16, **jetzt zieh 1** |
| Waxing Authority (Seal) | 5 Schaden, 1 Seal / 6, 2 | 6 Schaden, 1 Seal | 8 Schaden, 2 Seal |
| Waxen Surety (Ward Wax) | 4 Ward Wax / 5 | 5 Ward Wax (R2) | 6 Ward Wax, **sofort 3 Block** |
| Certified Kindling (Archive) | Archive, 4 Block (+4 bei Junk) / 5 (+6) | Archive, 6 Block (+5 bei Junk) (R2) | Archive, 8 Block (+6 bei Junk), **zieh 1 bei Junk** |
| Cursed Addendum (Paperwork) | 6 Schaden, 2 Paperwork / 7, 3 | unverändert | 9 Schaden, 3 Paperwork |
| Grave Lien (R4: heute 12/E, sehr stark) | 1 E: 7 Schaden, 5 Lien | **2 E**: 13 Schaden, 9 Lien | 2 E: 15 Schaden, 11 Lien, zieh 1 |
| Summary Judgment (2 E, heute 8/E, schwach) | 16 Schaden, ab 6 Paperwork: Paperwork auslösen | 2 E: **20** Schaden, ab 6 Paperwork auslösen | 2 E: 24 Schaden, ab **4** Paperwork auslösen |
| Permit A38 (Starter, 2 E) | 5 Paperwork / Kosten 1 | unverändert | unverändert (Kosten −1 ist schon ×2) |

**Schlüsselwort-Vorschläge (R2), statt jede Karte einzeln:** Ratify gibt +5 statt +3 pro Deed · eine Censure, die einen
Status abwehrt, gibt zusätzlich 2 Block. Ward Wax und Archive/Junk über die Kartenzahlen (s. Tabelle).


### F — FREIGEGEBEN (Spieler, 2026-10-02): Starter + Akt I

Globale Regeln: Ratify +5 statt +3 · Censure gibt 2 Block pro Abwehr · Starter: nur Upgrades stärker · Ziehen nur bei
Karten, deren Identität Ziehen ist. Upgrade ≈ ×1,5. (A Breite · B Mehrfach · C Bedingung · D Querverbindung ·
E Nachwirkung · F Umwandlung · G Sofort · H Kosten −1 · I Retain · K Beides statt Wahl · N Nachteil weg · S Schwelle)

Starter: Paper Cut + 5×2 (B) · Cower + 6 Block, nächster Zug 3 (E) · Strong Binder + 9 Block, 1 Doubt an ALLE (A) ·
Permit A38 + kostet 1.
Akt I: Deferred Hex + Queue 11 an ALLE (A) · Protective Adjournment + Queue 11 Block + 5 sofort (G) · Cinder Warrant
B 8 / + 8, bis zu 2 Junk je Wiederholung (C) · Dawn Summons + 24/+12 · Threefold Injunction + 4×4 (B) · Blank Warrant
+ 27/+7 · Rebuttal + 10, 5/Doubt max 15, 1 Doubt an ALLE (A) · Cauldron Copy B 10 / + 15 · Forfeit Seal + 10, 4 Lien,
8 wenn Block (C) · Inkblot Verdict B 8 + PW (max 8) / + max 10, PW tickt sofort (G) · Cursed Addendum + 7, 3 PW, +2 PW
wenn Ratified (C) · Deskward + 11, Red Tape → Exhaust (N) · Summary Judgment 2E B 20 / + kostet 1 (H) · Conditional
Approval + 9, 2 Seal (3) · Fine-Print Hex + 7×2, je 1 Seal bei Doubt (B) · Occult Precedent B 8+2 / + 9, +4 und 1 Ward
Wax bei PW (D) · Backlog Charge + 9 +5/Queue · Foreclosure + 9, 8 Lien · Waxing Authority B 6+1 Seal / + 4×2 je 1 Seal
(B) · Hex Circular 2E B 9 ALLE+1 Doubt / + 12+2 · Candle Tribunal 2E B 6×3 / + 8×3 · Grave Lien → 2E B 13+9 Lien / +
13 und 6 Lien an ALLE (A) · Petty Objection B 5 Block, Angriff: 2 Doubt / + 7, 3 Doubt +3 Block (C) · Malediction
Review + 8 Block, Censure beides (K) · Clerical Discretion + 7, Doubt und Seal (K) · Counter Ward + 8, −1, Retain (I) ·
Sealed Mantle + 12, 3 Wax · Sanctioned Charm B 6 / + 8 +1 Censure (D) · Wastepaper Bastion B 5+3 / + 7+4 · Waxen
Surety B 5 / + 5 + Block = Wax (F) · Tallow Reserve + 5 Wax · Certified Kindling B 6 (+5) / + 8, Junk: 6 an ALLE (D) ·
Form of Ill Intent → 2E B 6 PW, Angriff 2 Doubt / + 8, 3 · Blood Marginalia + 4 Cit, 3 BI · Witchmark Citation + 5
Cit, nicht-schadend +2 Cit (C) · Mortgage Sigil + 5/+5 · Notarial Press + 3 Seal, 7 Block · Seal of Concern B 2 Seal
1 Doubt / + an ALLE (A) · Silent Hearing + 3 Cit, 10 Block · Contempt Finding + 3 Block/Cit, je 2 Schaden (D) · Candle
Allowance + Queue 2 Energie · Borrowed Candle + ziehe 3 · Notary's Tithe + ziehe 3 · Secure Misfiling + ziehe 2 ·
Formal Dissent + Retain (I) · Tallow Budget + 2 Energie · False Signature + −2 · Night Docket / Privy Seal wie heute
(N) · Riten H wie heute · Continuance 2E B 12 / + kostet 1 · Black Ledger + Schwelle 5 (S) · Clerk's Familiar + 6 ·
Dubious Authority + 3 PW · Pending Matters + 5 · Stay of Execution + 28 · Usurer's Moon + 1 pro 2.

### F — FREIGEGEBEN (Spieler, 2026-10-02): Akt II–IV wie im Chat vorgeschlagen
Hedge Hospitality → 2E; Cross-Filing bleibt 1E; Riten behalten "kostet 1 weniger". Neue Facette T (Zielwahl).

## G2 — Entwürfe Glücksspiel-Events (vor dem Bau)

Alle Ausgänge per Lauf-Zufall (seed-reproduzierbar), die Chancen stehen im Text — Glück, kein Versteckspiel.

1. **Die Formular-Lotterie** (Akt I) — 30 Gold einsetzen: 40 % → 120 Gold · 35 % → eine seltene Karte · 25 % → nichts. Oder gehen.
2. **Der Stempel-Kreisel** (Akt I/II) — dreh das Rad: 30 % → ein zufälliges Relikt · 40 % → 2 zufällige Karten verbessert · 30 % → 12 HP verloren.
3. **Doppelt oder nichts beim Kassenwart** (Akt II) — die Goldbeute der nächsten 3 Kämpfe wetten: 50 % verdoppelt · 50 % entfällt.
4. **Die Berufung auf den Zufall** (Akt II/III) — streiche eine Karte deiner Wahl; 50 % → du darfst noch eine streichen · 50 % → eine zufällige weitere Karte wird verwandelt.
5. **Würfel des Notars** (Akt III) — zahle 10 % deiner Max-HP: 50 % → die nächsten 3 Kämpfe beginnst du mit +1 Energie · 50 % → die nächsten 3 Kämpfe beginnen die Gegner mit 2 Stärke.
6. **Das versiegelte Angebot** (Akt IV) — öffne einen von drei Umschlägen (blind): großes Relikt / 150 Gold / ein Fluch + 50 Gold.

Ersetzt werden die Events mit der schwächsten Wirkung (einmaliger Ein-Kampf-Effekt, „nächster Debuff wird ignoriert“ & Co.); welche genau, entscheidet die Liste aus G1.

## Status

| Punkt | Stand |
|---|---|
| A1 Ward Wax | ✔ Votive Covenant wirkte nie; Wax Reliquary/Indemnity hielten den ganzen Kampf; Indemnity+ heilte 3 statt 4 (bnb-content 85c50a9) |
| A2 Queue | ✔ Auflösung rechnet richtig (gemessen); Hover einer Queue-Karte zeigt jetzt den nächsten Zug (bnb-godot 3423c48) |
| A3 Rechner | ✔ `--calc-check` (60 Kämpfe): Kartenvorschau, Incoming, Gegner-HP exakt; **Absichts-Chips 160/450 falsch** → zeigen jetzt den gerechneten Schlag |
| B1 Rechtsklick | ✔ Archiv + Hand im Kampf (Rest hatte es schon) |
| B2 Blitz | ✔ |
| B3 Scroll | ✔ Archiv war der echte Fall; Hauptseiten merken die Position je Bildschirm |
| B4 Zurück | ✔ Lagerfeuer-Upgrade abbrechbar (Core EventChoice.Declinable). Event-Upgrades bewusst nicht (Teil der Geschichte) |
| C1 Belohnung | ✔ Gold sofort, Karten direkt, Skip (Core VictoryRewardGranted) |
| C2 Boss-Relikt | ✔ alle drei zur Wahl, jede Kombination |
| C3 Entfernung | ✔ 75 +25 je Nutzung, laufweit (Core ShopService.PriceStep) |
| C4 Upgrade-Funde | ✔ 10/20/30/40 % (Core PoolRewardSource.UpgradeChancePercent) |
| D1 Legacy | ✔ Neuer-Lauf-Panel fragt nicht mehr; alte Saves laden weiter |
| D2 Rast vor Boss | ✔ letzte Reihe vor jedem Akt-Boss ganz Rast (Core StrategicRoomSpec.PreBossKind): 100 % der Routen |
| D3 Entscheidungen | ✔ Querwege (Core MinForksPerRoute) + echte Entscheidungen als Zusage (MinRealDecisionsPerRoute=4, Gabel-Strafe auch für Kämpfe, 48 Versuche): **min 4, Median 7 echte pro Route in Akt I–IV** (300 Seeds/Akt); Entscheidungen pro Karte 4,9 → 17 |
| E1 Paperwork | ✔ alle ~290 Encounter ohne Paperwork auf dem Helden in Runde 1–2 — **außer Bossen** (geskriptete Mechanik, bewusst ausgenommen) |
| F Karten | ✔ umgesetzt (alle 138 Grundkarten nach Freigabe). Abweichungen, weil die Engine es so nicht kann oder es sonst wertlos wäre: Protective Adjournment+ = Queue 12 Block + 2 Ward Wax (eine Queue-Karte wirkt nicht beim Ausspielen) · Occult Precedent+ / Smudged Index+ geben ihr Ward Wax ohne Bedingung · Sanguine Errata+ +1 Blood Ink ohne Bedingung · Censure-Block kommt als Promised Block im nächsten Zug (im Gegnerzug gewonnener Block verfällt sonst) · Continuance: Basis-Regel 12, Upgrade kostet 1 |
| G1 X Kämpfe | ✔ Event-Eröffnungen gelten 3 Kämpfe (Core InstallNextCombatOpeningRunEffect.Combats, im Save); Markierungen bleiben 1 Kampf |
| G2 Glücksspiele | ✔ 6 Events (Core EventChoice.Outcomes), ersetzen 6 Events mit der geringsten bleibenden Wirkung |
