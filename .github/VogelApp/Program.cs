using System.Text.RegularExpressions;
using Spectre.Console;
using SkiaSharp;

var listNames = new Dictionary<string, string> {
    {"Anime","Anime.txt"},
    {"Anime Movies","Anime_movies.txt"},
    {"Books","Books.txt"},
    {"Comic","Comic.txt"},
    {"Currently Reading","Currently_reading.txt"},
    {"Currently Watching","Currently_watching.txt"},
    {"Dropped","Dropped.txt"},
    {"Manga","Manga.txt"},
    {"Movies","Movies.txt"},
    {"Light Novel","Light_novel.txt"},
};

Console.WriteLine($"Current: {Directory.GetCurrentDirectory()}");
var input = AnsiConsole.Ask<string>("Path to lists folder [Enter = current]:");
string basePath = string.IsNullOrWhiteSpace(input)? Directory.GetCurrentDirectory() : input;
if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);
Directory.CreateDirectory(Path.Combine(basePath, "fonts"));
Directory.CreateDirectory(Path.Combine(basePath, "exports"));

Dictionary<string,string> GetLists() => listNames
   .Where(kv => File.Exists(Path.Combine(basePath, kv.Value)))
   .ToDictionary(kv => kv.Key, kv => Path.Combine(basePath, kv.Value))
   .Concat(listNames.Where(kv =>!File.Exists(Path.Combine(basePath, kv.Value)))
       .Select(kv => { File.Create(Path.Combine(basePath, kv.Value)).Close(); return kv; })
       .ToDictionary(kv => kv.Key, kv => Path.Combine(basePath, kv.Value))
    ).ToDictionary(kv=>kv.Key, kv=>kv.Value);

List<string> GetAllYears() {
    var years = new HashSet<string>();
    foreach(var p in GetLists().Values){
        var txt = File.ReadAllText(p);
        foreach(Match m in Regex.Matches(txt, @"\b(19\d{2}|20\d{2})\b")) years.Add(m.Value);
    }
    var list = years.OrderByDescending(x=>x).ToList();
    return list.Count>0? list : new List<string>{"2026"};
}

string? PickYearManual() {
    var years = GetAllYears();
    var opts = years.Concat(new[]{"[Type year manually]"}).ToList();
    var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Pick year").AddChoices(opts));
    if(choice == "[Type year manually]"){
        var y = AnsiConsole.Ask<string>("Enter year (e.g. 2026):");
        return Regex.IsMatch(y, @"^\d{4}$")? y : null;
    }
    return choice;
}

SKTypeface? PickFont() {
    var fontsDir = Path.Combine(basePath, "fonts");
    var fonts = Directory.GetFiles(fontsDir, "*.ttf").Concat(Directory.GetFiles(fontsDir, "*.otf")).ToList();
    if(fonts.Count==0) return null;
    var names = fonts.Select(Path.GetFileName).Concat(new[]{"[Use default]"}).ToList();
    var sel = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Pick font").AddChoices(names));
    if(sel=="[Use default]") return null;
    return SKTypeface.FromFile(fonts[names.IndexOf(sel)]);
}

void CreateImage(string text, string fileName, SKTypeface? fontType, string title=""){
    var exportDir = Path.Combine(basePath, "exports");
    Directory.CreateDirectory(exportDir);
    var outPath = Path.Combine(exportDir, fileName);

    var lines = new List<string>();
    foreach(var l in text.Split('\n')){
        if(string.IsNullOrWhiteSpace(l)) lines.Add("");
        else {
            // simple wrap 55 chars
            var wrapped = Enumerable.Range(0, (l.Length+54)/55).Select(i=> l.Substring(i*55, Math.Min(55, l.Length - i*55))).ToList();
            lines.AddRange(wrapped);
        }
    }
    if(lines.Count==0) lines.Add("[empty]");

    using var titlePaint = new SKPaint{ Color=SKColors.White, TextSize=38, Typeface=fontType, IsAntialias=true };
    using var paint = new SKPaint{ Color=SKColors.White, TextSize=28, Typeface=fontType, IsAntialias=true };

    int w = 800;
    int lineH = 36;
    int h = 140 + lines.Count*lineH;
    using var bmp = new SKBitmap(w, h);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(new SKColor(24,24,28));
    int y = 60;
    if(!string.IsNullOrEmpty(title)){
        canvas.DrawText(title, 40, y, titlePaint);
        y+=50;
    }
    foreach(var line in lines){
        canvas.DrawText(line, 40, y, paint);
        y+=lineH;
    }
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.OpenWrite(outPath);
    data.SaveTo(fs);
    AnsiConsole.MarkupLine($"[green]Saved -> {outPath}[/green]");
}

