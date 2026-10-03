using System.Text;
using System.Text.Json;
using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// THE WHOLE ARCHIVE, TAKEN HOME (user, 2026-10-03): every entry of every tab — met or not — written out of the
// game as something anybody can open. Two files in one folder on the desktop:
//   • index.html — ONE self-contained page that reads like the archive: the same tabs, the same shelves, the
//     same tiles, the same plate with a card's face, a relic's pool frame and a body's portrait, every picture
//     embedded. Opens in any browser, with no game and no network.
//   • archive.json — the same entries as plain data (no pictures), for anything that wants to read them.
//
// Built from the catalogue the archive itself shows (Archive.Sections), so the export cannot say anything the
// screen does not.
public static class ArchiveExport
{
    private const int Picture = 320;   // the longest side of an embedded picture, px

    public static string Write(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        Archive.Build(blueprint);

        var root = OS.GetSystemDir(OS.SystemDir.Desktop);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            root = OS.GetUserDataDir();
        var folder = Path.Combine(root, $"BnB Archive {DateTime.Now:yyyy-MM-dd HHmm}");
        Directory.CreateDirectory(folder);

        var tabs = Archive.Kinds.Select(kind => new
        {
            kind = kind.ToString(),
            title = Archive.Title(kind),
            shelves = Archive.Sections(kind).Select(s => new
            {
                name = s.Shelf,
                entries = s.Entries.Select(e => Entry(blueprint, e)).ToList(),
            }).ToList(),
        }).ToList();

        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(folder, "archive.json"), JsonSerializer.Serialize(new
        {
            game = "Bureaucrats & Broomsticks",
            exported = DateTime.Now.ToString("s"),
            tabs = tabs.Select(t => new
            {
                t.kind,
                t.title,
                shelves = t.shelves.Select(s => new
                {
                    s.name,
                    entries = s.entries.Select(e => e with { image = null }).ToList(),
                }),
            }),
        }, json));

        var page = Page(JsonSerializer.Serialize(tabs), DataUri(GD.Load<Texture2D>("res://assets/cards/card-frame.png"), 600));
        var html = Path.Combine(folder, "index.html");
        File.WriteAllText(html, page);
        return html;
    }

    private sealed record Exported(
        string id, string name, string shelf, bool met, string? frame, string? prose, string? proseTitle,
        string? upgraded, string? cost, string? upgradedCost, List<string[]> facts, IReadOnlyList<string> moves,
        string? image, string? frameGround, string? frameEdge, int frameWidth, string titleColor);

    private static Exported Entry(RunBlueprint blueprint, ArchiveEntry e)
    {
        var cost = e.Facts.FirstOrDefault(f => f.Label == "Cost").Value;
        string? upgradedCost = null;
        if (e.Kind == ArchiveKind.Cards && e.Upgraded is { Length: > 0 })
        {
            var plus = blueprint.Cards.FirstOrDefault(c => c.Id == e.Id + "+");
            upgradedCost = plus is null ? cost
                : plus.Costs.Count == 0 ? "0"
                : string.Join(" · ", plus.Costs.Select(c => c.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        var frame = MoonvineTheme.RelicFrame(e.Frame);
        var art = e.Kind switch
        {
            ArchiveKind.Cards => CardVisuals.CardArt(e.Id),
            ArchiveKind.Relics => CardVisuals.RelicArt(e.Id),
            _ => CardVisuals.EnemyArt(e.Id),
        };
        return new Exported(
            e.Id, e.Name, e.Section, Archive.Seen(e), e.Frame, e.Prose, e.ProseTitle, e.Upgraded, cost, upgradedCost,
            [.. e.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => new[] { f.Label, f.Value })],
            e.Moves, DataUri(art, Picture),
            Hex(frame.Ground), Hex(frame.Edge), frame.Doubled ? 3 : frame.Width,
            Hex(MoonvineTheme.RarityColor(e.Frame)));
    }

    private static string Hex(Color color) => "#" + color.ToHtml(false);

    // A picture as a data URI: decompressed, fitted inside `longest` px, WebP (lossy, alpha kept).
    private static string? DataUri(Texture2D? texture, int longest)
    {
        if (texture?.GetImage() is not { } image)
            return null;
        if (image.IsCompressed())
            image.Decompress();
        var scale = Math.Min(1f, longest / (float)Math.Max(image.GetWidth(), image.GetHeight()));
        if (scale < 1f)
            image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)),
                Image.Interpolation.Lanczos);
        var bytes = image.SaveWebpToBuffer(true, 0.82f);
        return bytes.Length == 0 ? null : "data:image/webp;base64," + Convert.ToBase64String(bytes);
    }

    private static string Css(Color color) => "#" + color.ToHtml(false);

    private static string Page(string data, string? cardFrame)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Bureaucrats &amp; Broomsticks — Archive</title>
