using CodeSpace.Core;

namespace CodeSpace.Editor;

public sealed record DocumentEditBatch(EditorSession Session, IReadOnlyList<TextEdit> Edits, long? ExpectedVersion = null);

/// <summary>All-or-nothing text-only workspace edits. All buffers are committed before any observer is notified.</summary>
public static class WorkspaceEditTransaction
{
    public static int Apply(IEnumerable<DocumentEditBatch> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var prepared = new List<(EditorSession Session, TextBuffer Before, TextBuffer After, TextEdit[] Edits)>();
        var sessions = new HashSet<EditorSession>(); var files = new HashSet<WorkspaceFile>();
        foreach (var batch in documents)
        {
            ArgumentNullException.ThrowIfNull(batch.Session); ArgumentNullException.ThrowIfNull(batch.Edits);
            if (!sessions.Add(batch.Session) || !files.Add(batch.Session.File)) throw new ArgumentException("A document occurs more than once in the transaction.");
            if (batch.ExpectedVersion.HasValue && batch.Session.Version != batch.ExpectedVersion.Value)
                throw new InvalidOperationException("Document version changed before the workspace edit could be applied.");
            var before = batch.Session.Buffer; var edits = batch.Edits.OrderBy(e => e.Start).ToArray();
            var after = before.Apply(edits);
            if (!ReferenceEquals(before, after)) prepared.Add((batch.Session, before, after, edits));
        }
        foreach (var item in prepared) item.Session.CommitPrepared(item.After, item.Edits);
        foreach (var item in prepared) item.Session.PublishPrepared(item.Before, item.Edits);
        return prepared.Count;
    }
}
