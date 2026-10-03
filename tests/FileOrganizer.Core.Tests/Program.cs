using FileOrganizer.Core;

// Dependency-free test runner for FileOrganizer.Core.
// Each check prints PASS/FAIL; the process exit code is the number of failures (capped).
// Run:  dotnet run --project tests/FileOrganizer.Core.Tests -c Release

var failures = 0;
void Check(string name, bool condition, string detail = "")
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!condition) failures++;
}

var root = Path.Combine(Path.GetTempPath(), "FileOrganizerTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine($"Test dir: {root}\n");

try
{
    // ---------- setup ----------
    var history = new HistoryService(Path.Combine(root, "history.json"));
    var ops = new FileOperationService(history);
    var cfgPath = Path.Combine(root, "config.json");
    var configService = new ConfigService(cfgPath);
    var config = configService.Load();

    Check("default config seeds Afzal destinations",
        config.Destinations.Any(d => d.Name == "Jobs") && config.Destinations.Any(d => d.Name == "Advertisements"));
    Check("default extension map maps pdf → Documents\\PDF",
        config.ExtensionMap.Any(m => m.Extension == "pdf" && m.RelativeFolder.Contains("PDF")));

    var downloads = Path.Combine(root, "Downloads");
    var jobsDest = Path.Combine(root, "Afzal E Services", "Jobs");
    Directory.CreateDirectory(downloads);

    string Write(string folder, string name, string content = "x")
    {
        var p = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    // ---------- Quick Move (§1/§15): move + auto-rename conflict + undo ----------
    var poster = Write(downloads, "poster.jpg", "image-bytes");
    var moved = ops.TransferFiles(new[] { poster }, jobsDest, TransferMode.Move);
    Check("quick move succeeds", moved.Succeeded == 1 && File.Exists(Path.Combine(jobsDest, "poster.jpg")));

    var poster2 = Write(downloads, "poster.jpg", "image-bytes-2");
    var moved2 = ops.TransferFiles(new[] { poster2 }, jobsDest, TransferMode.Move);
    var renamedTarget = moved2.Files[0].DestinationPath ?? "";
    Check("name conflict auto-renames, never overwrites",
        moved2.Succeeded == 1 && renamedTarget.EndsWith("poster (1).jpg") && File.ReadAllText(Path.Combine(jobsDest, "poster.jpg")) == "image-bytes",
        renamedTarget);

    var poster3 = Write(downloads, "poster.jpg", "image-bytes-3");
    var skipped = ops.TransferFiles(new[] { poster3 }, jobsDest, TransferMode.Move, ConflictPolicy.Skip);
    Check("conflict Skip leaves source in place", skipped.SkippedCount == 1 && File.Exists(poster3));

    var undone = ops.UndoLastBatch(); // undoes moved2's batch; downloads\poster.jpg is taken again by then, so it must come back auto-renamed
    Check("undo returns the file and logs it",
        undone.Succeeded == 1 && !File.Exists(Path.Combine(jobsDest, "poster (1).jpg")) &&
        File.ReadAllText(Path.Combine(downloads, "poster (1).jpg")) == "image-bytes-2",
        string.Join(", ", Directory.EnumerateFiles(downloads, "poster*").Select(Path.GetFileName)));

    // ---------- Auto Organize (§3/§10): preview then execute ----------
    var messy = Path.Combine(root, "Messy");
    Write(messy, "admission.pdf");
    Write(messy, "photo.png");
    Write(messy, "song.mp3");
    Write(messy, "movie.mp4");
    Write(messy, "archive.zip");
    Write(messy, "notes.txt");
    Write(messy, "My CV 2026.docx"); // matches the seeded "CVs to Jobs" rule

    var engine = new OrganizeEngine(config);
    var preview = engine.BuildPreview(messy);
    Check("preview covers all 7 files", preview.Count == 7, $"count={preview.Count}");
    Check("preview sends PDF to Documents\\PDF",
        preview.Any(p => p.FileName == "admission.pdf" && p.DestinationFolder.EndsWith(Path.Combine("Documents", "PDF"))));
    Check("preview sends PNG to Images",
        preview.Any(p => p.FileName == "photo.png" && p.DestinationFolder.EndsWith("Images")));
    Check("rule beats extension map (CV → Jobs\\CV)",
        preview.Any(p => p.FileName == "My CV 2026.docx" && p.Reason.StartsWith("Rule:") && p.DestinationFolder.EndsWith(Path.Combine("Jobs", "CV"))),
        preview.FirstOrDefault(p => p.FileName == "My CV 2026.docx")?.DestinationFolder ?? "missing");

    // date organization (§5)
    var datePreview = engine.BuildPreview(messy, recursive: false,
        new DateOrganizeOptions { Enabled = true, Source = DateSource.Modified });
    Check("date preview inserts Year\\Month",
        datePreview.All(p => p.DestinationFolder.Contains(DateTime.Now.Year.ToString()) || p.DestinationFolder.Contains("20")),
        datePreview.FirstOrDefault()?.DestinationFolder ?? "none");

    var applied = ops.ApplyPairs(
        preview.Where(p => p.Included).Select(p => (p.SourcePath, p.DestinationPath)),
        TransferMode.Move);
    Check("organize executes the confirmed preview", applied.Succeeded == 7 && applied.Failed == 0,
        $"ok={applied.Succeeded} failed={applied.Failed}");
    Check("organized files landed on disk",
        File.Exists(Path.Combine(messy, "Documents", "PDF", "admission.pdf")) &&
        File.Exists(Path.Combine(messy, "Jobs", "CV", "My CV 2026.docx")),
        string.Join(", ", Directory.EnumerateFiles(messy, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(messy, p))));

    // ---------- Custom rules AND/OR (§4) ----------
    var andRule = new OrganizeRule
    {
        Name = "Admission JPGs", Logic = RuleLogic.All, TargetRelativeFolder = @"Advertisements\Admission",
        Conditions =
        {
            new RuleCondition { Field = RuleField.Extension, Operator = RuleOperator.Equals, Value = "jpg" },
            new RuleCondition { Field = RuleField.FileName, Operator = RuleOperator.Contains, Value = "Admission" },
        },
    };
    var tmpInfo = new FileInfo(Write(downloads, "College Admission.jpg"));
    Check("AND rule matches when both conditions hold", RulesEngine.Matches(andRule, tmpInfo));
    var tmpInfo2 = new FileInfo(Write(downloads, "random.jpg"));
    Check("AND rule rejects partial match", !RulesEngine.Matches(andRule, tmpInfo2));

    // ---------- Batch Rename (§7) ----------
    var r1 = Write(downloads, "government college admission.jpg");
    var r2 = Write(downloads, "government college admission 2.jpg");
    // distinct names for numbering clarity:
    var rn1 = Write(downloads, "Photo.jpg"); var rn2 = Write(downloads, "Photo2.jpg"); var rn3 = Write(downloads, "Photo3.jpg");
    var renamePreview = RenameEngine.BuildPreview(new[] { r1 },
        new RenameOptions { Case = CaseMode.TitleCase });
    Check("Title Case rename preview",
        renamePreview[0].NewName == "Government College Admission.jpg", renamePreview[0].NewName);

    var numbered = RenameEngine.BuildPreview(new[] { rn1, rn2, rn3 },
        new RenameOptions { AddNumbering = true, NumberStart = 1, NumberPadding = 3, NumberSeparator = "_" });
    Check("numbering 001..003",
        numbered.Select(n => n.NewName).SequenceEqual(new[] { "Photo_001.jpg", "Photo2_002.jpg", "Photo3_003.jpg" }),
        string.Join(", ", numbered.Select(n => n.NewName)));

    var withDate = RenameEngine.BuildPreview(new[] { r1 },
        new RenameOptions { AddDate = true, DatePosition = RenameDatePosition.End, DateFormat = "yyyy-MM-dd", DateSource = DateSource.Modified });
    Check("add-date suffix yyyy-MM-dd",
        withDate[0].NewName.StartsWith("government college admission_20") && withDate[0].NewName.EndsWith(".jpg"),
        withDate[0].NewName);

    var renamed = RenameEngine.Apply(renamePreview, history);
    Check("rename applies + is logged for undo", renamed.Succeeded == 1 &&
        File.Exists(Path.Combine(downloads, "Government College Admission.jpg")),
        $"{renamed.Files[0].Error ?? renamed.Files[0].DestinationPath ?? "no result"} | downloads: {string.Join(", ", Directory.EnumerateFiles(downloads).Select(Path.GetFileName))}");

    // ---------- Duplicates (§8) ----------
    var dupDir = Path.Combine(root, "Dups");
    Write(dupDir, "a.bin", "same-content");
    Write(dupDir, Path.Combine("sub", "b.bin"), "same-content");
    Write(dupDir, "c.bin", "same-content");
    Write(dupDir, "unique.bin", "different");
    var groups = DuplicateFinder.FindDuplicates(dupDir);
    Check("duplicate finder groups the 3 identical files",
        groups.Count == 1 && groups[0].Files.Count == 3, $"groups={groups.Count}");
    Check("keeper strategies return a file in the group",
        groups.Count == 1 && groups[0].Files.Contains(DuplicateFinder.PickKeeper(groups[0], DuplicateKeepStrategy.KeepNewest)));

    // ---------- Search (§9) ----------
    var hits = SearchService.Search(root, new SearchQuery { NameContains = "admission" }).ToList();
    Check("search finds admission files", hits.Count >= 2, $"hits={hits.Count}");
    var pdfHits = SearchService.Search(root, new SearchQuery { NameContains = "admission", Extension = "pdf" }).ToList();
    Check("search filters by extension", pdfHits.Count == 1 && pdfHits[0].Name == "admission.pdf");

    // ---------- Config export/import (§16) ----------
    var exportPath = Path.Combine(root, "export.json");
    configService.Export(config, exportPath);
    var imported = configService.Import(exportPath);
    Check("config export/import round-trips",
        imported.Destinations.Count == config.Destinations.Count && imported.Rules.Count == config.Rules.Count);
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup is best-effort */ }
}

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
return Math.Min(failures, 125);