void EditMenu(string filePath){
    while(true){
        var opt = AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"Edit {Path.GetFileName(filePath)}").AddChoices(new[]{"Add line","Remove line","Edit line","Move to section","Back"}));
        if(opt=="Back") break;
        var raw = File.ReadAllLines(filePath).ToList();

        List<string> display = raw.Select((l,i)=> string.IsNullOrWhiteSpace(l)? $"[empty {i}]" : l.Length>80? l[..80] : l).ToList();

        if(opt=="Add line"){
            var secs = raw.Where(l=>l.StartsWith("##")).ToList();
            var menu = secs.Concat(new[]{"[Bottom]"}).ToList();
            var where = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Add where?").AddChoices(menu));
            var newLine = AnsiConsole.Ask<string>("New line:");
            if(string.IsNullOrWhiteSpace(newLine)) continue;
            if(where=="[Bottom]") raw.Add(newLine);
            else raw.Insert(raw.IndexOf(where)+1, newLine);
            File.WriteAllLines(filePath, raw);
        }
        else if(opt=="Remove line"){
            if(display.Count==0) continue;
            var toRem = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Remove which?").AddChoices(display));
            raw.RemoveAt(display.IndexOf(toRem));
            File.WriteAllLines(filePath, raw);
        }
        else if(opt=="Edit line"){
            var toEdit = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Edit which?").AddChoices(display));
            var idx = display.IndexOf(toEdit);
            AnsiConsole.WriteLine($"Old: {raw[idx]}");
            var newLine = AnsiConsole.Ask<string>("New text:");
            if(string.IsNullOrWhiteSpace(newLine)) continue;
            raw[idx]=newLine;
            File.WriteAllLines(filePath, raw);
        }
        else if(opt=="Move to section"){
            var toMove = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Move which?").AddChoices(display));
            var idx = display.IndexOf(toMove);
            var secs = raw.Where(l=>l.StartsWith("##")).ToList();
            if(secs.Count==0){ AnsiConsole.WriteLine("No ## sections"); continue; }
            var dest = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Move to?").AddChoices(secs));
            var item = raw[idx];
            raw.RemoveAt(idx);
            var destIdx = raw.IndexOf(dest);
            raw.Insert(destIdx+1, item);
            File.WriteAllLines(filePath, raw);
        }
    }
}

void ListMenu(){
    var lists = GetLists();
    var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Which list?").AddChoices(lists.Keys));
    var path = lists[choice];
    while(true){
        var opt = AnsiConsole.Prompt(new SelectionPrompt<string>().Title(choice).AddChoices(new[]{"Show full","Show filtered by year","Edit/Add","Export FULL to image","Export FILTERED to image","Back"}));
        if(opt=="Back") break;
        if(opt=="Show full") AnsiConsole.WriteLine(File.ReadAllText(path));
        else if(opt=="Show filtered by year"){
            var y = PickYearManual(); if(y==null) continue;
            foreach(var l in File.ReadAllLines(path)) if(l.Contains(y) || l.StartsWith("##")) AnsiConsole.WriteLine(l);
        }
        else if(opt=="Edit/Add") EditMenu(path);
        else if(opt=="Export FULL to image"){
            CreateImage(File.ReadAllText(path), $"{Path.GetFileNameWithoutExtension(path)}.png", PickFont(), choice);
        }
        else if(opt=="Export FILTERED to image"){
            var y = PickYearManual(); if(y==null) continue;
            var filt = File.ReadAllLines(path).Where(l=>l.Contains(y) || l.StartsWith("##")).ToList();
            if(filt.Count==0){ AnsiConsole.MarkupLine("[red]No matches[/red]"); continue; }
            var month = DateTime.Now.ToString("MMMM");
            CreateImage($"[{month} {y}]\n\n"+string.Join("\n", filt), $"{Path.GetFileNameWithoutExtension(path)}_{y}.png", PickFont(), $"{choice} - {y}");
        }
    }
}

// Main loop
while(true){
    var main = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Vogel App").AddChoices(new[]{"Manage single list","Show ALL lists","Export ALL full to images","Export ALL filtered to ONE image","Quit"}));
    if(main=="Quit") break;
    if(main=="Manage single list") ListMenu();
    else if(main=="Show ALL lists"){
        foreach(var kv in GetLists()){ AnsiConsole.MarkupLine($"\n[cyan]## {kv.Key}[/cyan]\n{File.ReadAllText(kv.Value)}"); }
    }
    else if(main=="Export ALL full to images"){
        var font = PickFont();
        foreach(var kv in GetLists()){
            var txt = File.ReadAllText(kv.Value).Trim();
            if(!string.IsNullOrEmpty(txt)) CreateImage(txt, $"{Path.GetFileNameWithoutExtension(kv.Value)}.png", font, kv.Key);
        }
    }
    else if(main=="Export ALL filtered to ONE image"){
        var y = PickYearManual(); if(y==null) continue;
        var font = PickFont();
        var month = DateTime.Now.ToString("MMMM");
        var combined = new List<string>{ $"[{month} {y}]","" };
        foreach(var kv in GetLists()){
            var filt = File.ReadAllLines(kv.Value).Where(l=>l.Contains(y) || l.StartsWith("##")).ToList();
            if(filt.Count>0){ combined.Add($"## {kv.Key}"); combined.AddRange(filt); combined.Add(""); }
        }
        CreateImage(string.Join("\n", combined), $"ALL_{y}.png", font, $"ALL - {month} {y}");
    }
}
