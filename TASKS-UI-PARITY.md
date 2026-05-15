# UI Parity Tasks — Match wolffiles.eu Web Upload Form

> Goal: bring the desktop app's per-file upload form to feature parity with the website's `/upload` page, while preserving the multi-file queue advantage.

---

## Current state (as of 2026-05-14)

**Already implemented:**
- ✅ Title, Description, Category (dropdown with parent→child flattening)
- ✅ Version & Author (in `UploadItem` model + `WolffilesApiService.UploadFileAsync`, but **not rendered in XAML**)
- ✅ Single screenshot (in model + service, **not rendered in XAML**)
- ✅ Drag & drop, multi-file picker, per-file card, queue, progress, history

**Missing vs. web form (screenshot reference: `/upload` page on wolffiles.eu):**
- ❌ Game field (Auto-detect / RtCW / ET)
- ❌ Tags — 5 groups + custom input
- ❌ Multi-screenshots (max 10) per file
- ❌ Review-info hint box

---

## Phase A — Surface what's already in the model

**Goal:** make Version, Author, and (single) Screenshot visible in the per-file card. Quick win, no API/model changes needed.

### A.1 — Add Version + Author fields to UploadQueuePage.xaml

In `Views/UploadQueuePage.xaml`, find the `<Grid ColumnSpacing="8" RowSpacing="8">` inside the DataTemplate (the form grid). Add a new row between the Category row and the Description row:

```xaml
<!-- Version + Author row -->
<StackPanel Grid.Row="1" Grid.Column="0" Spacing="4">
    <TextBlock Text="VERSION" FontSize="10" FontFamily="Consolas"
               Foreground="#424860" CharacterSpacing="80" />
    <TextBox Text="{x:Bind Version, Mode=TwoWay}"
             PlaceholderText="z.B. 1.0, final, beta2" />
</StackPanel>
<StackPanel Grid.Row="1" Grid.Column="1" Spacing="4">
    <TextBlock Text="ORIGINAL AUTHOR" FontSize="10" FontFamily="Consolas"
               Foreground="#424860" CharacterSpacing="80" />
    <TextBox Text="{x:Bind Author, Mode=TwoWay}"
             PlaceholderText="Map/Mod creator" />
</StackPanel>
```

Then shift the existing Description row to `Grid.Row="2"` and add a `<RowDefinition Height="Auto" />` to `Grid.RowDefinitions`.

Add matching `.resw` keys: `Field_Version`, `Field_VersionPlaceholder`, `Field_Author`, `Field_AuthorPlaceholder` for all three languages.

### A.2 — Remove the debug ContentDialog in UploadQueuePage.xaml.cs

In `BrowseFiles_Click`, delete the `var dlg = new ContentDialog { Title = "Debug Categories", ... }; await dlg.ShowAsync();` block.

### A.3 — Remove dead ProgressableContent class

Delete the `ProgressableContent` class at the bottom of `Services/WolffilesApiService.cs`. It's unused and the comment about boundary header corruption explains why.

---

## Phase B — Add Game dropdown

### B.1 — Model
Add to `Models/UploadItem`:
```csharp
[ObservableProperty] private string _game = "auto"; // "auto" | "et" | "rtcw"
```

### B.2 — API service
Add to the multipart form in `UploadFileAsync`:
```csharp
form.Add(new StringContent(item.Game ?? "auto"), "game");
```

### B.3 — XAML
Below the Category dropdown, add a Game dropdown:
```xaml
<StackPanel Grid.Row="0" Grid.Column="2" Spacing="4">
    <TextBlock Text="GAME" .../>
    <ComboBox SelectedValue="{x:Bind Game, Mode=TwoWay}" SelectedValuePath="Tag">
        <ComboBoxItem Content="Auto-detect" Tag="auto" />
        <ComboBoxItem Content="Enemy Territory" Tag="et" />
        <ComboBoxItem Content="Return to Castle Wolfenstein" Tag="rtcw" />
    </ComboBox>
</StackPanel>
```
(Adjust column definitions to accommodate the third column.)

### B.4 — Laravel side
In `app/Http/Controllers/Api/FileUploadApiController.php`, add to `validate()`:
```php
'game' => 'nullable|in:auto,et,rtcw',
```
Persist if your `File` model has a `game_type` column; otherwise let auto-detect run server-side as before.

---

## Phase C — Tag system (5 groups)

### C.1 — Define tag groups as static data
Create `Models/UploadTags.cs`:
```csharp
namespace WolffilesUploader.Models;

public static class UploadTags
{
    public static readonly Dictionary<string, string[]> Groups = new()
    {
        ["map_type"] = ["objective", "frag", "trickjump", "deathmatch", "ctf", "lms", "single_player"],
        ["style"]    = ["sniper", "panzer", "rifle", "smg", "cqb", "vehicle", "indoor", "outdoor"],
        ["size"]     = ["small", "medium", "large", "xl"],
        ["theme"]    = ["ww2", "desert", "snow", "urban", "forest", "beach", "night", "custom"],
        ["quality"]  = ["final", "beta", "alpha", "fun", "competitive", "tournament"],
    };
}
```

### C.2 — UploadItem additions
```csharp
public ObservableCollection<string> Tags { get; } = [];
public string CustomTagInput { get; set; } = "";
```

### C.3 — XAML — render tag groups as toggle-pills
Add a new section between the form grid and the bottom of the card. For each group, use a `ItemsControl` with `WrapPanel` ItemsPanelTemplate and a `ToggleButton` template that toggles membership in `Tags`.

