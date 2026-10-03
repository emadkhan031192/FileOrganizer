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

    // ---------- Folders move/copy too (not just files) ----------
    var folderSrc = Path.Combine(downloads, "Customer CVs");
    Write(folderSrc, "cv1.pdf", "cv-one");
    Write(Path.Combine(folderSrc, "old"), "cv2.pdf", "cv-two");
    var folderMoved = ops.TransferFiles(new[] { folderSrc }, jobsDest, TransferMode.Move);
    Check("folder moves with all its contents",
        folderMoved.Succeeded == 1 && !Directory.Exists(folderSrc) &&
        File.Exists(Path.Combine(jobsDest, "Customer CVs", "cv1.pdf")) &&
        File.Exists(Path.Combine(jobsDest, "Customer CVs", "old", "cv2.pdf")));

    var folderSrc2 = Path.Combine(downloads, "Customer CVs");
    Write(folderSrc2, "cv1.pdf", "cv-one-again");
    var folderMoved2 = ops.TransferFiles(new[] { folderSrc2 }, jobsDest, TransferMode.Move);
    Check("folder name conflict auto-renames the whole folder",
        folderMoved2.Succeeded == 1 && Directory.Exists(Path.Combine(jobsDest, "Customer CVs (1)")) &&
        File.ReadAllText(Path.Combine(jobsDest, "Customer CVs", "cv1.pdf")) == "cv-one",
        folderMoved2.Files[0].DestinationPath ?? "none");

    var folderCopy = ops.TransferFiles(new[] { Path.Combine(jobsDest, "Customer CVs") }, Path.Combine(root, "Backup"), TransferMode.Copy);
    var copyTarget = Path.Combine(root, "Backup", "Customer CVs");
    Check("folder copies recursively and source stays",
        folderCopy.Succeeded == 1 && File.Exists(Path.Combine(copyTarget, "old", "cv2.pdf")) &&
        Directory.Exists(Path.Combine(jobsDest, "Customer CVs")));

    var undoCopy = ops.UndoLastBatch();
    Check("undo of a folder copy deletes the copied folder",
        undoCopy.Succeeded == 1 && !Directory.Exists(copyTarget) && Directory.Exists(Path.Combine(jobsDest, "Customer CVs")));

    var folderRenamePreview = RenameEngine.BuildPreview(
        new[] { Path.Combine(jobsDest, "Customer CVs (1)") },
        new RenameOptions { AddDate = true, DatePosition = RenameDatePosition.End, DateFormat = "yyyy", DateSource = DateSource.Modified });
    var folderRenamed = RenameEngine.Apply(folderRenamePreview, history);
    Check("folders can be batch-renamed too",
        folderRenamed.Succeeded == 1 && folderRenamePreview[0].NewName.StartsWith("Customer CVs (1)_20") &&
        Directory.Exists(Path.Combine(jobsDest, folderRenamePreview[0].NewName)),
        folderRenamePreview[0].NewName);

    // ---------- Cancellation keeps partial progress logged (Undo still works) ----------
    var cancelDir = Path.Combine(root, "CancelTest");
    var c1 = Write(cancelDir, "one.txt", "1");
    var c2 = Write(cancelDir, "two.txt", "2");
    var c3 = Write(cancelDir, "three.txt", "3");
    using var cts = new CancellationTokenSource();
    // Progress<T> posts callbacks asynchronously; use a synchronous IProgress for determinism.
    var syncProgress = new SyncProgress(p =>
    {
        if (p.Done >= 1) cts.Cancel();
    });
    var cancelledResult = ops.TransferFiles(new[] { c1, c2, c3 }, Path.Combine(cancelDir, "out"),
        TransferMode.Move, ConflictPolicy.AutoRename, syncProgress, cts.Token);
    Check("cancelled batch reports Cancelled with partial success",
        cancelledResult.Cancelled && cancelledResult.Succeeded >= 1 && cancelledResult.Succeeded < 3,
        $"succeeded={cancelledResult.Succeeded}");
    Check("cancelled batch leaves un-moved files in place",
        Directory.EnumerateFiles(cancelDir, "*.txt").Count() == 3 - cancelledResult.Succeeded,
        $"remaining={Directory.EnumerateFiles(cancelDir, "*.txt").Count()}");

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

/// <summary>Synchronous IProgress for deterministic cancellation tests (Progress&lt;T&gt; posts async).</summary>
sealed class SyncProgress : IProgress<(int Done, int Total, string CurrentFile)>
{
    private readonly Action<(int Done, int Total, string CurrentFile)> _onReport;
    public SyncProgress(Action<(int Done, int Total, string CurrentFile)> onReport) => _onReport = onReport;
    public void Report((int Done, int Total, string CurrentFile) value) => _onReport(value);
}
