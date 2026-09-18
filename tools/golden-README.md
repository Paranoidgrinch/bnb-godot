# Das Golden-Set — das Messgerät, eingefroren

## Wofür es da ist
Der ganze Runner-Umbau (`RUNNER_PERFORMANCE_PLAN.md`, R1–R5) hat **ein** Risiko: dass der Runner beim
Schnellerwerden aufhört, dasselbe zu finden. Das Golden-Set ist die Antwort darauf. Es spielt einen festen
Satz Läufe und schreibt auf, **was dabei herauskam** — nicht wie lange es gedauert hat.

```bash
cd ~/bnb-godot
tools/golden.sh              # nachspielen und gegen die Aufzeichnung vergleichen
tools/golden.sh --jobs 4     # weniger gleichzeitig (jeder unsterbliche Lauf hält ~900 MB)
tools/golden.sh --record     # NEU aufzeichnen — nur mit Absicht, siehe unten
tools/golden.sh --ui 0       # kein Lauf zeichnet den Schirm (Vorgabe: 1, der erste unsterbliche Seed)
tools/golden.sh --console    # durch den Godot-freien Konsolen-Runner spielen, EIN Prozess (R4).
                             #   Dasselbe Hirn (RogueDeck.Bot), ein anderer Wirt — wenn beide diese Datei
                             #   reproduzieren, liegt das Verhalten in der Bibliothek und in keinem Wirt.
tools/golden.sh --release    # aus einer EXPORTIERTEN Binärdatei spielen, deren Engine optimiert ist
                             #   (~12 s Export, dafür der ganze Satz rund ein Viertel schneller)
```
Exit 0 = alles wie aufgezeichnet. Exit 1 = irgendein Lauf findet etwas anderes, mit Diff darunter.

## Was verglichen wird und was nicht
Verglichen werden je Lauf die beiden Berichtszeilen `sim-fitness:` und `sim-result:` — Ergebnis, Akte, Räume,
Kämpfe, HP, genommener und geheilter Schaden, die Schadenstabelle je Akt, `problems`, `error` und der Grund,
aus dem der Lauf endete.

⚠ **`seconds=` wird vorher weggeschnitten.** Die Uhr *soll* sich ändern — das ist der Sinn des ganzen Bogens.
Ein Tor, das durchfällt, wenn der Runner schneller wird, wäre ein Tor gegen seinen eigenen Zweck. Die Zeiten
stehen trotzdem unten in der Datei, damit man sieht, wie viel schneller es geworden ist.

⚠⚠ **Ein Lauf, der nichts meldet, ist ein Fehlschlag, kein bestandener Test.** Ein abgestürzter Prozess, eine
fehlende Fitness-Zeile, ein Lauf, der nie endete — jeder wird als genau diese Abwesenheit hineingeschrieben
(`THE RUN PRODUCED NO RESULT LINE`) und fällt gegen eine Aufzeichnung durch, in der ein Ergebnis steht.
Schweigen darf nie wie Zustimmung aussehen. (Das ist die Lehre aus D7, wo drei Screenshots derselbe
Niederlagen-Schirm waren und alle drei Erfolg meldeten.)

## Der Satz Läufe
Zwölf **unsterbliche** Läufe (Seeds 1–12), die durch alle fünf Akte gehen — dort ist der Inhalt. Dazu drei auf
einem **sterblichen** Körper (Seeds 101–103), die in Akt I sterben: das deckt den Niederlagen-Pfad, das
Run-Ende und die Schirme ab, die ein Sieger nie sieht, für etwa ein Zehntel der Kosten eines unsterblichen
Laufs.

⚠ Die Seeds sind **nicht nach ihrem Ausgang gewählt**. Ein Set aus lauter Siegen könnte nicht bemerken, dass
Läufe anfangen zu verlieren.

## Wann neu aufzeichnen
Nur, wenn eine Änderung am **Spiel** beabsichtigt war — eine Regel, eine Zahl, ein Pool, ein Kampf. Dann im
Commit sagen, **welche Zeilen sich bewegt haben und warum**. Eine Neuaufzeichnung, die einen unerklärten
Unterschied wegschreibt, löscht genau die Information, für die die Datei existiert.

Eine Änderung, die den Runner nur schneller machen sollte, darf **nie** eine Neuaufzeichnung brauchen.

## Wo die Logs landen
`~/Desktop/bnb-golden/<zeitstempel>/` — ein volles Log je Lauf, plus `diff.txt`, wenn das Tor durchfiel.
Die Aufzeichnung selbst steht in `tools/golden-runs.txt` und gehört ins Git.