Style the ToggleButton:
- Unchecked: `Background=#1A1D2A`, `BorderBrush=#22263A`, `Foreground=#A0A4B8`
- Checked: `Background=#1A1800`, `BorderBrush=AccentGold`, `Foreground=AccentGold`
- CornerRadius=`12`, Padding=`10,4`

Pill labels are localized — add `.resw` keys like `Tag_objective`, `Tag_frag`, etc. (one per tag value across all 5 groups).

Group headers: `MAP TYPE`, `STYLE`, `SIZE`, `THEME`, `QUALITY` — small caps style matching existing field labels.

### C.4 — Custom tag input row
After the 5 groups:
```xaml
<Grid ColumnSpacing="8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBox Grid.Column="0" PlaceholderText="Custom tag..."
             Text="{x:Bind CustomTagInput, Mode=TwoWay}" />
    <Button Grid.Column="1" Content="Add" Click="AddCustomTag_Click" />
</Grid>
```
Handler appends to `Tags` if non-empty and not already present, then clears `CustomTagInput`.

### C.5 — API service
```csharp
foreach (var tag in item.Tags)
    form.Add(new StringContent(tag), "tags[]");
```

### C.6 — Laravel side
Add `tags` to validation (`'tags' => 'array'`, `'tags.*' => 'string|max:50'`) and persist into your `files` ↔ `tags` pivot table (or `tags` JSON column, depending on your schema).

---

## Phase D — Multi-screenshots (max 10)

### D.1 — Model
Replace `ScreenshotPath` (single) with:
```csharp
public ObservableCollection<string> ScreenshotPaths { get; } = [];
```
Keep the old field as `[Obsolete]` for one release if other code references it.

### D.2 — XAML — picker + thumbnails
Below the tags section:
```xaml
<StackPanel Spacing="6">
    <TextBlock Text="SCREENSHOTS (optional, max 10)" .../>
    <Grid ColumnSpacing="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Button Grid.Column="0" Content="Choose images"
                Click="ChooseScreenshots_Click"
                Style="{StaticResource AccentButtonStyle}" />
        <TextBlock Grid.Column="1" VerticalAlignment="Center"
                   Text="{x:Bind ScreenshotPaths.Count, Mode=OneWay,
                          Converter={StaticResource CountToScreenshotsLabelConverter}}" />
    </Grid>
    <!-- Thumbnail strip -->
    <ItemsControl ItemsSource="{x:Bind ScreenshotPaths, Mode=OneWay}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="6" /></ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Width="80" Height="60">
                    <Image Source="{Binding}" Stretch="UniformToFill" />
                    <Button Content="✕" Width="20" Height="20"
                            HorizontalAlignment="Right" VerticalAlignment="Top"
                            Click="RemoveScreenshot_Click" Tag="{Binding}" />
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

### D.3 — Code-behind handlers
```csharp
private async void ChooseScreenshots_Click(object sender, RoutedEventArgs e)
{
    if ((sender as Button)?.DataContext is not UploadItem item) return;
    var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
    foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" }) picker.FileTypeFilter.Add(ext);
    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
    var files = await picker.PickMultipleFilesAsync();
    foreach (var f in files)
    {
        if (item.ScreenshotPaths.Count >= 10) break;
        if (!item.ScreenshotPaths.Contains(f.Path)) item.ScreenshotPaths.Add(f.Path);
    }
}

private void RemoveScreenshot_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: string path, DataContext: UploadItem item })
        item.ScreenshotPaths.Remove(path);
}
```

### D.4 — API service
Replace the single-screenshot block with:
```csharp
for (int i = 0; i < item.ScreenshotPaths.Count; i++)
{
    var path = item.ScreenshotPaths[i];
    if (!File.Exists(path)) continue;
    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
    var content = new ByteArrayContent(bytes);
    var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    content.Headers.ContentType = new MediaTypeHeaderValue($"image/{(ext == "jpg" ? "jpeg" : ext)}");
    form.Add(content, "screenshots[]", Path.GetFileName(path));
}
```

### D.5 — Laravel side
In `FileUploadApiController::store`:
```php
'screenshots'   => 'nullable|array|max:10',
'screenshots.*' => 'image|mimes:jpg,jpeg,png,webp|max:10240', // 10 MB each
```
Loop and save each into Hetzner S3, then store paths in a `file_screenshots` table (or JSON column).

---

## Phase E — Review-info hint

Add a `<Border>` above the bottom of each card (or globally above the queue list) with the localized text:

> "After uploading, our team will review your file. We'll scan for viruses, verify the content, and may adjust the description or images before publishing. You'll be notified once it's approved."

Localize via `.resw` key `Upload_ReviewNotice`.

---

## How to use this with Claude Code

1. Drop both `CLAUDE.md` and this `TASKS-UI-PARITY.md` into the repo root and commit.
2. Open a Claude Code session in `C:\Dev\wolffiles-uploader`.
3. Prompt: **"Read CLAUDE.md and TASKS-UI-PARITY.md, then work through Phase A. Pause after each phase for me to test."**
4. After each phase, run `dotnet build -c Debug -p:Platform=x64` and `dotnet run -c Debug -p:Platform=x64` to verify.
5. When all phases pass, commit and push.

---

## Phase order rationale

A first → instant visible progress with zero risk (model + API already support it).
B–C–D each add one cohesive feature; can be paused between phases.
E is cosmetic — last.

Don't tackle them out of order; later phases depend on the form grid structure introduced in A.
