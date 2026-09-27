using CodeSpace.Extensions;
using CodeSpace.Workbench.Uno;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace CodeSpace.App;

internal sealed class WorkbenchPlatform : IWorkbenchPlatform
{
    public bool IsBrowser => OperatingSystem.IsBrowser();
#if __WASM__
    public IExtensionBridge? ExtensionBridge { get; } = new BrowserExtensionBridge();
#else
    public IExtensionBridge? ExtensionBridge { get; } = new DesktopExtensionBridge();
#endif
    public async Task<IReadOnlyList<ImportedFile>> PickFilesAsync(string extension = "*")
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension); var files = await picker.PickMultipleFilesAsync(); var result = new List<ImportedFile>();
        foreach (var file in files)
        {
            var properties = await file.GetBasicPropertiesAsync(); if (properties.Size > 100 * 1024 * 1024) throw new IOException("Import is limited to 100 MiB per file.");
            var buffer = await FileIO.ReadBufferAsync(file); var bytes = new byte[buffer.Length]; using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
            result.Add(new ImportedFile(file.Name, bytes));
        }
        return result;
    }
    public async Task SaveFileAsync(string name, byte[] content)
    {
        var picker = new FileSavePicker { SuggestedFileName = name, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        var extension = Path.GetExtension(name); picker.FileTypeChoices.Add("File", new List<string> { extension.Length == 0 ? ".txt" : extension });
        var file = await picker.PickSaveFileAsync(); if (file is null) throw new OperationCanceledException(); await FileIO.WriteBytesAsync(file, content);
    }
    public async Task<string?> LoadRecoveryAsync()
    {
        var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync("codespace-recovery.json") as StorageFile;
        return file is null ? null : await FileIO.ReadTextAsync(file);
    }
    public async Task SaveRecoveryAsync(string workspaceJson)
    {
        var folder = ApplicationData.Current.LocalFolder;
        // Write a separate file before replacing recovery, to avoid truncating the last backup on a failed write.
        var temporary = await folder.CreateFileAsync("codespace-recovery.pending.json", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(temporary, workspaceJson);
        await temporary.RenameAsync("codespace-recovery.json", NameCollisionOption.ReplaceExisting);
    }
}
