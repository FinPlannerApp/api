namespace Application.Common.Helpers;

/// <summary>One definition of how long a delete can be undone, shared by the restore rule and the Undo list.</summary>
public static class UndoRules
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
}