<style>
""");
        sb.Append($$"""
:root {
  --bg: {{Css(MoonvineTheme.Bg)}}; --panel: {{Css(MoonvineTheme.BgPanel)}}; --panel-strong: {{Css(MoonvineTheme.BgPanelStrong)}};
  --raised: {{Css(MoonvineTheme.BgRaised)}}; --control: {{Css(MoonvineTheme.BgControl)}}; --hairline: {{Css(MoonvineTheme.Hairline)}};
  --card: {{Css(MoonvineTheme.CardGround)}}; --text: {{Css(MoonvineTheme.Text)}}; --soft: {{Css(MoonvineTheme.TextSoft)}};
  --muted: {{Css(MoonvineTheme.TextMuted)}}; --accent: {{Css(MoonvineTheme.Accent)}}; --accent-light: {{Css(MoonvineTheme.AccentLight)}};
  --signal: {{Css(MoonvineTheme.Signal)}}; --harm: {{Css(MoonvineTheme.Harm)}};
}
""");
        sb.Append("""
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.4 Georgia, "Times New Roman", serif; }
header { display: flex; gap: 16px; align-items: baseline; padding: 18px 24px 8px; }
header h1 { margin: 0; color: var(--accent); font-size: 24px; font-weight: normal; }
header .said { color: var(--muted); flex: 1; }
header input { background: var(--panel); color: var(--text); border: 1px solid var(--hairline); border-radius: 6px; padding: 6px 10px; width: 260px; font: inherit; }
nav { display: flex; gap: 8px; padding: 6px 24px 12px; flex-wrap: wrap; }
nav button { flex: 1; min-width: 120px; background: var(--panel); color: var(--soft); border: 1px solid var(--hairline); border-radius: 8px; padding: 8px; font: inherit; cursor: pointer; }
nav button.on { background: var(--control); border-color: var(--accent); color: var(--accent-light); }
main { display: flex; gap: 16px; padding: 0 24px 24px; align-items: flex-start; }
#shelf { flex: 1; min-width: 0; }
.heading { display: flex; align-items: center; gap: 10px; margin: 14px 0 8px; }
.heading b { color: var(--accent); font-weight: normal; font-size: 17px; }
.heading span { color: var(--muted); }
.heading hr { flex: 1; border: 0; border-top: 1px solid var(--hairline); }
.grid { display: flex; flex-wrap: wrap; gap: 12px; }
.tile { width: 104px; cursor: pointer; }
.tile .window { width: 104px; height: 104px; border-radius: 4px; border: 1px solid var(--hairline); background: var(--panel); overflow: hidden; display: flex; align-items: center; justify-content: center; }
.tile .window img { width: 100%; height: 100%; object-fit: contain; }
.tile .window .none { color: var(--muted); opacity: .55; font-size: 34px; }
.tile.on .window { border-color: var(--accent-light); }
.tile .name { text-align: center; font-size: 11px; color: var(--soft); margin-top: 3px; }
.tile.unmet .name::after { content: " ·"; color: var(--muted); }
#plate { position: sticky; top: 12px; width: 420px; max-height: calc(100vh - 24px); overflow-y: auto; background: var(--panel-strong); border: 1px solid var(--hairline); border-radius: 8px; padding: 14px; }
#plate h2 { margin: 6px 0; color: var(--accent); font-weight: normal; font-size: 20px; }
#plate .rules { color: var(--soft); white-space: pre-wrap; }
#plate .decree { color: var(--accent-light); }
#plate .mark { color: var(--accent-light); margin-top: 8px; }
#plate table { border-collapse: collapse; margin-top: 10px; width: 100%; }
#plate td { vertical-align: top; padding: 2px 8px 2px 0; }
#plate td:first-child { color: var(--muted); width: 92px; }
#plate .moves { margin-top: 10px; color: var(--muted); }
#plate .moves div { color: var(--soft); font-size: 12px; }
#plate .invite { color: var(--muted); text-align: center; padding: 40px 0; }
#plate .unmetnote { color: var(--muted); font-size: 12px; }
.portrait { display: flex; justify-content: center; margin-bottom: 6px; }
.face { position: relative; width: 174px; height: 247px; border-radius: 13px; background: var(--card); overflow: hidden; }
.face .art { position: absolute; left: 4.37%; top: 14.19%; right: 4.46%; bottom: 27.91%; background: #1a0b0f; }
.face .art img { width: 100%; height: 100%; object-fit: cover; }
.face .title { position: absolute; left: 11%; right: 11%; top: 3.3%; height: 8.2%; display: flex; align-items: center; justify-content: center; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.face .cost { position: absolute; left: 0; top: 0; width: 13.7%; height: 9.9%; display: flex; align-items: center; justify-content: center; color: var(--signal); font-size: 14px; }
.face .plaque { position: absolute; left: 7.9%; right: 8%; top: 75.6%; bottom: 4.8%; display: flex; align-items: center; justify-content: center; text-align: center; color: var(--soft); font-size: 10px; line-height: 1.15; overflow: hidden; }
.face .frame { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
.flip { margin-left: 8px; background: var(--control); color: var(--accent-light); border: 1px solid var(--hairline); border-radius: 6px; font: inherit; font-size: 12px; cursor: pointer; }
.relic { width: 156px; height: 156px; border-radius: 6px; display: flex; align-items: center; justify-content: center; overflow: hidden; }
.relic img { width: 88%; height: 88%; object-fit: contain; }
.body { width: 220px; height: 240px; display: flex; align-items: center; justify-content: center; }
.body img { max-width: 100%; max-height: 100%; object-fit: contain; }
footer { color: var(--muted); padding: 0 24px 24px; font-size: 12px; }
</style>
</head>
<body>
<header><h1>Archive</h1><span class="said">Everything a run has shown you, kept.</span><span id="tally"></span><input id="find" placeholder="Search names and rules…"></header>
<nav id="tabs"></nav>
<main><div id="shelf"></div><aside id="plate"><div class="invite">Pick something off the shelf.</div></aside></main>
<footer id="foot"></footer>
<script id="data" type="application/json">
""");
        sb.Append(data.Replace("</", "<\\/"));
        sb.Append("</script>\n<script>\nconst FRAME = ");
        sb.Append(JsonSerializer.Serialize(cardFrame));
        sb.Append(";\n");
        sb.Append("""
const TABS = JSON.parse(document.getElementById('data').textContent);
let tab = 0, picked = null, plus = false, find = '';
const esc = s => (s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const all = TABS.flatMap(t => t.shelves.flatMap(s => s.entries));
document.getElementById('tally').textContent = `${all.filter(e => e.met).length} of ${all.length} found`;
document.getElementById('foot').textContent = `${all.length} entries · exported from the game · · marks something not yet met in this save`;
function matches(e) {
  if (!find) return true;
  const hay = [e.name, e.prose, e.upgraded, e.shelf, ...e.facts.map(f => f[1]), ...e.moves].join(' ').toLowerCase();
  return hay.includes(find);
}
function drawTabs() {
  document.getElementById('tabs').innerHTML = TABS.map((t, i) => {
    const n = t.shelves.reduce((a, s) => a + s.entries.length, 0);
    const m = t.shelves.reduce((a, s) => a + s.entries.filter(e => e.met).length, 0);
    return `<button class="${i === tab ? 'on' : ''}" data-i="${i}">${esc(t.title)}  ${m}/${n}</button>`;
  }).join('');
  document.querySelectorAll('#tabs button').forEach(b => b.onclick = () => { tab = +b.dataset.i; picked = null; plus = false; draw(); });
}
function drawShelf() {
  const t = TABS[tab];
  document.getElementById('shelf').innerHTML = t.shelves.map((s, si) => {
    const shown = s.entries.filter(matches);
    if (!shown.length) return '';
    const head = s.name ? `<div class="heading"><b>${esc(s.name)}</b><span>${s.entries.filter(e => e.met).length}/${s.entries.length}</span><hr></div>` : '';
    return head + '<div class="grid">' + shown.map(e => {
      const on = picked && picked.id === e.id ? ' on' : '';
      const art = e.image ? `<img src="${e.image}" alt="">` : '<span class="none">·</span>';
      return `<div class="tile${on}${e.met ? '' : ' unmet'}" data-id="${esc(e.id)}"><div class="window">${art}</div><div class="name">${esc(e.name)}</div></div>`;
    }).join('') + '</div>';
  }).join('') || '<p style="color:var(--muted)">Nothing on this shelf matches.</p>';
  document.querySelectorAll('.tile').forEach(el => el.onclick = () => {
    picked = TABS[tab].shelves.flatMap(s => s.entries).find(e => e.id === el.dataset.id);
    plus = false;
    document.querySelectorAll('.tile.on').forEach(x => x.classList.remove('on'));
    el.classList.add('on');
    drawPlate();
  });
}
function portrait(e) {
  const kind = TABS[tab].kind;
  if (kind === 'Cards') {
    const cost = plus ? e.upgradedCost : e.cost;
    const rules = plus ? e.upgraded : e.prose;
    const art = e.image ? `<img src="${e.image}" alt="">` : '';
    const frame = FRAME ? `<img class="frame" src="${FRAME}" alt="">` : '';
    return `<div class="face"><div class="art">${art}</div>${frame}<div class="cost">${esc(cost ?? '0')}</div>` +
      `<div class="title" style="color:${e.titleColor}">${esc(e.name + (plus ? '+' : ''))}</div><div class="plaque">${esc(rules)}</div></div>`;
  }
  if (kind === 'Relics') {
    const border = e.frameWidth >= 3 ? `double ${e.frameWidth * 2}px ${e.frameEdge}` : `solid ${e.frameWidth}px ${e.frameEdge}`;
    return `<div class="relic" style="background:${e.frameGround};border:${border}">${e.image ? `<img src="${e.image}" alt="">` : ''}</div>`;
  }
  return `<div class="body">${e.image ? `<img src="${e.image}" alt="">` : '<span style="color:var(--muted)">no picture yet</span>'}</div>`;
}
function drawPlate() {
  const p = document.getElementById('plate');
  if (!picked) { p.innerHTML = '<div class="invite">Pick something off the shelf.</div>'; return; }
  const e = picked;
  let h = `<div class="portrait">${portrait(e)}</div>`;
  h += `<h2>${esc(e.name)}${plus ? '+' : ''}${e.upgraded ? `<button class="flip" id="flip">${plus ? 'as found' : 'upgraded'}</button>` : ''}</h2>`;
  if (!e.met) h += '<div class="unmetnote">Not met yet in this save.</div>';
  if (e.prose) h += (e.proseTitle ? `<div class="decree">${esc(e.proseTitle)}</div>` : '') + `<div class="rules">${esc(e.prose)}</div>`;
  if (e.upgraded) h += `<div class="mark">Upgraded</div><div class="rules">${esc(e.upgraded)}</div>`;
  if (e.facts.length) h += '<table>' + e.facts.map(f => `<tr><td>${esc(f[0])}</td><td>${esc(f[1])}</td></tr>`).join('') + '</table>';
  if (e.moves.length) h += '<div class="moves">Moves' + e.moves.map(m => `<div>· ${esc(m)}</div>`).join('') + '</div>';
  p.innerHTML = h;
  const flip = document.getElementById('flip');
  if (flip) flip.onclick = () => { plus = !plus; drawPlate(); };
}
function draw() { drawTabs(); drawShelf(); drawPlate(); }
document.getElementById('find').oninput = ev => { find = ev.target.value.trim().toLowerCase(); drawShelf(); };
draw();
</script>
</body>
</html>
""");
        return sb.ToString();
    }
}
